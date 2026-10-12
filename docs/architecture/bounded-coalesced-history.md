# Bounded coalesced history

Branch 23 adds user-visible history grouping without changing the canonical
revision contract. Every accepted edit still increments `OperationHistory.Version`
and publishes its own change event. A grouped undo or redo is a new accepted
history transaction and increments the revision once.

## Coalescing contract

Only adjacent typing, Backspace, and Delete Forward deltas can coalesce. A
candidate must have the same paragraph, direction and style, and its range must
be exactly adjacent to the preceding delta. The elapsed time must also fit the
configured window. Time is therefore an additional boundary, not the source of
correctness.

Caret or selection movement, formatting, paste, paragraph/structural edits,
explicit transactions, undo/redo, session replacement, compatibility mutations,
and an explicit `BreakCoalescing` call end the current group. Manual save invokes
that boundary before capturing its savepoint.

The Avalonia view supplies a `TextInputMethodClient`. Any preedit notification
marks the next committed payload as a composition commit; it uses
`InsertCompositionText`, which brackets the commit with explicit boundaries.
Batched and soft-keyboard commits also take that path. A direct single text
element correlated with a normal text-producing key remains eligible for typing
coalescing.

The legacy `CommandManager` retains one canonical proxy per canonical undo group.
This keeps Ctrl+Z/Ctrl+Y aligned with `OperationHistory`, including after budget
eviction.

## Retained data and budget

The hot path uses reversible inline deltas. A delta retains the inserted or
removed UTF-16 text, the stable paragraph/inline IDs, offsets, direction and
selection states. It does not retain a document or paragraph snapshot for every
character. General structural and formatting operations remain independent
history entries.

`OperationHistoryPolicy` configures the coalescing window, maximum undo groups,
and retained-payload budget. Snapshot, transaction, block and text operations
report their recursive retained payload, including strings and inline trees.
After a successful edit, the history evicts the
oldest undo groups until both limits are satisfied. Eviction never reverts the
document, changes its revision, or creates redo entries. An individual record
larger than the whole budget is safely left non-undoable.

## Complexity checks

Deterministic tests cover 1k, 10k and 50k character corpora by asserting group
count and retained payload rather than wall-clock timing. They also cover
directional deletion groups, boundaries, UI/canonical stack alignment,
undo/redo revisions, and oldest-first budget eviction.
