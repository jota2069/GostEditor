using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    private string _currentFilePath = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(PointerWheelChangedEvent, OnWindowPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGlobalPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GostEditor.UI.ViewModels.MainWindowViewModel vm)
        {
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(vm.SelectedSection))
                {
                    // ВРЕМЕННО ОТКЛЮЧЕНО:
                    // if (vm.SelectedSection != null) SectionEditor.LoadSection(vm.SelectedSection);
                }
            };

            // ВРЕМЕННО ОТКЛЮЧЕНО:
            // if (vm.SelectedSection != null) SectionEditor.LoadSection(vm.SelectedSection);
        }
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        // ВРЕМЕННО ОТКЛЮЧЕНО: SectionEditor?.ApplyFormatting("\uFEFF");
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        // ВРЕМЕННО ОТКЛЮЧЕНО: SectionEditor?.ApplyFormatting("\u2060");
    }

    private void OnClearFormattingClick(object? sender, RoutedEventArgs e)
    {
        // ВРЕМЕННО ОТКЛЮЧЕНО: SectionEditor?.ClearFormatting();
    }

    private void OnStartPageNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue)
        {
            if (CurrentStartPageLabel != null)
            {
                CurrentStartPageLabel.Text = $"(Сейчас: {e.NewValue.Value})";
            }
            // ВРЕМЕННО ОТКЛЮЧЕНО: SectionEditor?.SetStartPageNumber((int)e.NewValue.Value);
        }
    }

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // ВРЕМЕННО ОТКЛЮЧЕНО: Логика горячих клавиш старого редактора отключена на время тестов нового движка

        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S)
        {
            await SaveDocumentAsync();
            e.Handled = true;
        }
    }

    private async void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {
        Window dialog = new Window
        {
            Title = "Внимание",
            Width = 350, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        StackPanel panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Создать новый документ?", Margin = new Avalonia.Thickness(0, 0, 0, 20) });

        StackPanel buttonsPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        Button btnYes = new Button { Content = "Да", Margin = new Avalonia.Thickness(0, 0, 10, 0) };
        Button btnNo = new Button { Content = "Нет" };

        btnYes.Click += async (_, _) =>
        {
            dialog.Close();
            await SaveDocumentAsync();
            _currentFilePath = string.Empty;
            // ВРЕМЕННО ОТКЛЮЧЕНО: SectionEditor?.ClearAll();
        };
        btnNo.Click += (_, _) => { dialog.Close(); };

        buttonsPanel.Children.Add(btnYes);
        buttonsPanel.Children.Add(btnNo);
        panel.Children.Add(buttonsPanel);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    private async Task SaveDocumentAsync()
    {
        // ВРЕМЕННО ОТКЛЮЧЕНО (заглушка для сохранения)
        /*
        if (SectionEditor == null) return;
        ...
        */
        await Task.CompletedTask;
    }

    private void OnWindowPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (DataContext is GostEditor.UI.ViewModels.MainWindowViewModel vm)
            {
                double delta = e.Delta.Y > 0 ? 0.1 : -0.1;
                double newZoom = Math.Round(vm.ZoomLevel + delta, 1);

                if (newZoom >= 0.5 && newZoom <= 2.0)
                {
                    vm.ZoomLevel = newZoom;
                }
                e.Handled = true;
            }
        }
    }
}
