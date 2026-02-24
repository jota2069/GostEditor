using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Input;

namespace GostEditor.UI.Services;

public sealed class DialogService
{
    private Window? GetMainWindow()
    {
        IClassicDesktopStyleApplicationLifetime? desktop =
            Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;

        return desktop?.MainWindow;
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, string filter)
    {
        Window? window = GetMainWindow();

        if (window is null)
        {
            return null;
        }

        FilePickerOpenOptions options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(filter)
        };

        System.Collections.Generic.IReadOnlyList<IStorageFile> files =
            await window.StorageProvider.OpenFilePickerAsync(options);

        if (files.Count == 0)
        {
            return null;
        }

        return files[0].Path.LocalPath;
    }

    public async Task<string?> ShowSaveFileDialogAsync(
        string title,
        string defaultExtension,
        string filter)
    {
        Window? window = GetMainWindow();

        if (window is null)
        {
            return null;
        }

        FilePickerSaveOptions options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = ParseFilter(filter)
        };

        IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(options);

        return file?.Path.LocalPath;
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        Window? window = GetMainWindow();

        if (window is null)
        {
            return null;
        }

        FolderPickerOpenOptions options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        System.Collections.Generic.IReadOnlyList<IStorageFolder> folders =
            await window.StorageProvider.OpenFolderPickerAsync(options);

        if (folders.Count == 0)
        {
            return null;
        }

        return folders[0].Path.LocalPath;
    }

    public async Task<string?> GetClipboardTextAsync()
    {
        Window? window = GetMainWindow();

        if (window?.Clipboard is null)
        {
            return null;
        }

        return await window.Clipboard.TryGetTextAsync();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        Window? window = GetMainWindow();

        if (window?.Clipboard is null)
        {
            return;
        }

        await window.Clipboard.SetTextAsync(text);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        Window? window = GetMainWindow();

        if (window is null)
        {
            return;
        }

        Window dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            CanResize = false
        };

        Button okButton = new Button
        {
            Content = "Понятно",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24, 8),
            Margin = new Thickness(0, 16, 0, 0)
        };

        StackPanel panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            Foreground = Brushes.Black
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Black,
            FontSize = 13
        });

        panel.Children.Add(okButton);
        dialog.Content = panel;
        okButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(window);
    }

    private List<FilePickerFileType> ParseFilter(string filter)
    {
        List<FilePickerFileType> result = new List<FilePickerFileType>();
        string[] parts = filter.Split('|');

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string name = parts[i];
            string pattern = parts[i + 1];

            FilePickerFileType fileType = new FilePickerFileType(name)
            {
                Patterns = new[] { pattern }
            };

            result.Add(fileType);
        }

        return result;
    }
}
