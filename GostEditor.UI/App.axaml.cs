using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GostEditor.Core;
using GostEditor.Core.Interfaces;
using GostEditor.UI.Services;
using GostEditor.UI.ViewModels;
using GostEditor.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GostEditor.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ServiceCollection services = new ServiceCollection();

        services.AddGostEditorCore();

        services.AddSingleton<DialogService>();
        services.AddSingleton<DocumentSessionState>();
        services.AddSingleton<RecoveryStorageService>();
        services.AddSingleton<AutoSaveService>();

        services.AddTransient<MainWindowViewModel>();

        ServiceProvider serviceProvider =
            services.BuildServiceProvider();

        _serviceProvider = serviceProvider;

        if (ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = new MainWindow(
                serviceProvider.GetRequiredService<IImageService>(),
                serviceProvider.GetRequiredService<AutoSaveService>(),
                serviceProvider.GetRequiredService<RecoveryStorageService>())
            {
                DataContext =
                    serviceProvider
                        .GetRequiredService<MainWindowViewModel>()
            };

            desktop.MainWindow = mainWindow;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(
        object? sender,
        ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }
}