using GostEditor.Core.Serialization.Format;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostMigrationContext
{
    public GostMigrationContext(
        int sourceVersion,
        GostDocumentV1Dto legacyDocument,
        GostArchiveEntryIndex entries,
        bool formatVersionWasPresent = true)
    {
        SourceVersion = sourceVersion;
        WorkingVersion = sourceVersion;
        LegacyDocument = legacyDocument;
        Entries = entries;
        FormatVersionWasPresent = formatVersionWasPresent;
    }

    public GostMigrationContext(
        GostMigratableDocument document,
        GostArchiveEntryIndex entries,
        GostSerializationDiagnostics diagnostics)
    {
        SourceVersion = GostFormatVersions.Current;
        WorkingVersion = GostFormatVersions.Current;
        Document = document;
        Entries = entries;
        Diagnostics = diagnostics;
    }

    public int SourceVersion { get; }
    public bool FormatVersionWasPresent { get; }
    public int WorkingVersion { get; set; }
    public GostDocumentV1Dto? LegacyDocument { get; set; }
    public GostMigratableDocument? Document { get; set; }
    public GostArchiveEntryIndex Entries { get; }
    public GostSerializationDiagnostics Diagnostics { get; } = new();
}
