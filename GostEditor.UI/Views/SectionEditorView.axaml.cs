using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using GostEditor.Core.Models;

namespace GostEditor.UI.Views;

public partial class SectionEditorView : UserControl
{
    private DocumentSection? _section;
    private readonly List<DocumentPageView> _pages = [];
    private DocumentPageView? _activePage;

    public int StartPageNumber { get; private set; } = 1;
    public bool IsGlobalSelectionActive { get; private set; } = false;

    public SectionEditorView()
    {
        InitializeComponent();
    }

    public void SetStartPageNumber(int number)
    {
        StartPageNumber = number;
        UpdatePageNumbers();
    }

    public void ClearAll()
    {
        _pages.Clear();
        PagesContainer.Children.Clear();
        if (_section != null) _section.Content = string.Empty;
        _activePage = null;
        IsGlobalSelectionActive = false;
        AddPage("");
    }

    public string GetFullText()
    {
        return string.Join("", _pages.Select(p => p.GetText()));
    }

    public void ClearFormatting()
    {
        if (IsGlobalSelectionActive)
        {
            foreach (DocumentPageView page in _pages) page.ClearFormatting();
        }
        else if (_activePage != null)
        {
            _activePage.ClearFormatting();
        }
    }

    public void ApplyFormatting(string marker)
    {
        if (_activePage != null) _activePage.WrapSelectedText(marker);
    }

    public void InsertText(string text)
    {
        if (_activePage != null) _activePage.InsertTextAtCaret(text);
    }

    public void SelectAllPages()
    {
        IsGlobalSelectionActive = true;
        foreach (DocumentPageView page in _pages)
        {
            page.SelectAllText();
        }
    }

    private void OnPageInteraction(DocumentPageView activePage)
    {
        _activePage = activePage;
        if (IsGlobalSelectionActive)
        {
            IsGlobalSelectionActive = false;
            foreach (DocumentPageView page in _pages)
            {
                if (page != activePage)
                {
                    page.ClearSelectionVisually();
                }
            }
        }
    }

    public void LoadSection(DocumentSection section)
    {
        _section = section;
        _pages.Clear();
        PagesContainer.Children.Clear();
        _activePage = null;
        IsGlobalSelectionActive = false;

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
        int pageNumber = StartPageNumber + _pages.Count;
        DocumentPageView page = new DocumentPageView(pageNumber, initialText);

        page.PageOverflow += OnPageOverflow;
        page.TextChanged += _ => SaveToSection();
        page.RequestPageChange += OnRequestPageChange;
        page.PageInteraction += OnPageInteraction;

        _pages.Add(page);
        PagesContainer.Children.Add(page);

        _activePage = page;

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
            _activePage = targetPage;
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
            Dispatcher.UIThread.Post(() =>
            {
                targetPage.FocusEditor(caretOffset);
            }, DispatcherPriority.Background);
        }

        SaveToSection();
    }

    private void UpdatePageNumbers()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].SetPageNumber(StartPageNumber + i);
        }
    }

    private void SaveToSection()
    {
        if (_section is null) return;
        _section.Content = string.Join("", _pages.Select(p => p.GetText()));
    }
}
