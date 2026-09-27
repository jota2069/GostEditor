using GostEditor.Core.DocumentModel;

namespace GostEditor.Core.Layout.Incremental;

public sealed class BlockLayoutCache<TLayout>
{
    private readonly Dictionary<DocumentNodeId, CacheEntry> _entries = new();

    public int Count => _entries.Count;

    public void Store(
        DocumentNodeId blockId,
        long contentVersion,
        double availableWidth,
        TLayout layout)
    {
        _entries[blockId] = new CacheEntry(
            contentVersion,
            availableWidth,
            layout);
    }

    public bool TryGet(
        DocumentNodeId blockId,
        long contentVersion,
        double availableWidth,
        out TLayout? layout)
    {
        if (_entries.TryGetValue(blockId, out CacheEntry? entry) &&
            entry.ContentVersion == contentVersion &&
            entry.AvailableWidth.Equals(availableWidth))
        {
            layout = entry.Layout;
            return true;
        }

        layout = default;
        return false;
    }

    public bool Remove(DocumentNodeId blockId) =>
        _entries.Remove(blockId);

    public void Retain(IReadOnlySet<DocumentNodeId> liveBlockIds)
    {
        ArgumentNullException.ThrowIfNull(liveBlockIds);

        foreach (DocumentNodeId id in _entries.Keys.ToArray())
        {
            if (!liveBlockIds.Contains(id))
            {
                _entries.Remove(id);
            }
        }
    }

    public void Clear() => _entries.Clear();

    private sealed record CacheEntry(
        long ContentVersion,
        double AvailableWidth,
        TLayout Layout);
}
