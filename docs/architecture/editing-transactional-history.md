# Transactional editing history

Both editing stacks now use the same failure contract:

- a command enters undo history only after its first execution succeeds;
- Undo and Redo keep the item on the original stack until the operation
  succeeds;
- failed attempts do not clear the opposite stack, increment history version or
  publish a history-changed notification;
- composite operations compensate children whose call completed successfully
  in reverse order before propagating the original exception.

Every individual `IEditOperation` must either complete its `Apply`/`Revert`
call or leave the document unchanged. `EditTransaction` does not call the
opposite method for the child that threw, because that child may have rejected
the operation before mutation. If compensation of an earlier completed child
fails, the original exception remains observable and all secondary failures
are attached to `Exception.Data` under
`GostEditor.EditTransaction.RollbackFailures`.

Selection-aware operations restore both document state and the pre-operation
selection when apply fails, and restore the post-operation state when undo
fails. Compensation errors are diagnostic secondary failures and never replace
the primary error.

History and document-change notifications observe an already committed state
transition. Subscriber exceptions are traced and isolated, so they cannot
prevent a completed command from entering the correct undo/redo stack.

The legacy `DocumentMutationCommand` captures its affected paragraph range,
image catalog, counters, caret and selection before mutation. A thrown action,
an exception while determining commit state, or a rejected commit predicate
restores that captured state and creates no history entry. Undo/redo restoration
also attempts the inverse snapshot if restoration fails. `SnapshotCommand`
similarly restores its old snapshot when its initial action throws.

This branch deliberately does not add typing coalescing, history-size limits or
canonical-model migration. Those are separate performance and architecture
changes. The transaction contract assumes individual production primitives are
reversible; if both a mutation and its compensation fail, the error is surfaced
with diagnostics rather than falsely reporting success.
