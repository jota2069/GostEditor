using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public partial class DocumentSessionState : ObservableObject
{
    private long _changeVersion;

    private long _savedRevision;

    private bool _isDirty;

    private FileContentFingerprint? _fileFingerprint;

    private bool _hasFileFingerprintBaseline;

    private GostDocument? _activeDocument;

    private long _documentGeneration;

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

    public FileContentFingerprint? FileFingerprint
    {
        get => _fileFingerprint;
        private set => SetProperty(ref _fileFingerprint, value);
    }

    public bool HasFileFingerprintBaseline
    {
        get => _hasFileFingerprintBaseline;
        private set => SetProperty(
            ref _hasFileFingerprintBaseline,
            value);
    }

    public long DocumentGeneration => _documentGeneration;

    private string? _currentFilePath;

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        private set
        {
            if (SetProperty(ref _currentFilePath, value))
            {
                OnPropertyChanged(nameof(DocumentName));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    private DateTimeOffset? _lastSavedAt;

    public DateTimeOffset? LastSavedAt
    {
        get => _lastSavedAt;
        private set => SetProperty(ref _lastSavedAt, value);
    }

    private bool _isRecovered;

    public bool IsRecovered
    {
        get => _isRecovered;
        private set => SetProperty(ref _isRecovered, value);
    }

    public string DocumentName =>
        string.IsNullOrWhiteSpace(CurrentFilePath)
            ? "Новый документ"
            : Path.GetFileName(CurrentFilePath);

    public string WindowTitle =>
        $"GostEditor - {DocumentName}{(IsDirty ? " *" : string.Empty)}";

    public void StartNew(GostDocument document)
    {
        BindDocument(document, forceNewGeneration: true);
        AdvanceToCleanBaseline();
        CurrentFilePath = null;
        LastSavedAt = null;
        IsRecovered = false;
        FileFingerprint = null;
        HasFileFingerprintBaseline = false;
    }

    public void ActivateDocument(GostDocument document) =>
        BindDocument(document, forceNewGeneration: false);

    public void MarkOpened(
        GostDocument document,
        string filePath,
        FileContentFingerprint? fileFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string normalizedPath = Path.GetFullPath(filePath);
        BindDocument(document, forceNewGeneration: true);

        CurrentFilePath = normalizedPath;
        LastSavedAt = null;
        IsRecovered = false;
        FileFingerprint = fileFingerprint;
        HasFileFingerprintBaseline = true;
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
        long savedRevision,
        FileContentFingerprint? fileFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (savedRevision < 0 || savedRevision > ChangeVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(savedRevision));
        }

        CurrentFilePath = Path.GetFullPath(filePath);
        LastSavedAt = savedAt;
        IsRecovered = false;
        FileFingerprint = fileFingerprint;
        HasFileFingerprintBaseline = fileFingerprint is not null;
        SavedRevision = savedRevision;
        UpdateDirtyState();
    }

    public void MarkRecovered(
        GostDocument document,
        string? originalFilePath)
    {
        string? normalizedPath = string.IsNullOrWhiteSpace(originalFilePath)
            ? null
            : Path.GetFullPath(originalFilePath);
        BindDocument(document, forceNewGeneration: true);
        CurrentFilePath = normalizedPath;

        LastSavedAt = null;
        IsRecovered = true;
        FileFingerprint = null;
        HasFileFingerprintBaseline = false;
        ChangeVersion = checked(ChangeVersion + 1);
        SavedRevision = ChangeVersion - 1;
        UpdateDirtyState();
    }

    public DocumentSessionCheckpoint CaptureCheckpoint(
        GostDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(_activeDocument, document))
        {
            throw new DocumentSessionChangedException();
        }

        return new DocumentSessionCheckpoint(
            document,
            DocumentGeneration,
            ChangeVersion,
            CurrentFilePath);
    }

    public bool IsCurrent(DocumentSessionCheckpoint checkpoint) =>
        ReferenceEquals(_activeDocument, checkpoint.Document) &&
        DocumentGeneration == checkpoint.DocumentGeneration;

    public bool IsUnchanged(DocumentSessionCheckpoint checkpoint) =>
        IsCurrent(checkpoint) &&
        ChangeVersion == checkpoint.ChangeVersion &&
        PathsEqual(CurrentFilePath, checkpoint.CurrentFilePath);

    public void EnsureCurrent(DocumentSessionCheckpoint checkpoint)
    {
        if (!IsCurrent(checkpoint))
        {
            throw new DocumentSessionChangedException();
        }
    }

    public void EnsureUnchanged(DocumentSessionCheckpoint checkpoint)
    {
        if (!IsUnchanged(checkpoint))
        {
            throw new DocumentSessionChangedException();
        }
    }

    internal DocumentSessionSnapshot CaptureState() =>
        new(
            _activeDocument,
            _documentGeneration,
            _changeVersion,
            _savedRevision,
            _isDirty,
            _fileFingerprint,
            _hasFileFingerprintBaseline,
            _currentFilePath,
            _lastSavedAt,
            _isRecovered);

    internal void RestoreState(
        DocumentSessionSnapshot snapshot,
        Exception primaryException)
    {
        long latestGeneration = _documentGeneration;
        _activeDocument = snapshot.ActiveDocument;
        _documentGeneration = Math.Max(
            latestGeneration,
            snapshot.DocumentGeneration);
        _changeVersion = snapshot.ChangeVersion;
        _savedRevision = snapshot.SavedRevision;
        _isDirty = snapshot.IsDirty;
        _fileFingerprint = snapshot.FileFingerprint;
        _hasFileFingerprintBaseline = snapshot.HasFileFingerprintBaseline;
        _currentFilePath = snapshot.CurrentFilePath;
        _lastSavedAt = snapshot.LastSavedAt;
        _isRecovered = snapshot.IsRecovered;

        try
        {
            OnPropertyChanged(string.Empty);
        }
        catch (Exception rollbackException)
        {
            primaryException.Data[
                "GostEditor.DocumentPublication.SessionRollback"] =
                rollbackException;
        }
    }

    private void BindDocument(
        GostDocument document,
        bool forceNewGeneration)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!forceNewGeneration &&
            ReferenceEquals(_activeDocument, document))
        {
            return;
        }

        _activeDocument = document;
        _documentGeneration = checked(_documentGeneration + 1);
        OnPropertyChanged(nameof(DocumentGeneration));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
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

public sealed record DocumentSessionCheckpoint(
    GostDocument Document,
    long DocumentGeneration,
    long ChangeVersion,
    string? CurrentFilePath);

internal sealed record DocumentSessionSnapshot(
    GostDocument? ActiveDocument,
    long DocumentGeneration,
    long ChangeVersion,
    long SavedRevision,
    bool IsDirty,
    FileContentFingerprint? FileFingerprint,
    bool HasFileFingerprintBaseline,
    string? CurrentFilePath,
    DateTimeOffset? LastSavedAt,
    bool IsRecovered);

public sealed class DocumentSessionChangedException : InvalidOperationException
{
    public DocumentSessionChangedException()
        : base("Активный документ изменился до завершения операции.")
    {
    }
}
