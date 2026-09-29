using GostEditor.UI.Services;

namespace GostEditor.Tests.Services;

public class DocumentSessionStateTests
{
    [Fact]
    public void NewSession_HasCleanUntitledState()
    {
        DocumentSessionState session = new DocumentSessionState();

        Assert.False(session.IsDirty);
        Assert.False(session.IsRecovered);
        Assert.Null(session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal(0, session.ChangeVersion);
        Assert.Equal(0, session.SavedRevision);
        Assert.Equal("Новый документ", session.DocumentName);
        Assert.Equal("GostEditor - Новый документ", session.WindowTitle);
    }

    [Fact]
    public void MarkDirty_AddsDirtyMarkerToWindowTitle()
    {
        DocumentSessionState session = new DocumentSessionState();

        session.MarkDirty();

        Assert.True(session.IsDirty);
        Assert.Equal(1, session.ChangeVersion);
        Assert.Equal("GostEditor - Новый документ *", session.WindowTitle);
    }

    [Fact]
    public void MarkDirty_IncrementsChangeVersionForEveryChange()
    {
        DocumentSessionState session = new DocumentSessionState();

        session.MarkDirty();
        session.MarkDirty();
        session.MarkDirty();

        Assert.True(session.IsDirty);
        Assert.Equal(3, session.ChangeVersion);
    }

    [Fact]
    public void MarkOpened_StoresFullPathAndResetsState()
    {
        DocumentSessionState session = new DocumentSessionState();
        string path = Path.Combine(Path.GetTempPath(), "document.gost");

        session.MarkDirty();
        session.MarkDirty();
        session.MarkOpened(path);

        Assert.False(session.IsDirty);
        Assert.False(session.IsRecovered);
        Assert.Equal(3, session.ChangeVersion);
        Assert.Equal(3, session.SavedRevision);
        Assert.Equal(Path.GetFullPath(path), session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal("document.gost", session.DocumentName);
        Assert.Equal("GostEditor - document.gost", session.WindowTitle);
    }

    [Fact]
    public void MarkSaved_StoresPathAndSaveTimeWithoutResettingVersion()
    {
        DocumentSessionState session = new DocumentSessionState();
        string path = Path.Combine(Path.GetTempPath(), "saved.gost");
        DateTimeOffset savedAt = new DateTimeOffset(
            2026,
            7,
            31,
            20,
            0,
            0,
            TimeSpan.Zero);

        session.MarkDirty();
        session.MarkDirty();
        session.MarkSaved(path, savedAt, session.ChangeVersion);

        Assert.False(session.IsDirty);
        Assert.False(session.IsRecovered);
        Assert.Equal(2, session.ChangeVersion);
        Assert.Equal(2, session.SavedRevision);
        Assert.Equal(Path.GetFullPath(path), session.CurrentFilePath);
        Assert.Equal(savedAt, session.LastSavedAt);
        Assert.Equal("GostEditor - saved.gost", session.WindowTitle);
    }

    [Fact]
    public void MarkRecovered_ProducesDirtyRecoveredState()
    {
        DocumentSessionState session = new DocumentSessionState();
        string path = Path.Combine(Path.GetTempPath(), "original.gost");

        session.MarkRecovered(path);

        Assert.True(session.IsDirty);
        Assert.True(session.IsRecovered);
        Assert.Equal(1, session.ChangeVersion);
        Assert.Equal(Path.GetFullPath(path), session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal("GostEditor - original.gost *", session.WindowTitle);
    }

    [Fact]
    public void StartNew_ResetsExistingSession()
    {
        DocumentSessionState session = new DocumentSessionState();
        string path = Path.Combine(Path.GetTempPath(), "old.gost");

        session.MarkRecovered(path);
        session.MarkDirty();
        session.StartNew();

        Assert.False(session.IsDirty);
        Assert.False(session.IsRecovered);
        Assert.Equal(3, session.ChangeVersion);
        Assert.Equal(3, session.SavedRevision);
        Assert.Null(session.CurrentFilePath);
        Assert.Null(session.LastSavedAt);
        Assert.Equal("GostEditor - Новый документ", session.WindowTitle);
    }

    [Fact]
    public void WindowTitle_RaisesPropertyChangedWhenDirtyStateChanges()
    {
        DocumentSessionState session = new DocumentSessionState();
        List<string?> changedProperties = new List<string?>();

        session.PropertyChanged += (_, e) =>
            changedProperties.Add(e.PropertyName);

        session.MarkDirty();

        Assert.Contains(nameof(DocumentSessionState.IsDirty), changedProperties);
        Assert.Contains(nameof(DocumentSessionState.WindowTitle), changedProperties);
        Assert.Contains(nameof(DocumentSessionState.ChangeVersion), changedProperties);
    }

    [Fact]
    public void MarkSaved_OlderRevision_KeepsNewerDocumentDirty()
    {
        DocumentSessionState session = new();
        string path = Path.Combine(Path.GetTempPath(), "stale.gost");

        session.RecordMutation();
        long snapshotRevision = session.ChangeVersion;
        session.RecordMutation();

        session.MarkSaved(
            path,
            DateTimeOffset.UtcNow,
            snapshotRevision);

        Assert.True(session.IsDirty);
        Assert.Equal(snapshotRevision + 1, session.ChangeVersion);
        Assert.Equal(snapshotRevision, session.SavedRevision);
    }

    [Fact]
    public void LifecycleBaselines_NeverDecreaseRevision()
    {
        DocumentSessionState session = new();
        List<long> revisions = new();

        session.StartNew();
        revisions.Add(session.ChangeVersion);
        session.RecordMutation();
        revisions.Add(session.ChangeVersion);
        session.MarkOpened(Path.Combine(Path.GetTempPath(), "opened.gost"));
        revisions.Add(session.ChangeVersion);
        session.MarkRecovered(null);
        revisions.Add(session.ChangeVersion);

        Assert.Equal(revisions.Order(), revisions);
        Assert.Equal(revisions.Distinct().Count(), revisions.Count);
    }
}
