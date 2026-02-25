using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
                    if (vm.SelectedSection != null)
                    {
                        SectionEditor.LoadSection(vm.SelectedSection);
                    }
                }
            };

            if (vm.SelectedSection != null)
            {
                SectionEditor.LoadSection(vm.SelectedSection);
            }
        }
    }

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        SectionEditor?.ApplyFormatting("\uFEFF");
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        SectionEditor?.ApplyFormatting("\u2060");
    }

    private void OnClearFormattingClick(object? sender, RoutedEventArgs e)
    {
        SectionEditor?.ClearFormatting();
    }

    private void OnStartPageNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue)
        {
            if (CurrentStartPageLabel != null)
            {
                CurrentStartPageLabel.Text = $"(Сейчас: {e.NewValue.Value})";
            }

            if (SectionEditor != null)
            {
                SectionEditor.SetStartPageNumber((int)e.NewValue.Value);
            }
        }
    }

    private async void OnGlobalPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (SectionEditor != null && SectionEditor.IsGlobalSelectionActive)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.C)
            {
                IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    _ = clipboard.SetTextAsync(SectionEditor.GetFullText());
                }
                e.Handled = true;
                return;
            }
            else if ((e.KeyModifiers & KeyModifiers.Control) != 0 && e.Key == Key.X)
            {
                IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    _ = clipboard.SetTextAsync(SectionEditor.GetFullText());
                }
                SectionEditor.ClearAll();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                SectionEditor.ClearAll();
                e.Handled = true;
                return;
            }
        }

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (e.Key == Key.B)
            {
                SectionEditor?.ApplyFormatting("\uFEFF");
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                SectionEditor?.ApplyFormatting("\u2060");
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SectionEditor?.InsertText("\f");
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                await SaveDocumentAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                SectionEditor?.SelectAllPages();
                e.Handled = true;
            }
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
            SectionEditor?.ClearAll();
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
        if (SectionEditor == null) return;

        if (string.IsNullOrEmpty(_currentFilePath))
        {
            IStorageProvider storage = TopLevel.GetTopLevel(this)!.StorageProvider;
            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить документ",
                DefaultExtension = "gost",
                SuggestedFileName = "Новый_документ"
            });

            if (file != null)
            {
                _currentFilePath = file.Path.LocalPath;
                await File.WriteAllTextAsync(_currentFilePath, SectionEditor.GetFullText());
            }
        }
        else
        {
            await File.WriteAllTextAsync(_currentFilePath, SectionEditor.GetFullText());
        }
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
