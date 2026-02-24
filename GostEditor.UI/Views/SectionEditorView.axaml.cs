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

        // Грузим 100% чистый текст без извращений с Replace
        string fullText = section.Content ?? string.Empty;

        if (string.IsNullOrEmpty(fullText))
        {
            AddPage("");
        }
        else
        {
            AddPage(fullText);
        }
    }

    private DocumentPageView AddPage(string initialText = "")
    {
        int pageNumber = _pages.Count + 1;
        DocumentPageView page = new DocumentPageView(pageNumber, initialText);

        page.PageOverflow += OnPageOverflow;
        page.TextChanged += _ => SaveToSection();
        page.RequestPageChange += OnRequestPageChange;

        _pages.Add(page);
        PagesContainer.Children.Add(page);

        return page;
    }

    private void OnRequestPageChange(DocumentPageView senderPage, int direction)
    {
        int senderIndex = _pages.IndexOf(senderPage);
        if (senderIndex == -1) return;

        int targetIndex = senderIndex + direction;

        if (targetIndex >= 0 && targetIndex < _pages.Count)
        {
            DocumentPageView targetPage = _pages[targetIndex];

            if (direction == 1)
            {
                targetPage.FocusEditor(0);
            }
            else
            {
                targetPage.FocusEditor(-1);
            }
        }
    }

    private void OnPageOverflow(DocumentPageView senderPage, string overflowText, int caretOffset)
    {
        int senderIndex = _pages.IndexOf(senderPage);
        if (senderIndex == -1) return;

        DocumentPageView targetPage;

        if (senderIndex + 1 < _pages.Count)
        {
            targetPage = _pages[senderIndex + 1];
            string existing = targetPage.GetText();
            targetPage.SetText(overflowText + existing);
        }
        else
        {
            targetPage = AddPage(overflowText);
            UpdatePageNumbers();
        }

        if (caretOffset >= 0)
        {
            targetPage.FocusEditor(caretOffset);
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
        _section.Content = string.Join("", _pages.Select(p => p.GetText()));
    }
}
