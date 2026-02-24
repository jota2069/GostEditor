using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace GostEditor.UI.Services;

/// <summary>
/// Сервис для работы с диалогами ОС: открытие файлов, папок, сохранение, буфер обмена.
/// Core об этом сервисе ничего не знает — он живёт только в UI слое.
/// </summary>
public sealed class DialogService
{
    // Получаем главное окно через IClassicDesktopStyleApplicationLifetime.
    private Window? GetMainWindow()
    {
        IClassicDesktopStyleApplicationLifetime? desktop =
            Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;

        return desktop?.MainWindow;
    }

    /// <summary>
    /// Показывает диалог открытия файла.
    /// Возвращает путь или null если пользователь отменил.
    /// </summary>
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

    /// <summary>
    /// Показывает диалог сохранения файла.
    /// Возвращает путь или null если пользователь отменил.
    /// </summary>
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

    /// <summary>
    /// Показывает диалог выбора папки.
    /// Возвращает путь или null если пользователь отменил.
    /// </summary>
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

    /// <summary>
    /// Получить текст из буфера обмена.
    /// </summary>
    public async Task<string?> GetClipboardTextAsync()
    {
        Window? window = GetMainWindow();

        if (window?.Clipboard is null)
        {
            return null;
        }

        return await window.Clipboard.GetTextAsync();
    }

    /// <summary>
    /// Установить текст в буфер обмена.
    /// </summary>
    public async Task SetClipboardTextAsync(string text)
    {
        Window? window = GetMainWindow();

        if (window?.Clipboard is null)
        {
            return;
        }

        await window.Clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// Парсит строку фильтра формата "Описание (*.ext)|*.ext" в список FilePickerFileType.
    /// </summary>
    private System.Collections.Generic.List<FilePickerFileType> ParseFilter(string filter)
    {
        System.Collections.Generic.List<FilePickerFileType> result =
            new System.Collections.Generic.List<FilePickerFileType>();

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
