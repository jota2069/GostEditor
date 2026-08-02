using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public sealed class AutoSaveService : IDisposable
{
    public static readonly TimeSpan DefaultInterval =
        TimeSpan.FromSeconds(30);

    private readonly RecoveryStorageService _recoveryStorage;
    private readonly DocumentSessionState _session;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly TimeSpan _interval;

    private DispatcherTimer? _timer;
    private Func<GostDocument>? _documentSnapshotProvider;
    private long _lastSavedChangeVersion = -1;
    private bool _disposed;

    public AutoSaveService(
        RecoveryStorageService recoveryStorage,
        DocumentSessionState session)
        : this(
            recoveryStorage,
            session,
            DefaultInterval)
    {
    }

    public AutoSaveService(
        RecoveryStorageService recoveryStorage,
        DocumentSessionState session,
        TimeSpan interval)
    {
        _recoveryStorage = recoveryStorage
            ?? throw new ArgumentNullException(
                nameof(recoveryStorage));

        _session = session
            ?? throw new ArgumentNullException(
                nameof(session));

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "Интервал автосохранения должен быть больше нуля.");
        }

        _interval = interval;
    }

    public event Action<DateTimeOffset>? Saved;

    public event Action<Exception>? Failed;

    public bool IsRunning => _timer?.IsEnabled == true;

    public long LastSavedChangeVersion =>
        Interlocked.Read(ref _lastSavedChangeVersion);

    public void Start(
        Func<GostDocument> documentSnapshotProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _documentSnapshotProvider =
            documentSnapshotProvider
            ?? throw new ArgumentNullException(
                nameof(documentSnapshotProvider));

        if (_timer is null)
        {
            _timer = new DispatcherTimer
            {
                Interval = _interval
            };

            _timer.Tick += OnTimerTick;
        }

        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
    }

    public Task<bool> SaveIfNeededAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Func<GostDocument>? provider =
            _documentSnapshotProvider;

        return provider is null
            ? Task.FromResult(false)
            : SaveIfNeededAsync(provider);
    }

    public async Task<bool> SaveIfNeededAsync(
        Func<GostDocument> documentSnapshotProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentNullException.ThrowIfNull(
            documentSnapshotProvider);

        if (!_session.IsDirty)
        {
            return false;
        }

        long candidateVersion =
            _session.ChangeVersion;

        if (candidateVersion <= 0 ||
            candidateVersion == LastSavedChangeVersion)
        {
            return false;
        }

        if (!await _saveGate.WaitAsync(0))
        {
            return false;
        }

        try
        {
            if (!_session.IsDirty)
            {
                return false;
            }

            candidateVersion =
                _session.ChangeVersion;

            if (candidateVersion <= 0 ||
                candidateVersion == LastSavedChangeVersion)
            {
                return false;
            }

            GostDocument snapshot =
                documentSnapshotProvider()
                ?? throw new InvalidOperationException(
                    "Поставщик документа вернул null.");

            await _recoveryStorage.SaveAsync(
                snapshot,
                _session.CurrentFilePath);

            Interlocked.Exchange(
                ref _lastSavedChangeVersion,
                candidateVersion);

            DateTimeOffset savedAt =
                DateTimeOffset.UtcNow;

            Saved?.Invoke(savedAt);

            return true;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task ClearRecoveryAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _saveGate.WaitAsync();

        try
        {
            _recoveryStorage.DeleteRecovery();

            Interlocked.Exchange(
                ref _lastSavedChangeVersion,
                _session.ChangeVersion);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task ResetAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _saveGate.WaitAsync();

        try
        {
            _recoveryStorage.DeleteRecovery();

            Interlocked.Exchange(
                ref _lastSavedChangeVersion,
                -1);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        _documentSnapshotProvider = null;
        _saveGate.Dispose();
    }

    private async void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        try
        {
            await SaveIfNeededAsync();
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }
}
