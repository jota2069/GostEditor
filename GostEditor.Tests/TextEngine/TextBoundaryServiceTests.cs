using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.TextEngine;

public sealed class TextBoundaryServiceTests
{
    public static TheoryData<string> ComplexTextElements => new()
    {
        "😀",
        "e\u0301",
        "и\u0306",
        "👍🏽",
        "👨‍👩‍👧‍👦",
        "❤\uFE0F"
    };

    [Theory]
    [MemberData(nameof(ComplexTextElements))]
    public void Boundaries_TreatComplexSequenceAsOneTextElement(
        string textElement)
    {
        TextBoundaryService boundaries = TextBoundaryService.Default;
        string text = $"A{textElement}B";
        int elementEnd = 1 + textElement.Length;

        Assert.Equal(
            new[] { 0, 1, elementEnd, elementEnd + 1 },
            boundaries.GetBoundaries(text));
        for (int offset = 2; offset < elementEnd; offset++)
        {
            Assert.False(boundaries.IsBoundary(text, offset));
            Assert.Equal(1, boundaries.Normalize(
                text,
                offset,
                TextBoundaryAffinity.Backward));
            Assert.Equal(elementEnd, boundaries.Normalize(
                text,
                offset,
                TextBoundaryAffinity.Forward));
        }
    }

    [Fact]
    public void Utf16Validation_RejectsUnpairedSurrogates()
    {
        TextBoundaryService boundaries = TextBoundaryService.Default;

        Assert.True(boundaries.IsValidUtf16("A😀B"));
        Assert.False(boundaries.IsValidUtf16("A\uD83DB"));
        Assert.False(boundaries.IsValidUtf16("A\uDE00B"));
    }

    [Theory]
    [MemberData(nameof(ComplexTextElements))]
    public void Navigation_MovesAcrossWholeTextElement(string textElement)
    {
        DocumentEditor editor = CreateEditor(textElement);

        editor.MoveRight();
        Assert.Equal(textElement.Length, editor.CaretPosition.Offset);
        editor.MoveLeft();
        Assert.Equal(0, editor.CaretPosition.Offset);
    }

    [Theory]
    [MemberData(nameof(ComplexTextElements))]
    public void Backspace_RemovesWholeTextElementAndUndoRedoIsExact(
        string textElement)
    {
        DocumentEditor editor = CreateEditor(textElement);
        editor.CaretPosition = new DocumentPosition(0, textElement.Length);

        editor.Backspace();
        Assert.Equal(string.Empty, GetText(editor));
        Assert.Equal(0, editor.CaretPosition.Offset);

        editor.History.Undo();
        Assert.Equal(textElement, GetText(editor));
        Assert.Equal(textElement.Length, editor.CaretPosition.Offset);
        Assert.True(editor.TextBoundaries.IsBoundary(
            GetText(editor),
            editor.CaretPosition.Offset));

        editor.History.Redo();
        Assert.Equal(string.Empty, GetText(editor));
        Assert.Equal(0, editor.CaretPosition.Offset);
    }

    [Theory]
    [MemberData(nameof(ComplexTextElements))]
    public void DeleteForward_RemovesWholeTextElementAndUndoRestoresIt(
        string textElement)
    {
        DocumentEditor editor = CreateEditor(textElement);

        editor.DeleteForward();
        Assert.Equal(string.Empty, GetText(editor));
        Assert.Equal(0, editor.CaretPosition.Offset);

        editor.History.Undo();
        Assert.Equal(textElement, GetText(editor));
        Assert.Equal(0, editor.CaretPosition.Offset);
    }

    [Fact]
    public void Backspace_RemovesTextElementSpanningFormattingRuns()
    {
        DocumentEditor editor = CreateEditorFromRuns("е", "\u0301");
        editor.CaretPosition = new DocumentPosition(0, 2);

        editor.Backspace();

        Assert.Equal(string.Empty, GetText(editor));
        editor.History.Undo();
        Assert.Equal("е\u0301", GetText(editor));
        Assert.Equal(2, editor.CaretPosition.Offset);
    }

    [Fact]
    public void SeparateCommittedInputChunks_FormOneSafeTextElement()
    {
        DocumentEditor editor = CreateEditor(string.Empty);

        editor.InsertText("е");
        editor.InsertText("\u0301");

        Assert.Equal("е\u0301", GetText(editor));
        Assert.Equal(2, editor.CaretPosition.Offset);
        Assert.True(editor.TextBoundaries.IsBoundary(
            GetText(editor),
            editor.CaretPosition.Offset));

        editor.Backspace();
        Assert.Equal(string.Empty, GetText(editor));
    }

    [Fact]
    public void InsertText_InvalidUtf16IsRejectedWithoutMutation()
    {
        DocumentEditor editor = CreateEditor("ok");
        editor.CaretPosition = new DocumentPosition(0, 2);

        Assert.Throws<ArgumentException>(() =>
            editor.InsertText("\uD83D"));

        Assert.Equal("ok", GetText(editor));
        Assert.Equal(2, editor.CaretPosition.Offset);
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void PasteText_InvalidUtf16IsRejectedBeforeAnyMutation()
    {
        DocumentEditor editor = CreateEditor("ok");
        editor.CaretPosition = new DocumentPosition(0, 2);

        Assert.Throws<ArgumentException>(() =>
            editor.PasteText("valid\n\uDE00"));

        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("ok", GetText(editor));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void CaretAndSelectionSetters_NormalizeInteriorOffsets()
    {
        DocumentEditor editor = CreateEditor("😀X");

        editor.CaretPosition = new DocumentPosition(0, 1);
        editor.SelectionAnchor = new DocumentPosition(0, 1);

        Assert.Equal(2, editor.CaretPosition.Offset);
        Assert.Equal(2, editor.SelectionAnchor!.Value.Offset);
    }

    [Fact]
    public void ShiftNavigation_ExtendsSelectionByWholeTextElements()
    {
        DocumentEditor editor = CreateEditor("👍🏽X");

        editor.MoveRight(extendSelection: true);

        Assert.True(editor.HasSelection);
        Assert.Equal(new DocumentPosition(0, 0), editor.SelectionAnchor);
        Assert.Equal(new DocumentPosition(0, 4), editor.CaretPosition);
        Assert.Equal("👍🏽", editor.GetSelectedText());

        editor.MoveLeft(extendSelection: false);
        Assert.False(editor.HasSelection);
        Assert.Equal(new DocumentPosition(0, 0), editor.CaretPosition);
    }

    [Fact]
    public void DeleteSelection_RemovesWholeTextElementAndRestoresExactState()
    {
        const string textElement = "👨‍👩‍👧‍👦";
        DocumentEditor editor = CreateEditor($"A{textElement}B");
        editor.SelectionAnchor = new DocumentPosition(0, 1);
        editor.CaretPosition = new DocumentPosition(0, 1 + textElement.Length);

        editor.DeleteSelection();
        Assert.Equal("AB", GetText(editor));
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);

        editor.History.Undo();
        Assert.Equal($"A{textElement}B", GetText(editor));
        Assert.Equal(new DocumentPosition(0, 1), editor.SelectionAnchor);
        Assert.Equal(
            new DocumentPosition(0, 1 + textElement.Length),
            editor.CaretPosition);

        editor.History.Redo();
        Assert.Equal("AB", GetText(editor));
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);
    }

    [Fact]
    public void NavigationAndDelete_CrossParagraphBoundaries()
    {
        DocumentEditor editor = CreateEditor("A");
        editor.Document.Paragraphs.Add(new Paragraph
        {
            Runs = { new TextRun("😀B") }
        });
        editor.CaretPosition = new DocumentPosition(0, 1);

        editor.MoveRight();
        Assert.Equal(new DocumentPosition(1, 0), editor.CaretPosition);
        editor.MoveRight();
        Assert.Equal(new DocumentPosition(1, 2), editor.CaretPosition);
        editor.MoveLeft();
        Assert.Equal(new DocumentPosition(1, 0), editor.CaretPosition);
        editor.MoveLeft();
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);

        editor.DeleteForward();
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("A😀B", GetText(editor));
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);
    }

    [Fact]
    public void DeleteForward_AtParagraphEnd_MergeCanBeUndoneAndRedone()
    {
        DocumentEditor editor = CreateEditor("A");
        editor.Document.Paragraphs.Add(new Paragraph
        {
            Runs = { new TextRun("😀B") }
        });
        editor.CaretPosition = new DocumentPosition(0, 1);

        editor.DeleteForward();
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("A😀B", GetText(editor));
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);

        editor.History.Undo();
        Assert.Equal(2, editor.Document.Paragraphs.Count);
        Assert.Equal("A", editor.Document.Paragraphs[0].GetPlainText());
        Assert.Equal("😀B", editor.Document.Paragraphs[1].GetPlainText());
        Assert.Equal(new DocumentPosition(0, 1), editor.CaretPosition);

        editor.History.Redo();
        Assert.Single(editor.Document.Paragraphs);
        Assert.Equal("A😀B", GetText(editor));
    }

    private static DocumentEditor CreateEditor(string text) =>
        CreateEditorFromRuns(text);

    private static DocumentEditor CreateEditorFromRuns(params string[] runs)
    {
        GostDocument document = new();
        document.Paragraphs.Clear();
        Paragraph paragraph = new();
        foreach (string run in runs)
        {
            paragraph.Runs.Add(new TextRun(run));
        }
        document.Paragraphs.Add(paragraph);
        return new DocumentEditor(document, new ImageService());
    }

    private static string GetText(DocumentEditor editor) =>
        editor.Document.Paragraphs[0].GetPlainText();
}
