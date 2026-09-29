# Recovery startup state machine

Startup no longer assumes that `current.json` and its target are the only
possible recovery source. Before autosave is started, the recovery directory is
inspected once and classified as one of three states:

- `None`: no published, orphaned or legacy recovery package exists;
- `Recoverable`: at least one package and its metadata form a readable,
  consistent candidate;
- `Corrupted`: recovery artifacts exist, but none can be loaded safely.

For generation candidates, both `autosave.gost` and `session.json` are required.
The metadata format version and `SessionId` must match the directory generation
identifier. A corrupt pointer, incomplete generation or corrupt package is
recorded as an issue and cannot invalidate another independently valid copy.
Legacy root-level recovery remains supported.

All immutable generation directories are inspected, including complete orphans
left by an interrupted pointer publication. The valid candidate with the newest
`SavedAtUtc` is offered to the user; the current generation wins an exact-time
tie. This recovers a newer complete orphan without silently discarding it, while
still falling back to an older retained generation when the current one is
damaged.

Autosave starts only after one of these terminal startup outcomes:

- there is no recovery;
- the user restored a selected candidate;
- the user explicitly deleted all recovery artifacts.

If every candidate is corrupt, the application offers deletion or exit and does
not overwrite the artifacts in the background. A failed deletion returns to the
same decision instead of starting a new session. Closing the prompt exits the
application.

Explicit startup deletion is strict rather than best-effort. Cleanup failures
are propagated to the decision loop, and autosave remains stopped. The current
generation is deleted last: failures while deleting older artifacts leave the
published current copy intact; if deleting the current directory itself fails,
the pointer has already been removed and the remaining directory is still
discoverable as an orphan on the next inspection.

The inspection result contains the selected document and metadata together, so
the UI does not resolve `current.json` independently for each file. External
process conflicts and hostile-file resource limits remain separate later work.
