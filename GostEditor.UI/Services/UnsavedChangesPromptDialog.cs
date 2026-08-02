using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GostEditor.UI.Services;

public enum UnsavedChangesDecision
{
    Cancel = 0,
    Save = 1,
    Discard = 2
}

public sealed class UnsavedChangesPromptDialog : Window
{
    public UnsavedChangesPromptDialog(string documentName)
    {
        string displayName = string.IsNullOrWhiteSpace(documentName)
            ? "Новый документ"
            : documentName;

        Title = "Несохранённые изменения";
        Width = 540;
        Height = 250;
        MinWidth = 540;
        MinHeight = 250;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBlock title = new()
        {
            Text = "Сохранить изменения?",
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };

        TextBlock description = new()
        {
            Text =
                $"Документ «{displayName}» содержит несохранённые изменения.\n\n" +
                "Сохранить изменения перед продолжением?",
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21
        };

        Button saveButton = new()
        {
            Content = "Сохранить",
            MinWidth = 110,
            Padding = new Thickness(16, 8)
        };

        Button discardButton = new()
        {
            Content = "Не сохранять",
            MinWidth = 130,
            Padding = new Thickness(16, 8)
        };

        Button cancelButton = new()
        {
            Content = "Отмена",
            MinWidth = 100,
            Padding = new Thickness(16, 8)
        };

        saveButton.Click += (_, _) =>
            Close(UnsavedChangesDecision.Save);

        discardButton.Click += (_, _) =>
            Close(UnsavedChangesDecision.Discard);

        cancelButton.Click += (_, _) =>
            Close(UnsavedChangesDecision.Cancel);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                cancelButton,
                discardButton,
                saveButton
            }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                title,
                description,
                buttons
            }
        };
    }
}