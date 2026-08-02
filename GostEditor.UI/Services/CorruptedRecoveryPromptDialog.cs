using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GostEditor.UI.Services;

public enum CorruptedRecoveryDecision
{
    Exit = 0,
    Delete = 1
}

public sealed class CorruptedRecoveryPromptDialog : Window
{
    public CorruptedRecoveryPromptDialog(string problemDescription)
    {
        Title = "Повреждённая аварийная копия";
        Width = 560;
        Height = 290;
        MinWidth = 560;
        MinHeight = 290;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBlock title = new()
        {
            Text = "Аварийную копию невозможно восстановить",
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };

        TextBlock description = new()
        {
            Text =
                $"{problemDescription}\n\n" +
                "Автосохранение не будет запущено, пока повреждённая " +
                "копия не удалена. Поэтому она не будет незаметно " +
                "перезаписана новой сессией.",
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21
        };

        Button deleteButton = new()
        {
            Content = "Удалить копию",
            MinWidth = 130,
            Padding = new Thickness(16, 8)
        };

        Button exitButton = new()
        {
            Content = "Закрыть программу",
            MinWidth = 150,
            Padding = new Thickness(16, 8)
        };

        deleteButton.Click += (_, _) =>
            Close(CorruptedRecoveryDecision.Delete);

        exitButton.Click += (_, _) =>
            Close(CorruptedRecoveryDecision.Exit);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                title,
                description,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        exitButton,
                        deleteButton
                    }
                }
            }
        };
    }
}
