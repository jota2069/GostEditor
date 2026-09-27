using GostEditor.Core.Editing;
using GostEditor.Core.Models;

namespace GostEditor.Core.DocumentModel.Legacy;

/// <summary>
/// Explicit compatibility boundary between the persisted .gost v2 model and
/// the structured editing model. The bridge avoids changing the v2 archive
/// contract while allowing new editing services to be introduced gradually.
/// </summary>
public sealed class LegacyDocumentEditingBridge
{
    private readonly LegacyDocumentAdapter _adapter;

    public LegacyDocumentEditingBridge()
        : this(new LegacyDocumentAdapter())
    {
    }

    public LegacyDocumentEditingBridge(LegacyDocumentAdapter adapter)
    {
        _adapter = adapter
            ?? throw new ArgumentNullException(nameof(adapter));
    }

    public DocumentEditingSession CreateSession(GostDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentEditingSession(
            _adapter.ToDocumentModel(document));
    }

    public GostDocument CreateLegacySnapshot(
        DocumentEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _adapter.ToLegacyDocument(session.Document);
    }
}
