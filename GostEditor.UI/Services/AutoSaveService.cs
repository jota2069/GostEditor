using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly PersistenceIoCoordinator _ioCoordinator;
    private readonly TimeSpan _interval;
    private readonly object _scheduledOperationsSync = new();
    private readonly HashSet<Task> _scheduledOperations = new();

    private DispatcherTimer? _timer;
    private Func<GostDocument>? _documentSnapshotProvider;
    private long _lastSavedChangeVersion = -1;
    private bool _acceptScheduledOperations;
    private bool _disposed;

    public AutoSaveService(
        RecoveryStorageService recoveryStorage,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator)
        : this(
            recoveryStorage,
            session,
            ioCoordinator,
            DefaultInterval)
    {
    }

    public AutoSaveService(
        RecoveryStorageService recoveryStorage,
        DocumentSessionState session,
        PersistenceIoCoordinator ioCoordinator,
        TimeSpan interval)
    {
        _recoveryStorage = recoveryStorage
            ?? throw new ArgumentNullException(
                nameof(recoveryStorage));

        _session = session
            ?? throw new ArgumentNullException(
                nameof(session));

        _ioCoordinator = ioCoordinator
            ?? throw new ArgumentNullException(
                nameof(ioCoordinator));

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

        lock (_scheduledOperationsSync)
        {
            _acceptScheduledOperations = true;
        }

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
        lock (_scheduledOperationsSync)
        {
            _acceptScheduledOperations = false;
        }

        _timer?.Stop();
    }

    public Task<bool> SaveIfNeededAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Func<GostDocument>? provider =
            _documentSnapshotProvider;

        return provider is null
            ? Task.FromResult(false)
            : SaveIfNeededAsync(provider, cancellationToken);
    }

    public async Task<bool> SaveIfNeededAsync(
        Func<GostDocument> documentSnapshotProvider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ArgumentNullException.ThrowIfNull(
            documentSnapshotProvider);

        cancellationToken.ThrowIfCancellationRequested();

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

        if (!_ioCoordinator.TryAcquire(
                PersistenceIoOperation.AutoSave,
                cancellationToken,
                out PersistenceIoCoordinator.PersistenceIoLease? ownership))
        {
            return false;
        }

        using (ownership)
        {
            CancellationToken operationCancellation =
                ownership!.CancellationToken;
            operationCancellation.ThrowIfCancellationRequested();

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
                _session.CurrentFilePath,
                operationCancellation);

            Interlocked.Exchange(
                ref _lastSavedChangeVersion,
                candidateVersion);

            DateTimeOffset savedAt =
                DateTimeOffset.UtcNow;

            Saved?.Invoke(savedAt);

            return true;
        }
    }

    public async Task<bool> ClearRecoveryAsync(
        long? expectedCleanRevision = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await _ioCoordinator.AcquireAsync(
                PersistenceIoOperation.RecoveryMaintenance,
                cancellationToken);

        ownership.CancellationToken.ThrowIfCancellationRequested();

        if (expectedCleanRevision.HasValue &&
            (_session.IsDirty ||
             _session.ChangeVersion != expectedCleanRevision.Value ||
             _session.SavedRevision != expectedCleanRevision.Value))
        {
            return false;
        }

        _recoveryStorage.DeleteRecovery();

        Interlocked.Exchange(
            ref _lastSavedChangeVersion,
            expectedCleanRevision ?? _session.ChangeVersion);

        return true;
    }

    public async Task ResetAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using PersistenceIoCoordinator.PersistenceIoLease ownership =
            await _ioCoordinator.AcquireAsync(
                PersistenceIoOperation.RecoveryMaintenance,
                cancellationToken);

        ownership.CancellationToken.ThrowIfCancellationRequested();
        _recoveryStorage.DeleteRecovery();

        Interlocked.Exchange(
            ref _lastSavedChangeVersion,
            -1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        if (_timer is not null)
        {
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        _documentSnapshotProvider = null;
    }

    internal Task WaitForScheduledOperationsAsync()
    {
        lock (_scheduledOperationsSync)
        {
            return _scheduledOperations.Count == 0
                ? Task.CompletedTask
                : Task.WhenAll([.. _scheduledOperations]);
        }
    }

    internal Task RunScheduledSaveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_scheduledOperationsSync)
        {
            if (!_acceptScheduledOperations)
            {
                return Task.CompletedTask;
            }

            Task operation = RunScheduledSaveCoreAsync();
            _scheduledOperations.Add(operation);
            _ = RemoveScheduledOperationWhenCompleteAsync(operation);
            return operation;
        }
    }

    private void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        try
        {
            _ = RunScheduledSaveAsync();
        }
        catch (ObjectDisposedException)
        {
            // A dispatcher callback already queued before Dispose is stale.
        }
    }

    private async Task RunScheduledSaveCoreAsync()
    {
        // Ensure the operation is registered before any provider, writer or
        // event callback can complete synchronously.
        await Task.Yield();

        try
        {
            await SaveIfNeededAsync();
        }
        catch (OperationCanceledException)
            when (_ioCoordinator.IsSuspended)
        {
            // Application shutdown deliberately cancels active persistence.
        }
        catch (Exception exception)
        {
            NotifyFailure(exception);
        }
    }

    private async Task RemoveScheduledOperationWhenCompleteAsync(
        Task operation)
    {
        try
        {
            await operation;
        }
        finally
        {
            lock (_scheduledOperationsSync)
            {
                _scheduledOperations.Remove(operation);
            }
        }
    }

    private void NotifyFailure(Exception exception)
    {
        Delegate[] subscribers =
            Failed?.GetInvocationList() ?? [];

        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((Action<Exception>)subscriber)(exception);
            }
            catch (Exception subscriberException)
            {
                Debug.WriteLine(
                    "[AUTOSAVE] Обработчик ошибки автосохранения " +
                    $"завершился с ошибкой: {subscriberException}");
            }
        }
    }
}
