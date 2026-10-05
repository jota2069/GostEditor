from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


MODULE_PATH = Path(__file__).with_name("generate_compliance.py")
SPEC = importlib.util.spec_from_file_location("generate_compliance", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
COMPLIANCE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPLIANCE)


class ReleaseComplianceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.repository = self.root / "repository"
        self.nuget = self.root / "packages"
        self.repository.mkdir()
        (self.repository / "notices").mkdir()
        (self.repository / "notices" / "MIT.txt").write_text(
            "MIT fixture license\n",
            encoding="utf-8",
        )
        self._create_package(
            "Allowed.Core",
            "1.0.0",
            license_type="expression",
            license_value="MIT",
            extra_files={"NOTICE.txt": b"Allowed package notice\n"},
        )
        self._create_package(
            "Native.Asset",
            "2.0.0",
            license_type="file",
            license_value="LICENSE",
            extra_files={"LICENSE": b"BSD fixture license\n"},
        )
        self.policy = self._create_policy()
        self.policy_path = self.repository / "policy.json"
        self.baseline_path = self.repository / "baseline.json"
        self._write_json(self.policy_path, self.policy)
        self.baseline = {
            "schemaVersion": 1,
            "rids": {
                "linux-x64": [
                    "Allowed.Core.dll",
                    "GostEditor.UI.deps.json",
                    "GostEditor.UI.dll",
                    "THIRD-PARTY-NOTICES.txt",
                ],
                "win-x64": [
                    "Allowed.Core.dll",
                    "GostEditor.UI.deps.json",
                    "GostEditor.UI.dll",
                    "Native.Asset.dll",
                    "THIRD-PARTY-NOTICES.txt",
                ],
            },
        }
        self._write_json(self.baseline_path, self.baseline)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_generate_same_inputs_produces_identical_inventory_sbom_and_notices(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        first = self.root / "first"
        second = self.root / "second"

        self._generate("linux-x64", publish, first)
        self._generate("linux-x64", publish, second)

        for file_name in (
            "publish-inventory.json",
            "sbom.cdx.json",
            "THIRD-PARTY-NOTICES.txt",
        ):
            self.assertEqual(
                (first / file_name).read_bytes(),
                (second / file_name).read_bytes(),
                file_name,
            )
        notice = (first / "THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8")
        self.assertIn("Allowed.Core/1.0.0", notice)
        self.assertIn("MIT fixture license", notice)
        self.assertIn("Allowed package notice", notice)

    def test_generate_records_sorted_files_origins_and_content_hashes(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        first = self.root / "first"
        second = self.root / "second"

        self._generate("linux-x64", publish, first)
        first_inventory = self._read_json(first / "publish-inventory.json")
        first_files = first_inventory["files"]
        self.assertEqual(
            sorted(item["path"].casefold() for item in first_files),
            [item["path"].casefold() for item in first_files],
        )
        library = next(item for item in first_files if item["path"] == "Allowed.Core.dll")
        self.assertEqual("Allowed.Core/1.0.0", library["origin"])
        self.assertEqual("managed", library["kind"])
        package = first_inventory["packages"][0]
        self.assertEqual("direct", package["relationship"])

        (publish / "Allowed.Core.dll").write_bytes(b"changed library")
        self._generate("linux-x64", publish, second)
        second_inventory = self._read_json(second / "publish-inventory.json")
        changed = next(
            item
            for item in second_inventory["files"]
            if item["path"] == "Allowed.Core.dll"
        )
        self.assertNotEqual(library["sha256"], changed["sha256"])
        sbom = self._read_json(second / "sbom.cdx.json")
        sbom_file = next(
            item
            for item in sbom["components"]
            if item.get("bom-ref") == "file:linux-x64:Allowed.Core.dll"
        )
        self.assertEqual(changed["sha256"], sbom_file["hashes"][0]["content"])

    def test_generate_rejects_unapproved_runtime_package(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        deps = self._read_json(publish / "GostEditor.UI.deps.json")
        self._add_package_to_deps(deps, "Unknown.Library", "9.9.9", "Unknown.dll")
        self._write_json(publish / "GostEditor.UI.deps.json", deps)
        (publish / "Unknown.dll").write_bytes(b"unknown")
        policy = copy.deepcopy(self.policy)
        policy["rids"]["linux-x64"].append("Unknown.Library/9.9.9")
        self._write_json(self.policy_path, policy)

        with self.assertRaisesRegex(
            COMPLIANCE.ComplianceError,
            "Runtime package is not approved",
        ):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_rejects_license_metadata_drift(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        self._write_nuspec(
            "Allowed.Core",
            "1.0.0",
            license_type="expression",
            license_value="Apache-2.0",
        )

        with self.assertRaisesRegex(COMPLIANCE.ComplianceError, "License drift"):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_rejects_unapproved_component_license(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        policy = copy.deepcopy(self.policy)
        policy["components"]["Allowed.Core"]["licenses"] = ["GPL-3.0-only"]
        self._write_json(self.policy_path, policy)

        with self.assertRaisesRegex(
            COMPLIANCE.ComplianceError,
            "unapproved license policy",
        ):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_rejects_notice_content_drift(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        notice = self.nuget / "allowed.core" / "1.0.0" / "NOTICE.txt"
        notice.write_text("changed notice\n", encoding="utf-8")

        with self.assertRaisesRegex(COMPLIANCE.ComplianceError, "Unexpected content"):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_rejects_xceed_even_if_policy_lists_it(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        self._create_package(
            "Xceed.DocX",
            "5.0.0",
            license_type="expression",
            license_value="MIT",
        )
        deps = self._read_json(publish / "GostEditor.UI.deps.json")
        self._add_package_to_deps(deps, "Xceed.DocX", "5.0.0", "Xceed.DocX.dll")
        self._write_json(publish / "GostEditor.UI.deps.json", deps)
        policy = copy.deepcopy(self.policy)
        policy["rids"]["linux-x64"].append("Xceed.DocX/5.0.0")
        policy["components"]["Xceed.DocX"] = {
            "version": "5.0.0",
            "metadataLicense": "MIT",
            "licenses": ["MIT"],
            "source": "https://invalid.example/Xceed",
            "copyright": "fixture",
            "notices": [],
        }
        self._write_json(self.policy_path, policy)

        with self.assertRaisesRegex(COMPLIANCE.ComplianceError, "Forbidden"):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_rejects_publish_content_drift(self) -> None:
        publish = self._create_publish("linux-x64", include_native=False)
        (publish / "unexpected.bin").write_bytes(b"unexpected")

        with self.assertRaisesRegex(
            COMPLIANCE.ComplianceError,
            "Published file has no dependency owner|Publish content baseline drift",
        ):
            self._generate("linux-x64", publish, self.root / "output")

    def test_generate_uses_distinct_rid_package_and_native_inventories(self) -> None:
        linux_publish = self._create_publish("linux-x64", include_native=False)
        windows_publish = self._create_publish("win-x64", include_native=True)
        linux_output = self.root / "linux-output"
        windows_output = self.root / "windows-output"

        self._generate("linux-x64", linux_publish, linux_output)
        self._generate("win-x64", windows_publish, windows_output)

        linux = self._read_json(linux_output / "publish-inventory.json")
        windows = self._read_json(windows_output / "publish-inventory.json")
        self.assertEqual(["Allowed.Core"], [item["id"] for item in linux["packages"]])
        self.assertEqual(
            ["Allowed.Core", "Native.Asset"],
            [item["id"] for item in windows["packages"]],
        )
        self.assertEqual(
            ["direct", "transitive"],
            [item["relationship"] for item in windows["packages"]],
        )
        native = next(
            item for item in windows["files"] if item["path"] == "Native.Asset.dll"
        )
        self.assertEqual("Native.Asset/2.0.0", native["origin"])
        self.assertEqual("native", native["kind"])
        windows_notice = (windows_output / "THIRD-PARTY-NOTICES.txt").read_text(
            encoding="utf-8"
        )
        self.assertIn("BSD fixture license", windows_notice)
        self.assertNotIn(
            "BSD fixture license",
            (linux_output / "THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8"),
        )

    def _create_policy(self) -> dict:
        mit_path = self.repository / "notices" / "MIT.txt"
        allowed_notice = (
            self.nuget / "allowed.core" / "1.0.0" / "NOTICE.txt"
        )
        native_license = (
            self.nuget / "native.asset" / "2.0.0" / "LICENSE"
        )
        return {
            "schemaVersion": 1,
            "application": {
                "name": "GostEditor",
                "version": "1.0.0",
                "supplier": "test",
            },
            "allowedLicenses": ["MIT", "BSD-3-Clause"],
            "forbiddenPackagePrefixes": ["docx", "xceed"],
            "mitLicenseNotice": {
                "kind": "repository",
                "path": "notices/MIT.txt",
                "sha256": COMPLIANCE.sha256(mit_path),
            },
            "components": {
                "Allowed.Core": {
                    "version": "1.0.0",
                    "metadataLicense": "MIT",
                    "licenses": ["MIT"],
                    "source": "https://example.test/allowed",
                    "copyright": "Allowed copyright",
                    "notices": ["allowed-notice"],
                },
                "Native.Asset": {
                    "version": "2.0.0",
                    "metadataLicenseFile": "LICENSE",
                    "metadataLicenseFileSha256": COMPLIANCE.sha256(native_license),
                    "licenses": ["BSD-3-Clause"],
                    "source": "https://example.test/native",
                    "copyright": "Native copyright",
                    "notices": ["native-license"],
                },
            },
            "notices": {
                "allowed-notice": {
                    "kind": "package",
                    "package": "Allowed.Core",
                    "path": "NOTICE.txt",
                    "sha256": COMPLIANCE.sha256(allowed_notice),
                },
                "native-license": {
                    "kind": "package",
                    "package": "Native.Asset",
                    "path": "LICENSE",
                    "sha256": COMPLIANCE.sha256(native_license),
                },
            },
            "rids": {
                "linux-x64": ["Allowed.Core/1.0.0"],
                "win-x64": ["Allowed.Core/1.0.0", "Native.Asset/2.0.0"],
            },
        }

    def _create_package(
        self,
        package_id: str,
        version: str,
        *,
        license_type: str,
        license_value: str,
        extra_files: dict[str, bytes] | None = None,
    ) -> None:
        root = self.nuget / package_id.lower() / version
        root.mkdir(parents=True)
        self._write_nuspec(
            package_id,
            version,
            license_type=license_type,
            license_value=license_value,
        )
        (root / f"{package_id.lower()}.{version}.nupkg").write_bytes(
            f"{package_id}/{version}".encode("utf-8")
        )
        for relative, content in (extra_files or {}).items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)

    def _write_nuspec(
        self,
        package_id: str,
        version: str,
        *,
        license_type: str,
        license_value: str,
    ) -> None:
        root = self.nuget / package_id.lower() / version
        root.mkdir(parents=True, exist_ok=True)
        (root / f"{package_id.lower()}.nuspec").write_text(
            "\n".join(
                [
                    '<?xml version="1.0" encoding="utf-8"?>',
                    "<package>",
                    "  <metadata>",
                    f"    <id>{package_id}</id>",
                    f"    <version>{version}</version>",
                    f'    <license type="{license_type}">{license_value}</license>',
                    "  </metadata>",
                    "</package>",
                ]
            )
            + "\n",
            encoding="utf-8",
        )

    def _create_publish(self, rid: str, *, include_native: bool) -> Path:
        publish = self.root / f"publish-{rid}"
        publish.mkdir()
        (publish / "Allowed.Core.dll").write_bytes(b"allowed library")
        (publish / "GostEditor.UI.dll").write_bytes(b"application")
        packages = [
            ("Allowed.Core", "1.0.0", "Allowed.Core.dll"),
        ]
        if include_native:
            (publish / "Native.Asset.dll").write_bytes(b"native")
            packages.append(("Native.Asset", "2.0.0", "Native.Asset.dll"))
        libraries = {
            f"{package_id}/{version}": {"type": "package"}
            for package_id, version, _ in packages
        }
        libraries["GostEditor.UI/1.0.0"] = {"type": "project"}
        target = {
            f"{package_id}/{version}": {
                ("native" if package_id == "Native.Asset" else "runtime"): {
                    f"lib/net10.0/{file_name}": {}
                }
            }
            for package_id, version, file_name in packages
        }
        target["GostEditor.UI/1.0.0"] = {
            "dependencies": {"Allowed.Core": "1.0.0"},
            "runtime": {"GostEditor.UI.dll": {}}
        }
        if include_native:
            target["Allowed.Core/1.0.0"]["dependencies"] = {
                "Native.Asset": "2.0.0"
            }
        deps = {
            "libraries": libraries,
            "targets": {f".NETCoreApp,Version=v10.0/{rid}": target},
        }
        self._write_json(publish / "GostEditor.UI.deps.json", deps)
        return publish

    @staticmethod
    def _add_package_to_deps(
        deps: dict,
        package_id: str,
        version: str,
        file_name: str,
    ) -> None:
        key = f"{package_id}/{version}"
        deps["libraries"][key] = {"type": "package"}
        target = next(iter(deps["targets"].values()))
        target[key] = {"runtime": {f"lib/net10.0/{file_name}": {}}}

    def _generate(self, rid: str, publish: Path, output: Path) -> None:
        COMPLIANCE.generate(
            rid,
            publish,
            output,
            self.policy_path,
            self.baseline_path,
            self.nuget,
            self.repository,
        )

    @staticmethod
    def _write_json(path: Path, value: dict) -> None:
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    @staticmethod
    def _read_json(path: Path) -> dict:
        return json.loads(path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
