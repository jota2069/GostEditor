# Persistence I/O coordinator

## Purpose

`PersistenceIoCoordinator` is the single application-scoped ownership boundary
for user-visible document I/O. It serializes operations that capture or publish
document state without blocking ordinary editing.

The coordinator complements, but does not replace, the atomic file commit and
revision snapshot layers:

1. an operation obtains coordinator ownership;
2. it captures the document and revision while it owns the coordinator;
3. it performs the I/O;
4. it publishes session state only after successful completion;
5. disposing the ownership lease allows the next operation to run.

## Operation rules

| Operation | When ownership is unavailable | Snapshot rule |
| --- | --- | --- |
| Manual Save | Waits, observing its cancellation token | Captured after ownership |
| Save As | Waits, observing its cancellation token | Captured after ownership |
| Autosave | Skips the current attempt; a later timer tick may retry | Captured only after successful non-blocking ownership |
| DOCX export | Waits, observing its cancellation token | Deep revision snapshot captured after ownership |
| Recovery clear/reset | Waits | Runs under the same ownership boundary |

Only one of these operations can own the coordinator at a time. Waiting user
operations enter a FIFO queue, so overlapping Save/Save As/export requests run
in request order. No snapshot is captured while an operation is merely waiting.
Consequently a queued save cannot later publish a snapshot that was already
stale when it entered the queue.

Autosave deliberately does not queue. A pending autosave must not delay a user
Save, Save As or export, and skipping one timer tick does not lose live document
state.

## Savepoint and recovery cleanup

The revision/savepoint contract remains unchanged: a successful save records
the revision of the committed snapshot. If edits occurred after capture, the
document remains Dirty.

Cleanup requested after a successful manual save carries that save's revision.
After acquiring ownership, autosave deletes recovery data only if the document
is still Clean at exactly that revision. If a newer edit or autosave exists, the
cleanup is skipped so that a newer recovery copy is not deleted by an older
manual-save continuation.

Explicit lifecycle cleanup and reset do not use this revision guard because
their caller intentionally requests removal independent of the savepoint.

## Cancellation and failures

- Cancellation while waiting prevents ownership, snapshot capture and output.
- After ownership, the operation passes the token to its underlying writer.
- Atomic `.gost` commit retains its existing late-cancellation commit point.
- DOCX generation is synchronous once its worker has started; cancellation can
  prevent queued work from starting but is late after generation begins.
- Every ownership lease is released by `using`, including exceptions and
  cancellation, so a failed operation cannot permanently block later work.
- Failure or cancellation does not advance `SavedRevision` or change the
  current path because `DocumentSaveService` publishes them only after commit.

## Explicit exclusions

This coordinator is process-local and does not solve:

- shutdown while I/O is active;
- recovery generations or atomic package/metadata generations;
- startup recovery decisions;
- external file modification or multiple application processes;
- transactional undo/redo;
- filesystem durability beyond the atomic commit contract.
