#!/usr/bin/env python3
"""Validate release dependencies and generate deterministic compliance artifacts."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


class ComplianceError(RuntimeError):
    """Raised when a release artifact violates the committed policy."""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha512(path: Path) -> str:
    digest = hashlib.sha512()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as error:
        raise ComplianceError(f"Cannot read JSON {path}: {error}") from error


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def split_library_key(key: str) -> tuple[str, str]:
    try:
        package_id, version = key.rsplit("/", 1)
    except ValueError as error:
        raise ComplianceError(f"Invalid .deps.json library key: {key}") from error
    return package_id, version


def local_name(element: ET.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def child(element: ET.Element, name: str) -> ET.Element | None:
    return next((item for item in element if local_name(item) == name), None)


def text_of(element: ET.Element, name: str) -> str:
    item = child(element, name)
    return (item.text or "").strip() if item is not None else ""


def package_root(nuget_packages: Path, package_id: str, version: str) -> Path:
    root = nuget_packages / package_id.lower() / version
    if not root.is_dir():
        raise ComplianceError(
            f"NuGet package is missing from the restored cache: {package_id}/{version}"
        )
    return root


def read_nuspec(root: Path) -> tuple[ET.Element, Path]:
    files = sorted(root.glob("*.nuspec"))
    if len(files) != 1:
        raise ComplianceError(
            f"Expected one nuspec in {root}, found {len(files)}"
        )
    try:
        package = ET.parse(files[0]).getroot()
    except (OSError, ET.ParseError) as error:
        raise ComplianceError(f"Cannot parse {files[0]}: {error}") from error
    metadata = child(package, "metadata")
    if metadata is None:
        raise ComplianceError(f"NuSpec has no metadata element: {files[0]}")
    return metadata, files[0]


def validate_license_metadata(
    package_id: str,
    component: dict,
    root: Path,
) -> None:
    metadata, nuspec = read_nuspec(root)
    declared_id = text_of(metadata, "id")
    declared_version = text_of(metadata, "version")
    if declared_id.casefold() != package_id.casefold():
        raise ComplianceError(
            f"NuSpec ID mismatch for {package_id}: {declared_id} ({nuspec})"
        )
    if declared_version != component["version"]:
        raise ComplianceError(
            f"NuSpec version mismatch for {package_id}: {declared_version}"
        )

    license_element = child(metadata, "license")
    expected_expression = component.get("metadataLicense")
    expected_file = component.get("metadataLicenseFile")
    if expected_expression is not None:
        if license_element is None:
            raise ComplianceError(f"{package_id} no longer declares a license")
        actual_type = license_element.attrib.get("type", "")
        actual_value = (license_element.text or "").strip()
        if actual_type != "expression" or actual_value != expected_expression:
            raise ComplianceError(
                f"License drift for {package_id}: "
                f"expected expression {expected_expression}, "
                f"got {actual_type}:{actual_value}"
            )
    elif expected_file is not None:
        if license_element is None:
            raise ComplianceError(f"{package_id} no longer declares a license file")
        actual_type = license_element.attrib.get("type", "")
        actual_value = (license_element.text or "").strip()
        if actual_type != "file" or actual_value != expected_file:
            raise ComplianceError(
                f"License drift for {package_id}: "
                f"expected file {expected_file}, got {actual_type}:{actual_value}"
            )
        license_path = root / expected_file
        validate_evidence_hash(
            license_path,
            component["metadataLicenseFileSha256"],
            f"license file for {package_id}",
        )
    else:
        raise ComplianceError(
            f"Policy has no NuGet license expectation for {package_id}"
        )


def validate_evidence_hash(path: Path, expected: str, label: str) -> bytes:
    if not path.is_file():
        raise ComplianceError(f"Missing {label}: {path}")
    actual = sha256(path)
    if actual != expected:
        raise ComplianceError(
            f"Unexpected content for {label}: expected {expected}, got {actual}"
        )
    return path.read_bytes()


def get_runtime_packages(deps: dict) -> dict[str, dict]:
    packages: dict[str, dict] = {}
    for key, details in deps.get("libraries", {}).items():
        if details.get("type") != "package":
            continue
        package_id, version = split_library_key(key)
        packages[key] = {
            "id": package_id,
            "version": version,
            "details": details,
        }
    return packages


def validate_runtime_packages(
    rid: str,
    runtime_packages: dict[str, dict],
    policy: dict,
    nuget_packages: Path,
) -> dict[str, dict]:
    expected = sorted(policy.get("rids", {}).get(rid, []), key=str.casefold)
    actual = sorted(runtime_packages, key=str.casefold)
    if actual != expected:
        missing = sorted(set(expected) - set(actual), key=str.casefold)
        unexpected = sorted(set(actual) - set(expected), key=str.casefold)
        raise ComplianceError(
            f"Runtime package baseline drift for {rid}; "
            f"missing={missing}, unexpected={unexpected}"
        )

    prefixes = [value.casefold() for value in policy["forbiddenPackagePrefixes"]]
    allowed = set(policy["allowedLicenses"])
    components = policy["components"]
    selected: dict[str, dict] = {}
    for key in actual:
        package = runtime_packages[key]
        package_id = package["id"]
        folded_id = package_id.casefold()
        if any(folded_id.startswith(prefix) for prefix in prefixes):
            raise ComplianceError(f"Forbidden runtime package: {key}")
        component = components.get(package_id)
        if component is None:
            raise ComplianceError(f"Runtime package is not approved: {key}")
        if package["version"] != component["version"]:
            raise ComplianceError(
                f"Approved version mismatch for {key}: {component['version']}"
            )
        licenses = component.get("licenses", [])
        if not licenses or any(license_id not in allowed for license_id in licenses):
            raise ComplianceError(
                f"Runtime package has an unapproved license policy: {key} {licenses}"
            )
        root = package_root(nuget_packages, package_id, package["version"])
        validate_license_metadata(package_id, component, root)
        selected[key] = {**package, "component": component, "root": root}
    return selected


def resolve_notice(
    notice: dict,
    policy: dict,
    selected: dict[str, dict],
    repository_root: Path,
) -> bytes:
    kind = notice["kind"]
    if kind == "repository":
        path = repository_root / notice["path"]
    elif kind == "package":
        package_id = notice["package"]
        package = next(
            (
                value
                for value in selected.values()
                if value["id"].casefold() == package_id.casefold()
            ),
            None,
        )
        if package is None:
            raise ComplianceError(
                f"Notice references a package outside the {package_id} RID graph"
            )
        path = package["root"] / notice["path"]
    else:
        raise ComplianceError(f"Unsupported notice source kind: {kind}")
    return validate_evidence_hash(path, notice["sha256"], f"notice {path.name}")


def generate_notice(
    selected: dict[str, dict],
    policy: dict,
    repository_root: Path,
) -> str:
    lines = [
        "GostEditor third-party software notices",
        "========================================",
        "",
        "This file is generated from the locked runtime dependency graph.",
        "It covers third-party components shipped with this RID publish.",
        "",
        "Runtime components",
        "------------------",
    ]
    for key, package in sorted(selected.items(), key=lambda item: item[0].casefold()):
        component = package["component"]
        lines.extend(
            [
                f"- {key}",
                f"  Licenses: {', '.join(component['licenses'])}",
                f"  Copyright: {component['copyright']}",
                f"  Source: {component['source']}",
            ]
        )

    mit_components = [
        package
        for package in selected.values()
        if "MIT" in package["component"]["licenses"]
    ]
    if mit_components:
        mit_notice = policy["mitLicenseNotice"]
        mit_text = resolve_notice(
            mit_notice,
            policy,
            selected,
            repository_root,
        ).decode("utf-8-sig").rstrip()
        lines.extend(
            [
                "",
                "MIT-licensed components",
                "-----------------------",
            ]
        )
        seen_copyrights: set[str] = set()
        for package in sorted(mit_components, key=lambda value: value["id"].casefold()):
            copyright_text = package["component"]["copyright"]
            if copyright_text not in seen_copyrights:
                lines.append(copyright_text)
                seen_copyrights.add(copyright_text)
        lines.extend(["", mit_text])

    notice_names = sorted(
        {
            notice_name
            for package in selected.values()
            for notice_name in package["component"].get("notices", [])
        },
        key=str.casefold,
    )
    seen_payloads: set[str] = set()
    for notice_name in notice_names:
        notice = policy["notices"].get(notice_name)
        if notice is None:
            raise ComplianceError(f"Unknown notice policy entry: {notice_name}")
        payload = resolve_notice(notice, policy, selected, repository_root)
        payload_hash = hashlib.sha256(payload).hexdigest()
        if payload_hash in seen_payloads:
            continue
        seen_payloads.add(payload_hash)
        lines.extend(
            [
                "",
                f"Notice: {notice_name}",
                "-" * (8 + len(notice_name)),
                payload.decode("utf-8-sig").rstrip(),
            ]
        )
    return "\n".join(lines) + "\n"


def target_for_rid(deps: dict, rid: str) -> dict:
    matching = [
        value
        for name, value in deps.get("targets", {}).items()
        if name.endswith(f"/{rid}")
    ]
    if len(matching) != 1:
        raise ComplianceError(
            f"Expected one .deps.json target for {rid}, found {len(matching)}"
        )
    return matching[0]


def map_published_assets(target: dict, rid: str) -> dict[str, dict[str, str]]:
    assets: dict[str, dict[str, str]] = {}

    def add_asset(
        path: str,
        owner: str,
        kind: str,
        relative: str | None = None,
    ) -> None:
        published_path = relative or Path(path).name
        value = {"origin": owner, "kind": kind}
        previous = assets.get(published_path)
        if previous is not None and previous != value:
            raise ComplianceError(
                f"Ambiguous publish asset for {published_path}: {previous}, {value}"
            )
        assets[published_path] = value

    for owner, details in target.items():
        for asset_path in details.get("runtime", {}):
            add_asset(asset_path, owner, "managed")
        for asset_path in details.get("native", {}):
            add_asset(asset_path, owner, "native")
        for asset_path, asset in details.get("resources", {}).items():
            locale = asset.get("locale")
            relative = f"{locale}/{Path(asset_path).name}" if locale else Path(asset_path).name
            add_asset(asset_path, owner, "resource", relative)
        for asset_path, asset in details.get("runtimeTargets", {}).items():
            if asset.get("rid") == rid:
                add_asset(
                    asset_path,
                    owner,
                    "native" if asset.get("assetType") == "native" else "runtime",
                )
    return assets


def application_file_kind(relative: str) -> str:
    lower = relative.casefold()
    if lower.endswith(".pdb"):
        return "symbols"
    if lower.endswith(".deps.json") or lower.endswith(".runtimeconfig.json"):
        return "runtime-metadata"
    if lower.endswith(".dll"):
        return "managed"
    return "app-host"


def inventory_files(
    publish_dir: Path,
    assets: dict[str, dict[str, str]],
) -> list[dict]:
    files: list[dict] = []
    for path in sorted(
        (item for item in publish_dir.rglob("*") if item.is_file()),
        key=lambda item: item.relative_to(publish_dir).as_posix().casefold(),
    ):
        relative = path.relative_to(publish_dir).as_posix()
        if relative == "THIRD-PARTY-NOTICES.txt":
            origin = "release-compliance"
            kind = "notice"
        else:
            asset = assets.get(relative)
            origin = asset["origin"] if asset is not None else None
            kind = asset["kind"] if asset is not None else None
        if origin is None or kind is None:
            lower = relative.casefold()
            is_application_file = lower.startswith("gosteditor.") or lower == "gosteditor.ui"
            if not is_application_file:
                raise ComplianceError(f"Published file has no dependency owner: {relative}")
            origin = "GostEditor"
            kind = application_file_kind(relative)
        files.append(
            {
                "path": relative,
                "origin": origin,
                "kind": kind,
                "sha256": sha256(path),
                "size": path.stat().st_size,
            }
        )
    return files


def validate_publish_baseline(rid: str, files: list[dict], baseline: dict) -> None:
    expected = sorted(baseline.get("rids", {}).get(rid, []), key=str.casefold)
    actual = sorted((item["path"] for item in files), key=str.casefold)
    if actual != expected:
        missing = sorted(set(expected) - set(actual), key=str.casefold)
        unexpected = sorted(set(actual) - set(expected), key=str.casefold)
        raise ComplianceError(
            f"Publish content baseline drift for {rid}; "
            f"missing={missing}, unexpected={unexpected}"
        )


def package_component(package: dict) -> dict:
    component = package["component"]
    nupkg = package["root"] / (
        f"{package['id'].lower()}.{package['version']}.nupkg"
    )
    if not nupkg.is_file():
        raise ComplianceError(f"NuGet archive is missing: {nupkg}")
    return {
        "type": "library",
        "bom-ref": f"pkg:nuget/{package['id']}@{package['version']}",
        "name": package["id"],
        "version": package["version"],
        "purl": f"pkg:nuget/{package['id']}@{package['version']}",
        "licenses": [
            {"license": {"id": license_id}}
            for license_id in component["licenses"]
        ],
        "hashes": [
            {"alg": "SHA-256", "content": sha256(nupkg)},
            {"alg": "SHA-512", "content": sha512(nupkg)},
        ],
        "externalReferences": [
            {"type": "vcs", "url": component["source"]}
        ],
        "properties": [
            {"name": "gosteditor:copyright", "value": component["copyright"]}
        ],
    }


def file_component(rid: str, file: dict) -> dict:
    return {
        "type": "file",
        "bom-ref": f"file:{rid}:{file['path']}",
        "name": file["path"],
        "hashes": [{"alg": "SHA-256", "content": file["sha256"]}],
        "properties": [
            {"name": "gosteditor:rid", "value": rid},
            {"name": "gosteditor:origin", "value": file["origin"]},
            {"name": "gosteditor:kind", "value": file["kind"]},
            {"name": "gosteditor:size", "value": str(file["size"])},
        ],
    }


def file_bom_ref(rid: str, file: dict) -> str:
    return f"file:{rid}:{file['path']}"


def direct_runtime_package_keys(
    target: dict,
    selected: dict[str, dict],
) -> set[str]:
    by_id = {value["id"].casefold(): key for key, value in selected.items()}
    direct: set[str] = set()
    for key, details in target.items():
        if key in selected:
            continue
        for dependency_id in details.get("dependencies", {}):
            dependency_key = by_id.get(dependency_id.casefold())
            if dependency_key is not None:
                direct.add(dependency_key)
    return direct


def dependency_graph(
    target: dict,
    selected: dict[str, dict],
    application_ref: str,
    rid: str,
    files: list[dict],
    direct_packages: set[str],
) -> list[dict]:
    by_id = {value["id"].casefold(): key for key, value in selected.items()}
    application_files = [
        file_bom_ref(rid, file)
        for file in files
        if file["origin"] in {"GostEditor", "release-compliance"}
    ]
    dependencies: list[dict] = [
        {
            "ref": application_ref,
            "dependsOn": sorted(
                application_files
                + [
                    f"pkg:nuget/{selected[key]['id']}@{selected[key]['version']}"
                    for key in direct_packages
                ]
            ),
        }
    ]
    for key, package in sorted(selected.items(), key=lambda item: item[0].casefold()):
        direct_dependencies: list[str] = []
        for dependency_id in target.get(key, {}).get("dependencies", {}):
            dependency_key = by_id.get(dependency_id.casefold())
            if dependency_key is None:
                continue
            dependency = selected[dependency_key]
            direct_dependencies.append(
                f"pkg:nuget/{dependency['id']}@{dependency['version']}"
            )
        direct_dependencies.extend(
            file_bom_ref(rid, file)
            for file in files
            if file["origin"] == key
        )
        dependencies.append(
            {
                "ref": f"pkg:nuget/{package['id']}@{package['version']}",
                "dependsOn": sorted(direct_dependencies),
            }
        )
    return dependencies


def generate(
    rid: str,
    publish_dir: Path,
    output_dir: Path,
    policy_path: Path,
    baseline_path: Path,
    nuget_packages: Path,
    repository_root: Path,
) -> None:
    if not publish_dir.is_dir():
        raise ComplianceError(f"Publish directory does not exist: {publish_dir}")
    policy = read_json(policy_path)
    baseline = read_json(baseline_path)
    deps_path = publish_dir / "GostEditor.UI.deps.json"
    deps = read_json(deps_path)
    runtime_packages = get_runtime_packages(deps)
    selected = validate_runtime_packages(
        rid,
        runtime_packages,
        policy,
        nuget_packages,
    )

    notice_text = generate_notice(selected, policy, repository_root)
    notice_path = publish_dir / "THIRD-PARTY-NOTICES.txt"
    notice_path.write_text(notice_text, encoding="utf-8", newline="\n")

    target = target_for_rid(deps, rid)
    assets = map_published_assets(target, rid)
    files = inventory_files(publish_dir, assets)
    validate_publish_baseline(rid, files, baseline)
    direct_packages = direct_runtime_package_keys(target, selected)

    package_inventory = [
        {
            "id": package["id"],
            "version": package["version"],
            "relationship": "direct" if key in direct_packages else "transitive",
            "licenses": package["component"]["licenses"],
            "source": package["component"]["source"],
        }
        for key, package in sorted(selected.items(), key=lambda item: item[0].casefold())
    ]
    inventory = {
        "schemaVersion": 1,
        "rid": rid,
        "packages": package_inventory,
        "files": files,
    }

    application = policy["application"]
    application_ref = f"pkg:generic/{application['name']}@{application['version']}"
    package_components = [
        package_component(package)
        for _, package in sorted(selected.items(), key=lambda item: item[0].casefold())
    ]
    file_components = [file_component(rid, file) for file in files]
    sbom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.6",
        "version": 1,
        "metadata": {
            "component": {
                "type": "application",
                "bom-ref": application_ref,
                "name": application["name"],
                "version": application["version"],
                "supplier": {"name": application["supplier"]},
            },
            "tools": {
                "components": [
                    {
                        "type": "application",
                        "name": "GostEditor release compliance generator",
                        "version": "1",
                    }
                ]
            },
        },
        "components": package_components + file_components,
        "dependencies": dependency_graph(
            target,
            selected,
            application_ref,
            rid,
            files,
            direct_packages,
        ),
    }

    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / "THIRD-PARTY-NOTICES.txt").write_text(
        notice_text,
        encoding="utf-8",
        newline="\n",
    )
    write_json(output_dir / "publish-inventory.json", inventory)
    write_json(output_dir / "sbom.cdx.json", sbom)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True)
    parser.add_argument("--publish-dir", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument(
        "--policy",
        type=Path,
        default=Path(__file__).with_name("compliance-policy.json"),
    )
    parser.add_argument(
        "--baseline",
        type=Path,
        default=Path(__file__).with_name("publish-baseline.json"),
    )
    parser.add_argument(
        "--nuget-packages",
        type=Path,
        default=Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages")),
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    repository_root = Path(__file__).resolve().parents[2]
    try:
        generate(
            args.rid,
            args.publish_dir.resolve(),
            args.output_dir.resolve(),
            args.policy.resolve(),
            args.baseline.resolve(),
            args.nuget_packages.resolve(),
            repository_root,
        )
    except ComplianceError as error:
        print(f"release compliance failed: {error}", file=sys.stderr)
        return 2
    print(f"release compliance passed for {args.rid}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
