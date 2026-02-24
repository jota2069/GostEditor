using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GostEditor.Core;
using GostEditor.UI.Services;
using GostEditor.UI.ViewModels;
using GostEditor.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GostEditor.UI;

public partial class App : Application
{
    // Контейнер зависимостей для всего приложения.
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Собираем сервисы.
        ServiceCollection services = new ServiceCollection();

        // Регистрируем всё из Core одной строкой (метод лида).
        services.AddGostEditorCore();

        // Регистрируем UI-специфичные сервисы.
        services.AddSingleton<DialogService>();
        
        services.AddTransient<MainWindowViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Освобождаем ресурсы при закрытии.
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}