using System;
using System.Threading.Tasks;

namespace GostEditor.UI.Services;

/// <summary>
/// Establishes a stable persistence idle boundary before application shutdown.
/// The service can be resumed when the user cancels closing or needs to perform
/// a final Save from the unsaved-changes prompt.
/// </summary>
public sealed class PersistenceShutdownService
{
    private readonly PersistenceIoCoordinator _ioCoordinator;
    private readonly AutoSaveService _autoSaveService;

    public PersistenceShutdownService(
        PersistenceIoCoordinator ioCoordinator,
        AutoSaveService autoSaveService)
    {
        _ioCoordinator = ioCoordinator
            ?? throw new ArgumentNullException(nameof(ioCoordinator));
        _autoSaveService = autoSaveService
            ?? throw new ArgumentNullException(nameof(autoSaveService));
    }

    public bool IsSuspended => _ioCoordinator.IsSuspended;

    public async Task SuspendAndDrainAsync()
    {
        _autoSaveService.Stop();

        Task coordinatorDrain =
            _ioCoordinator.SuspendAndDrainAsync();

        Task autoSaveDrain =
            _autoSaveService.WaitForScheduledOperationsAsync();

        await Task.WhenAll(coordinatorDrain, autoSaveDrain);
    }

    public void Resume()
    {
        _ioCoordinator.Resume();
    }
}
