using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GostEditor.UI.Services;

public enum ExternalFileConflictDecision
{
    Cancel,
    SaveCopy,
    Overwrite
}

public sealed class ExternalFileConflictDialog : Window
{
    public ExternalFileConflictDialog(string fileName)
    {
        Title = "Файл изменён извне";
        Width = 570;
        Height = 285;
        MinWidth = 570;
        MinHeight = 285;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button cancelButton = new()
        {
            Content = "Отмена",
            MinWidth = 100
        };
        Button saveCopyButton = new()
        {
            Content = "Сохранить копию",
            MinWidth = 140
        };
        Button overwriteButton = new()
        {
            Content = "Перезаписать",
            MinWidth = 120
        };

        cancelButton.Click += (_, _) =>
            Close(ExternalFileConflictDecision.Cancel);
        saveCopyButton.Click += (_, _) =>
            Close(ExternalFileConflictDecision.SaveCopy);
        overwriteButton.Click += (_, _) =>
            Close(ExternalFileConflictDecision.Overwrite);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = "Документ на диске был изменён",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text =
                        $"Файл «{fileName}» изменён или заменён другой " +
                        "программой после открытия. Обычное сохранение " +
                        "остановлено, чтобы не затереть внешние изменения.",
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 21
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        cancelButton,
                        saveCopyButton,
                        overwriteButton
                    }
                }
            }
        };
    }
}
