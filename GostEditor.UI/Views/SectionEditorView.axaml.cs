using Avalonia.Controls;
using GostEditor.Core.Models;
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

        // Разбиваем существующий текст по страницам.
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

    private void OnPageOverflow(string overflowText)
    {
        int overflowIndex = _pages.Count - 1;

        for (int i = 0; i < _pages.Count; i++)
        {
            if (PagesContainer.Children[i] == _pages[i])
            {
                overflowIndex = i;
                break;
            }
        }

        if (overflowIndex + 1 < _pages.Count)
        {
            string existing = _pages[overflowIndex + 1].GetText();
            string combined = overflowText + "\n" + existing;
            _pages[overflowIndex + 1].SetText(combined);
        }
        else
        {
            DocumentPageView newPage = AddPage(overflowText);
            UpdatePageNumbers();
            newPage.FocusEditor();
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
        if (_section is null)
        {
            return;
        }

        // Собираем текст со всех страниц.
        _section.Content = string.Join("\n", _pages.Select(p => p.GetText()));
    }



}
