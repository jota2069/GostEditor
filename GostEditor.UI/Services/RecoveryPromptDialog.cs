using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GostEditor.UI.Services;

public enum RecoveryDecision
{
    Exit = 0,
    Restore = 1,
    Discard = 2
}

public sealed class RecoveryPromptDialog : Window
{
    public RecoveryPromptDialog(RecoveryMetadata? metadata)
    {
        Title = "Восстановление документа";
        Width = 520;
        Height = 270;
        MinWidth = 520;
        MinHeight = 270;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string documentName = GetDocumentName(metadata);
        string savedAt = GetSavedAtText(metadata);

        TextBlock title = new()
        {
            Text = "Найдена аварийная копия",
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };

        TextBlock description = new()
        {
            Text =
                $"Документ: {documentName}\n" +
                $"Время копии: {savedAt}\n\n" +
                "Приложение завершилось до обычного сохранения. " +
                "Можно восстановить последнюю автоматическую копию " +
                "или удалить её.",
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21
        };

        Button restoreButton = new()
        {
            Content = "Восстановить",
            MinWidth = 120,
            Padding = new Thickness(16, 8)
        };

        Button discardButton = new()
        {
            Content = "Удалить копию",
            MinWidth = 120,
            Padding = new Thickness(16, 8)
        };

        Button exitButton = new()
        {
            Content = "Закрыть программу",
            MinWidth = 140,
            Padding = new Thickness(16, 8)
        };

        restoreButton.Click += (_, _) =>
            Close(RecoveryDecision.Restore);

        discardButton.Click += (_, _) =>
            Close(RecoveryDecision.Discard);

        exitButton.Click += (_, _) =>
            Close(RecoveryDecision.Exit);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                exitButton,
                discardButton,
                restoreButton
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

    private static string GetDocumentName(
        RecoveryMetadata? metadata)
    {
        if (string.IsNullOrWhiteSpace(
                metadata?.OriginalFilePath))
        {
            return "Новый документ";
        }

        return Path.GetFileName(
            metadata.OriginalFilePath);
    }

    private static string GetSavedAtText(
        RecoveryMetadata? metadata)
    {
        return metadata is null
            ? "неизвестно"
            : metadata.SavedAtUtc
                .ToLocalTime()
                .ToString("dd.MM.yyyy HH:mm");
    }
}
