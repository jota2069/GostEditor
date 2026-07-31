using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GostEditor.UI.Services;

public partial class DocumentSessionState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;

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
        CurrentFilePath = null;
        LastSavedAt = null;
        IsRecovered = false;
        IsDirty = false;
    }

    public void MarkOpened(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        CurrentFilePath = Path.GetFullPath(filePath);
        LastSavedAt = null;
        IsRecovered = false;
        IsDirty = false;
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void MarkSaved(string filePath, DateTimeOffset savedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        CurrentFilePath = Path.GetFullPath(filePath);
        LastSavedAt = savedAt;
        IsRecovered = false;
        IsDirty = false;
    }

    public void MarkRecovered(string? originalFilePath)
    {
        CurrentFilePath = string.IsNullOrWhiteSpace(originalFilePath)
            ? null
            : Path.GetFullPath(originalFilePath);

        LastSavedAt = null;
        IsRecovered = true;
        IsDirty = true;
    }
}