# Canonical text editor cutover

## Active ownership

`DocumentEditor` owns one active `DocumentEditingSession`. Text, caret,
selection, supported formatting and their inverse operations mutate its
`DocumentRoot`; stable node IDs are the addressing boundary. The UI-facing
`CommandManager` is retained as a compatibility action stack, but text command
records delegate execute/undo/redo to the captured canonical
`OperationHistory`. It does not own a second text mutation.

## Temporary legacy projection

Layout, `.gost` persistence, recovery and DOCX export still consume
`GostDocument` until their roadmap cutovers. `LegacyTextProjection` updates
that object in one direction after a committed canonical revision. It reuses
unchanged legacy paragraph instances, projects canonical order and formatting,
and never writes changes back through events.

Any direct mutation of the legacy text view is rejected by the next editor
action rather than imported back into canonical state. The active session also
validates legacy projection compatibility before an operation is committed:
unsupported blocks, links and font families fail atomically without a history
record or revision. This keeps the frozen `.gost` boundary loss-aware.

The projection deliberately keeps the existing `GostDocument` object identity.
Phase 1 savepoints therefore continue to capture a revision-bound immutable
snapshot, and an edit occurring after snapshot capture cannot replace the
active session or publish stale state.

Images and other structural commands remain at the explicit legacy boundary in
Branch 22. A completed structural command reconciles its legacy range into the
same active `DocumentRoot`; it does not replace the editing session. The
linear-time reconciliation preserves unchanged block instances and uses the
command's canonical identity snapshot when undo/redo restores a previously
removed block. Historical text operations therefore keep valid stable IDs
across ordered mixed undo/redo while the Branch 24 structural cutover remains
pending. Text commands never rebuild the graph per keystroke.

## Revision roles

Canonical revision advances for each successful canonical operation and for
successful undo/redo. The persistence `DocumentSessionState.ChangeVersion`
continues to advance from the editor's mutation event. It is monotonic, so undo
back to previously saved content remains dirty. Rejected/no-op canonical edits
do not publish a mutation event and do not advance either contract.

## Deferred boundaries

- canonical figures/resources and structural command ownership: Branch 24;
- canonical persistence snapshots: Branch 25;
- canonical immutable layout input: Branch 26;
- removal of the active legacy runtime facade: Branch 27.

No network, collaboration transport, CRDT/OT or AI dependency is introduced by
this cutover.
