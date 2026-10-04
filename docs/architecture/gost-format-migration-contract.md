# .gost format and migration contract

The current persisted format is **v2**. A package is a standard single-disk,
non-ZIP64 ZIP archive whose `document.json` entry is UTF-8 JSON. For historical
reader compatibility, comments are accepted and ordinary DTO property names
are matched case-insensitively. The version discriminator is the exception:
the writer and reader require an integer `FormatVersion` with this exact casing.

Supported inputs:

| Source | Identification | Migration path |
|---|---|---|
| v0 | `FormatVersion` is absent | v0 → v1 → v2 |
| v1 | `FormatVersion` is integer `1` | v1 → v2 |
| v2 | `FormatVersion` is integer `2` | no migration |

An explicit integer `0` is accepted as v0 for compatibility. Negative,
non-integer and out-of-range versions are invalid data. A version above v2 is
rejected as unsupported rather than guessed. A differently-cased
`formatVersion` is rejected instead of being silently mistaken for v0.

Migrations are sequential. The registry accepts only N → N+1 steps, requires
every intermediate step and verifies that each migration advances the context
to its declared target version. Migration happens on an isolated DTO graph;
the live document is published only after the complete chain and
materialization succeed.

## Diagnostics

`ArchiveService.LoadWithDiagnosticsAsync` returns:

- the materialized document;
- detected source and current versions;
- whether migration occurred;
- immutable information/warning diagnostics.

The compatibility default on `IArchiveService` cannot infer format metadata
from an implementation that exposes only the legacy `LoadAsync` method. It
therefore reports `SourceVersion` and `WasMigrated` as unknown (`null`) instead
of inventing a current-format source. `ArchiveService` always returns detected
non-null values.

The normal `IArchiveService.LoadAsync` API remains compatible and returns only
the document. The desktop Open workflow uses the diagnostic API exposed by
`IArchiveService` and shows migration/repair notes in its status message.

Information diagnostics describe the v0/v1 migration steps. Warning
diagnostics describe observable repair or ignored/lost legacy input, including
missing/empty legacy media, replaced invalid image IDs, normalized image sizes,
unreferenced v2 media and ignored legacy-only v2 fields.

Loading never rewrites the source file. A migrated document is written as v2
only after an explicit successful Save/Save As through the normal snapshot and
atomic-commit pipeline.

## Compatibility boundary

Files produced by every GostEditor writer version represented by v0, v1 and v2
remain supported. The supported JSON profile is UTF-8 JSON with optional
comments and without single-quoted strings or trailing commas. UTF-16 manifests
are not part of the `.gost` contract. Unknown fields may be ignored within a
known version, but they cannot override exact version detection or v2 media
ownership rules.
