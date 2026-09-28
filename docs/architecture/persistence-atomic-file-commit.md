# Atomic file commit contract

Path-based `.gost` saves use transaction artifacts in the destination
directory. The replacement package is fully written, flushed and closed before
the destination name is changed. Artifact names contain a transaction-specific
identifier:

- `<destination>.<transaction>.tmp` contains the fully written replacement;
- `<destination>.<transaction>.rollback` is a transient copy of the previous
  destination created by the filesystem replacement operation.

Both artifacts are implementation details. The rollback file is not a
persistent user-facing `.bak` or document-history feature.

## Normal commit

For an existing destination, the replacement copies the destination's Unix mode
on Unix and calls `File.Replace` with the transaction rollback path. A successful
replace publishes the new package at the destination and leaves the old package
at the rollback path. Rollback cleanup is best-effort; cleanup failure does not
turn the successful save into a reported failure, and the old package may remain
as a `.rollback` artifact.

For a new destination, `File.Move` publishes the closed replacement. The
replacement and destination are in the same directory so the operation does not
intentionally cross filesystems.

## Failed commit and Windows reconciliation

An exception from `File.Replace` does not prove that the filesystem was left
unchanged. In particular, Windows `ReplaceFileW` documents partial failure
states including `ERROR_UNABLE_TO_MOVE_REPLACEMENT` and
`ERROR_UNABLE_TO_MOVE_REPLACEMENT_2`.

After any exception from the commit phase, the committer inspects the
destination, replacement and rollback paths. It follows these rules:

1. If the destination is missing and the rollback path contains a file, it
   attempts to move the rollback file back to the destination. Windows performs
   this move without overwriting another file. On Unix, the non-overwriting
   `File.Move` contract still has the inspection-to-rename TOCTOU limitation
   described below when another process changes the destination concurrently.
2. If the destination already exists, its contents are not changed during
   reconciliation.
3. Replacement and rollback artifacts are not deleted after a failed commit.
   They may be the only remaining valid old or new package.
4. If inspection or rollback restoration fails, the primary commit exception is
   preserved and the secondary failure is attached to its diagnostic data.
5. In an ambiguous state, preserving every potential copy has priority over
   artifact cleanup. A `.tmp` or `.rollback` file can therefore remain for
   manual or future automated recovery.

Typical outcomes are:

| Commit outcome | Destination | `.tmp` | `.rollback` | Reconciliation |
| --- | --- | --- | --- | --- |
| Failure before filesystem mutation | old package | new package | absent | preserve both packages |
| `ERROR_UNABLE_TO_MOVE_REPLACEMENT` with rollback path | old package normally remains | new package normally remains | absent or filesystem-defined | preserve all present copies |
| `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2` | may be missing | new package normally remains | old package normally remains | restore rollback to destination when safe; preserve replacement |
| Replacement happened before an error was reported | new package | absent | old package | preserve destination and rollback |
| Reconciliation itself fails | filesystem-defined | preserved when present | preserved when present | report original commit error with secondary diagnostics |

This state-based policy does not rely solely on a Win32 error number. The same
conservative reconciliation runs after any commit exception because filters,
drivers and filesystems may report other failures after changing state.

## Failure and exception guarantees

- Serialization, write, flush and close failures do not open or truncate the
  destination.
- Before commit starts, failure cleanup is best-effort and removes the temporary
  replacement when possible.
- Cleanup, inspection, close-retry and reconciliation failures never replace the
  primary write, flush, close or commit exception. They are retained as
  secondary diagnostics where a primary exception exists.
- Temporary contents are flushed through `FileStream.Flush(true)` before close.
- Direct stream saves do not provide this transaction boundary because the
  caller owns the destination stream.

## Cancellation commit point

Cancellation is observed while writing and flushing and is checked once more
after the replacement stream has been closed. That final check is the commit
point:

- cancellation observed before it prevents publication and triggers normal
  pre-commit cleanup;
- after the final cancellation-token check has completed, cancellation is
  considered too late, including the short interval before the synchronous
  filesystem commit call begins;
- a successfully completed commit is reported as success even if the token is
  cancelled during the synchronous commit operation.

The commit operation itself is intentionally not cancellable because an
interrupted or ambiguously reported filesystem replacement must be reconciled,
not reported as a clean cancellation.

## Durability and power-loss boundary

The replacement file contents are durably flushed before commit and the
replacement occurs in the destination directory. .NET does not expose a
portable parent-directory `fsync`, so this contract does not guarantee that a
directory-entry change survives every sudden-power-loss scenario. It also
cannot strengthen the atomicity or durability guarantees of the destination
filesystem, network share, FUSE provider or storage hardware.

A process crash before commit can leave a `.tmp` artifact. A crash during or
after replacement can leave the destination and/or rollback artifact according
to the operating system and filesystem. Startup discovery and user-facing
recovery of such artifacts belong to the later recovery design; this primitive
only avoids deliberately deleting potential recovery copies after a reported
commit failure.

## Deliberate concurrency boundaries

This layer does not coordinate simultaneous saves and does not detect external
file modification. `File.Exists`, state inspection and the following move or
replace operations contain unavoidable TOCTOU windows.

Ordering manual save, autosave and export belongs to the later I/O coordinator.
Revision snapshots and savepoints are separate persistence work. External file
conflict detection and recovery generations are also outside this contract.
