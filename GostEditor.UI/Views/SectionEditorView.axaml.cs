using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using GostEditor.Core.Models;

namespace GostEditor.UI.Views;

public partial class SectionEditorView : UserControl
{
    private DocumentSection? _section;
    private readonly List<DocumentPageView> _pages = [];

    public SectionEditorView()
    {
        InitializeComponent();
    }

    public void LoadSection(DocumentSection section)
    {
        _section = section;
        _pages.Clear();
        PagesContainer.Children.Clear();

        string fullText = section.Content ?? string.Empty;
        const int maxLines = 34;

        string[] allLines = fullText.Split('\n');
        List<string> pageTexts = [];

        for (int i = 0; i < allLines.Length; i += maxLines)
        {
            string[] chunk = allLines[i..Math.Min(i + maxLines, allLines.Length)];
            pageTexts.Add(string.Join("\n", chunk));
        }

        if (pageTexts.Count == 0)
        {
            pageTexts.Add(string.Empty);
        }

        foreach (string pageText in pageTexts)
        {
            AddPage(pageText);
        }
    }

    private DocumentPageView AddPage(string initialText = "")
    {
        int pageNumber = _pages.Count + 1;
        DocumentPageView page = new DocumentPageView(pageNumber, initialText);

        page.PageOverflow += OnPageOverflow;
        page.TextChanged += _ => SaveToSection();

        _pages.Add(page);
        PagesContainer.Children.Add(page);

        return page;
    }

    // 🔥 ИСПРАВЛЕНИЕ 2: Принимаем саму страницу-отправителя
    private void OnPageOverflow(DocumentPageView senderPage, string overflowText, bool focusNextPage)
    {
        // Находим ТОЧНЫЙ индекс страницы, которая переполнилась
        int senderIndex = _pages.IndexOf(senderPage);
        if (senderIndex == -1) return;

        DocumentPageView targetPage;

        // Если следующая страница уже существует, льем текст на неё
        if (senderIndex + 1 < _pages.Count)
        {
            targetPage = _pages[senderIndex + 1];
            string existing = targetPage.GetText();

            // Аккуратная склейка текста
            string combined = overflowText + (string.IsNullOrEmpty(existing) || existing.StartsWith(" ") || existing.StartsWith("\n") ? existing : " " + existing);
            targetPage.SetText(combined);
        }
        else // Если следующей страницы нет — создаем новую
        {
            targetPage = AddPage(overflowText);
            UpdatePageNumbers();
        }

        // Двигаем курсор, только если текст небольшой (пользователь печатает)
        if (focusNextPage && overflowText.Length < 1000)
        {
            targetPage.FocusEditor();
        }

        SaveToSection();
    }

    private void UpdatePageNumbers()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].SetPageNumber(i + 1);
        }
    }

    private void SaveToSection()
    {
        if (_section is null) return;
        _section.Content = string.Join("\n", _pages.Select(p => p.GetText()));
    }
}
