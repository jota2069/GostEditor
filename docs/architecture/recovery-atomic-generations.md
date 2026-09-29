# Atomic recovery generations

## Problem

The legacy recovery layout published `autosave.gost` and `session.json` with
two independent renames. A process or filesystem failure between those renames
could pair a new document with old metadata, or the reverse.

## Generation layout

Each autosave now creates an immutable directory in the recovery filesystem:

```text
Recovery/
  current.json
  generation-<guid>/
    autosave.gost
    session.json
```

The package and metadata are first written into a same-filesystem staging
directory named `.generation-<guid>.tmp`. The package uses the normal atomic
`.gost` writer. Metadata is flushed to disk, then the complete staging directory
is renamed to its immutable generation name.

The generation is not current yet. `current.json` is then published through the
same `AtomicFileCommitter` used by normal document saves. This one atomic pointer
commit is the visibility boundary for the package/metadata pair.

If any step before pointer commit fails, the previous pointer is unchanged. A
complete but unreferenced generation may remain as a recovery artifact and is
never mistaken for the current generation. Cleanup after a successful publish
keeps the current and immediately previous committed generations. If the old
pointer cannot be read, generation cleanup is conservative and preserves every
potentially useful generation.

Cleanup is strictly best-effort. A cleanup or directory-enumeration failure is
recorded as an internal diagnostic and cannot replace the primary write or
pointer-publication exception. Once the pointer has been committed, cleanup
failure also cannot turn the successful recovery save into a reported failure;
extra generations or staging artifacts are retained instead.

## Reading and compatibility

Readers resolve `current.json` once and load both files from that generation.
The metadata `SessionId` must equal the pointer generation identifier. Missing,
corrupt or mismatched members are reported rather than silently combined.

If `current.json` is absent, the legacy root-level `autosave.gost` and
`session.json` pair remains readable. Generation publishing does not delete the
legacy pair: it remains an additional fallback artifact until the user or
lifecycle explicitly deletes recovery data.

## Durability boundary

The package, metadata file and pointer file are individually durably flushed.
The staging and generation directories are on the same filesystem. .NET does
not expose a portable directory-fsync API, so survival of the directory rename
across sudden power loss still depends on filesystem and operating-system
semantics. The previous committed generation is retained to reduce that risk.

Startup selection, diagnostics and user decisions for corrupt/orphaned
generations belong to the separate startup recovery state-machine branch.
