# Collaboration-ready local edit contracts

## Boundary

`CanonicalEditEnvelope` is a versioned, transport-neutral command boundary over
the existing `DocumentEditingSession`, `EditTransaction` and
`OperationHistory`. It does not introduce a second editing stack: validated
payloads compile to the existing reversible operations and enter the same
history, invalidation and node-index paths as local editor actions.

The contract depends only on Core data types. It has no Avalonia, ViewModel,
layout, filesystem, network, WebRTC, CRDT or OT dependency. Polymorphic JSON
metadata makes payloads serialization-friendly for tests and future boundaries,
but Branch 21 does not define a network protocol or persisted operation log.

## Envelope and transaction

An envelope contains:

- envelope version;
- non-empty transaction identity;
- base and expected result canonical revisions;
- a bounded ordered list of versioned operations with unique operation IDs;
- stable node targets and deterministic data payloads.

One accepted envelope is one history item and one canonical revision,
independent of the number of primitive operations inside it. Operation IDs seed
deterministic IDs for new inline nodes. Transaction and operation objects never
contain UI controls, callbacks from the payload, runtime node references or
transport state.

## Canonical revision

`DocumentEditingSession.Revision` is the monotonic revision of the canonical
mutable graph:

- it starts at zero;
- a successful operation or transaction increments it once;
- successful undo and redo each create a new revision;
- rejection, failed apply, failed undo/redo and history clearing do not create a
  graph revision;
- an envelope is accepted only when `BaseRevision` equals the current revision
  and `ResultRevision == BaseRevision + 1`.

The UI `DocumentSessionState.ChangeVersion` and
`DocumentPersistenceSnapshot.Revision` remain the proven legacy persistence
savepoint contract. They are intentionally separate from canonical revision
until the persistence/runtime cutover branches. Branch 21 does not connect or
replace them.

## Replay and failure behavior

The same canonical initial graph (including stable IDs) plus the same ordered
envelopes produces the same semantic fingerprint. The fingerprint uses explicit
tree order, sorted extension keys and stable binary encodings; it never depends
on object identity or dictionary enumeration.

Expected rejection returns a data-only `CanonicalApplyResult` with a bounded
failure code. Stale revision, invalid envelope, invalid target and semantic
no-op do not mutate graph, index or history. Multi-operation apply uses the
existing transaction compensation in reverse order. Index validation is part of
the atomic boundary. Listener exceptions remain observed by tracing and do not
roll back already committed state.

Undo/redo restore captured post-operation inline snapshots, so generated node
identities do not change on redo. Full graph/index validation remains reserved
for structural boundaries; ordinary text/style edits update only affected
paragraph indexes.
