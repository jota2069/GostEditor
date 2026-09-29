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
| Open | Waits; shutdown prevents late publication | Loaded document is published while ownership is held |
| Autosave | Skips the current attempt; a later timer tick may retry | Captured only after successful non-blocking ownership |
| DOCX export | Waits, observing its cancellation token | Deep revision snapshot captured after ownership |
| Recovery clear/reset | Waits | Runs under the same ownership boundary |

Only one of these operations can own the coordinator at a time. Waiting user
operations enter a FIFO queue, so overlapping Save/Save As/Open/export requests run
in request order. No snapshot is captured while an operation is merely waiting.
Consequently a queued save cannot later publish a snapshot that was already
stale when it entered the queue.

Autosave deliberately does not queue. A pending autosave must not delay a user
Save, Save As, Open or export, and skipping one timer tick does not lose live document
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

## Shutdown lifecycle

Application shutdown extends ordinary ownership with a suspended state. Once
suspended, new queued ownership is rejected, existing waiters are cancelled and
the active lease receives a lifecycle cancellation token. Shutdown waits until
that lease is released; it never treats cancellation request delivery as proof
that the writer has stopped.

Every coordinated service passes the lease token to its underlying writer. A
writer that is still before its cancellation boundary stops normally. Atomic
save may already have entered its late, non-cancellable commit phase; in that
case shutdown waits and the successful save is still published to the session.

The autosave timer is stopped before suspension. A tick already in progress is
tracked separately and awaited after coordinator drain, including its error
notification. Exceptions thrown by autosave failure subscribers are contained
and recorded as diagnostics rather than escaping an `async void` dispatcher
callback.

Cancelling window close resumes the coordinator with a fresh lifecycle token
and restarts autosave. The unsaved-changes prompt also runs after resume so a
final Save is possible. Immediately before the window is actually closed, the
coordinator is suspended and drained again. This makes provider disposal occur
only after coordinated persistence has reached a stable idle boundary.

## Explicit exclusions

This coordinator is process-local and does not solve:

- startup selection and diagnostics for corrupt recovery generations;
- startup recovery decisions;
- external file modification or multiple application processes;
- transactional undo/redo;
- filesystem durability beyond the atomic commit contract.
