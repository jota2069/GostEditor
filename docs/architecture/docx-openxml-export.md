# DOCX export through Open XML

## Boundary and ownership

DOCX export keeps the Phase 1 persistence boundary:

1. `DocumentExportService` obtains exclusive ownership from the application-scoped I/O coordinator.
2. It captures a `DocumentPersistenceSnapshot` only after ownership has been acquired.
3. `ExportService` passes the snapshot document to `OpenXmlDocxWriter`; the writer never reads the live document graph.
4. `AtomicFileCommitter` writes the package to a same-directory temporary file and atomically publishes it only after the Open XML package has been finalized and durably flushed.

The Open XML writer is an implementation detail. It does not introduce another snapshot, coordinator, or publication mechanism.

## Exported contract

The writer emits a WordprocessingML package containing:

- document page size and margins;
- a default page-number footer;
- the optional title page;
- an optional TOC field with the configured heading depth and a request to update fields when Word opens the document;
- `Normal`, `Heading1`, `Heading2`, `Heading3`, and `Code` paragraph styles;
- paragraphs, page-break flags, alignment, line spacing, indentation, run size, bold, italic, and color;
- inline images, package relationships, image content types, dimensions, numbering, and formatted captions;
- selected bibliography entries ordered by `Order`;
- selected code listings, including the existing 12 pt italic Times New Roman listing title and 10 pt Consolas code.

Raster formats that WordprocessingML can embed directly are kept in their original encoding. Other image formats accepted by the editor are decoded with SkiaSharp and embedded as PNG, so the resulting package does not claim an incorrect content type.

## Failure and cancellation

Package construction occurs inside the existing atomic-file callback. An exception or cancellation before the atomic commit leaves an existing destination unchanged and removes the uncommitted temporary file according to the atomic-commit contract. Cancellation after atomic commit has started follows the commit-point rules documented in `persistence-atomic-file-commit.md`.

## Verification and platform limits

Regression tests inspect the package semantically with the Open XML SDK, run `OpenXmlValidator`, and verify styles, fields, page settings, relationships, image parts, bibliography ordering, listing fidelity, and atomic failure/cancellation behavior.

LibreOffice headless rendering is used as the available Linux runtime smoke test. A real Microsoft Word open/update/render smoke remains an external release gate because Word is not available in the current environment. In particular, automated Linux rendering does not prove Word-specific TOC refresh behavior.
