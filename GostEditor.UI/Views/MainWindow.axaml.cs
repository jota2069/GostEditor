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
            vm.PropertyChanged += (object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
            {
                if (args.PropertyName == nameof(vm.SelectedSection))
                {

                }
            };
        }
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ApplyBold();
        MainEditor?.Focus();
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ApplyItalic();
        MainEditor?.Focus();
    }

    private void OnAlignLeftClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignLeft();
        MainEditor?.Focus();
    }

    private void OnAlignCenterClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignCenter();
        MainEditor?.Focus();
    }

    private void OnAlignRightClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignRight();
        MainEditor?.Focus();
    }

    private void OnAlignJustifyClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.AlignJustify();
        MainEditor?.Focus();
    }

    private void OnClearFormattingClick(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Focus();
    }

    private void OnStartPageNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue && CurrentStartPageLabel != null)
        {
            CurrentStartPageLabel.Text = $"(Сейчас: {e.NewValue.Value})";
        }
    }

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.S)
        {
            await SaveDocumentAsync();
            e.Handled = true;
        }
    }

    private async void OnNewDocumentClick(object? sender, RoutedEventArgs e)
    {

    }

    private async Task SaveDocumentAsync()
    {
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
                if (newZoom >= 0.5 && newZoom <= 2.0) vm.ZoomLevel = newZoom;
                e.Handled = true;
            }
        }
    }
}
