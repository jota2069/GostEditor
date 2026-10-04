# External file change conflicts

When a local `.gost` file is opened, GostEditor buffers the exact bytes supplied
to the archive reader and stores their SHA-256 fingerprint in the document
session. A successful save stores the SHA-256 of the exact prepared archive
bytes passed to the atomic committer; it does not trust a post-commit reread of
a path that another process may already have replaced. New and recovered
documents have no baseline until their first successful save.

For an ordinary Save to the current path, the on-disk fingerprint must still
match the session baseline. The check occurs twice while holding the process-wide
I/O coordinator ownership:

1. before the persistence snapshot is serialized;
2. after the temporary file is durably written and closed, immediately before
   the atomic commit starts.

A missing, replaced or content-modified destination causes
`ExternalFileChangedException`; the temporary file is removed and the external
file, session savepoint, current path and live document remain unchanged.

The UI never overwrites silently. It offers three explicit outcomes: cancel,
save to another path, or overwrite the external version. Confirmed overwrite is
an intentional last-writer-wins operation and still uses the normal atomic file
commit. Save As is treated as a new destination selected by the platform file
picker and establishes a new baseline after commit.

There is no portable cross-platform compare-and-swap operation for replacing a
regular file. Another process can still race in the very small interval between
the final fingerprint check and atomic rename, or replace the result immediately
afterward. The two checks close the long serialization window without claiming a
filesystem guarantee that Windows and Unix do not provide. Multi-process locking
is intentionally not introduced.

An external replacement immediately after a successful commit therefore never
becomes a trusted baseline: the following ordinary Save detects its different
content and requires confirmation. A recovered document has an unknown baseline
for its original path, so an existing destination also requires confirmation.
If that path is absent at both checks, recreating it does not overwrite external
content and is allowed.
