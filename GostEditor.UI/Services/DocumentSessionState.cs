using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GostEditor.UI.Services;

public partial class DocumentSessionState : ObservableObject
{
    private long _changeVersion;

    private long _savedRevision;

    private bool _isDirty;

    public long ChangeVersion
    {
        get => _changeVersion;
        private set => SetProperty(ref _changeVersion, value);
    }

    public long SavedRevision
    {
        get => _savedRevision;
        private set => SetProperty(ref _savedRevision, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentName))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _currentFilePath;

    [ObservableProperty]
    private DateTimeOffset? _lastSavedAt;

    [ObservableProperty]
    private bool _isRecovered;

    public string DocumentName =>
        string.IsNullOrWhiteSpace(CurrentFilePath)
            ? "Новый документ"
            : Path.GetFileName(CurrentFilePath);

    public string WindowTitle =>
        $"GostEditor - {DocumentName}{(IsDirty ? " *" : string.Empty)}";

    public void StartNew()
    {
        AdvanceToCleanBaseline();
        CurrentFilePath = null;
        LastSavedAt = null;
        IsRecovered = false;
    }

    public void MarkOpened(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        CurrentFilePath = Path.GetFullPath(filePath);
        LastSavedAt = null;
        IsRecovered = false;
        AdvanceToCleanBaseline();
    }

    public void RecordMutation()
    {
        ChangeVersion = checked(ChangeVersion + 1);
        UpdateDirtyState();
    }

    public void MarkDirty() => RecordMutation();

    public void MarkSaved(
        string filePath,
        DateTimeOffset savedAt,
        long savedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (savedRevision < 0 || savedRevision > ChangeVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(savedRevision));
        }

        CurrentFilePath = Path.GetFullPath(filePath);
        LastSavedAt = savedAt;
        IsRecovered = false;
        SavedRevision = savedRevision;
        UpdateDirtyState();
    }

    public void MarkRecovered(string? originalFilePath)
    {
        CurrentFilePath = string.IsNullOrWhiteSpace(originalFilePath)
            ? null
            : Path.GetFullPath(originalFilePath);

        LastSavedAt = null;
        IsRecovered = true;
        ChangeVersion = checked(ChangeVersion + 1);
        SavedRevision = ChangeVersion - 1;
        UpdateDirtyState();
    }

    private void AdvanceToCleanBaseline()
    {
        ChangeVersion = checked(ChangeVersion + 1);
        SavedRevision = ChangeVersion;
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        IsDirty = ChangeVersion != SavedRevision;
    }
}
