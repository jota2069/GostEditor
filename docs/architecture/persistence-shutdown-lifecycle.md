# Persistence shutdown lifecycle

## Contract

Closing the main window is asynchronous even when the current document is
Clean. The close request is cancelled first, persistence reaches a stable idle
boundary, and only then does the window issue a confirmed close.

The boundary is established in this order:

1. stop scheduling new autosave ticks;
2. suspend the application-scoped `PersistenceIoCoordinator`;
3. cancel the current lifecycle token and all queued ownership requests;
4. wait until the active ownership lease is released;
5. wait until every already scheduled autosave callback has finished handling
   its success, cancellation or failure.

Suspension rejects Save, Save As, Open, export, autosave and recovery maintenance.
`Resume` is legal only after drain has completed. It creates a fresh lifecycle
token, so a cancelled close cannot leak cancellation into later operations.

Ordinary Open cannot cancel the legacy archive reader once loading has begun,
so shutdown waits for that read to return. The loaded document is published only
after a final lifecycle-token check and while ownership is still held. Startup
recovery loading remains part of the later startup-recovery state-machine work.

## Close prompt and final Save

After the initial drain the coordinator is resumed before the unsaved-changes
prompt. Autosave remains stopped, but a user-selected final Save can acquire
normal ownership. If the user cancels closing, autosave is restarted. If the
user proceeds, persistence is suspended and drained a second time immediately
before the confirmed `Close()` call.

An atomic `.gost` writer may pass its last cancellable point before shutdown.
Such a save is allowed to finish, its successful revision/path result is
published, and shutdown awaits that completion. Cancellation is not reported
after a successful commit. If a newer edit exists, Branch 4 semantics still
leave `SavedRevision` at the committed snapshot and keep the document Dirty.

## Async error observation

The window close workflow contains and reports its asynchronous failures. The
autosave timer delegates to tracked `Task` instances instead of placing the
operation body directly in `async void`. Writer failures are delivered to all
failure subscribers; a subscriber failure is diagnostic only and cannot make
the dispatcher callback unhandled. Shutdown awaits the tracked task set even
when lifecycle cancellation itself reports a callback failure.

## Boundaries

This lifecycle is process-local. It does not define recovery generations,
startup recovery decisions, external-file conflict handling or multi-process
coordination. It also does not promise that every underlying writer can stop
immediately: synchronous DOCX generation and an atomic save beyond its commit
point may finish late, and closing waits for them.
