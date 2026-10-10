using System;
using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.Commands;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine;

public partial class DocumentEditor
{
    public void ScrollToParagraph(int index)
    {
        if (index >= 0 && index < Document.Paragraphs.Count)
        {
            ClearSelection();
            CaretPosition = new DocumentPosition(index, 0);
        }
    }

    public bool FindNext(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText) || Document.Paragraphs.Count == 0)
        {
            return false;
        }

        int startParagraph = Math.Clamp(CaretPosition.ParagraphIndex, 0, Document.Paragraphs.Count - 1);
        int startOffset = Math.Clamp(CaretPosition.Offset, 0, Document.Paragraphs[startParagraph].GetPlainText().Length);

        if (HasSelection)
        {
            (_, DocumentPosition end) = GetNormalizedSelection();
            startParagraph = end.ParagraphIndex;
            startOffset = end.Offset;
        }

        for (int pass = 0; pass < Document.Paragraphs.Count; pass++)
        {
            int pIdx = (startParagraph + pass) % Document.Paragraphs.Count;
            string text = Document.Paragraphs[pIdx].GetPlainText();
            int searchStart = pIdx == startParagraph ? Math.Min(startOffset, text.Length) : 0;
            int index = text.IndexOf(searchText, searchStart, StringComparison.CurrentCultureIgnoreCase);

            if (index >= 0)
            {
                SelectionAnchor = new DocumentPosition(pIdx, index);
                CaretPosition = new DocumentPosition(pIdx, index + searchText.Length);
                SelectedImageParagraphIndex = null;
                return true;
            }
        }

        string startText = Document.Paragraphs[startParagraph].GetPlainText();
        int wrapIndex = startText.IndexOf(searchText, 0, StringComparison.CurrentCultureIgnoreCase);
        if (wrapIndex >= 0 && wrapIndex <= startOffset)
        {
            SelectionAnchor = new DocumentPosition(startParagraph, wrapIndex);
            CaretPosition = new DocumentPosition(startParagraph, wrapIndex + searchText.Length);
            SelectedImageParagraphIndex = null;
            return true;
        }

        return false;
    }

    public void SelectAll()
    {
        int paragraphCount = Document.Paragraphs.Count;
        if (paragraphCount == 0) return;

        SelectionAnchor = new DocumentPosition(0, 0);

        int lastParagraphIndex = paragraphCount - 1;
        Paragraph lastParagraph = Document.Paragraphs[lastParagraphIndex];
        int lastOffset = lastParagraph.GetPlainText().Length;

        CaretPosition = new DocumentPosition(lastParagraphIndex, lastOffset);
    }

    public (DocumentPosition Start, DocumentPosition End) GetNormalizedSelection()
    {
        if (!HasSelection) return (CaretPosition, CaretPosition);

        return SelectionAnchor!.Value.CompareTo(CaretPosition) < 0
            ? (SelectionAnchor.Value, CaretPosition)
            : (CaretPosition, SelectionAnchor.Value);
    }

    public void ClearSelection()
    {
        SelectionAnchor = null;
        SelectedImageParagraphIndex = null;
    }

    public void MoveLeft() => MoveLeft(extendSelection: false);

    public void MoveLeft(bool extendSelection)
    {
        if (!extendSelection && HasSelection)
        {
            (DocumentPosition start, _) = GetNormalizedSelection();
            MoveCaret(start, extendSelection: false);
            return;
        }

        DocumentPosition destination = CaretPosition;
        if (CaretPosition.Offset > 0)
        {
            string text = Document.Paragraphs[
                CaretPosition.ParagraphIndex].GetPlainText();
            destination = new DocumentPosition(
                CaretPosition.ParagraphIndex,
                TextBoundaries.Previous(text, CaretPosition.Offset));
        }
        else if (CaretPosition.ParagraphIndex > 0)
        {
            int prevIdx = CaretPosition.ParagraphIndex - 1;
            destination = new DocumentPosition(
                prevIdx,
                Document.Paragraphs[prevIdx].GetPlainText().Length);
        }

        MoveCaret(destination, extendSelection);
    }

    public void MoveRight() => MoveRight(extendSelection: false);

    public void MoveRight(bool extendSelection)
    {
        if (!extendSelection && HasSelection)
        {
            (_, DocumentPosition end) = GetNormalizedSelection();
            MoveCaret(end, extendSelection: false);
            return;
        }

        DocumentPosition destination = CaretPosition;
        int currentLength = Document.Paragraphs[CaretPosition.ParagraphIndex].GetPlainText().Length;
        if (CaretPosition.Offset < currentLength)
        {
            string text = Document.Paragraphs[
                CaretPosition.ParagraphIndex].GetPlainText();
            destination = new DocumentPosition(
                CaretPosition.ParagraphIndex,
                TextBoundaries.Next(text, CaretPosition.Offset));
        }
        else if (CaretPosition.ParagraphIndex < Document.Paragraphs.Count - 1)
        {
            destination = new DocumentPosition(
                CaretPosition.ParagraphIndex + 1,
                0);
        }

        MoveCaret(destination, extendSelection);
    }

    public void MoveCaret(
        DocumentPosition destination,
        bool extendSelection)
    {
        DocumentPosition? anchor = extendSelection
            ? SelectionAnchor ?? CaretPosition
            : null;
        CaretPosition = destination;
        SelectionAnchor = anchor;
        SelectedImageParagraphIndex = null;
    }

    public string GetSelectedText()
    {
        if (!HasSelection) return string.Empty;

        (DocumentPosition start, DocumentPosition end) = GetNormalizedSelection();
        System.Text.StringBuilder resultBuilder = new System.Text.StringBuilder();

        for (int pIdx = start.ParagraphIndex; pIdx <= end.ParagraphIndex; pIdx++)
        {
            Paragraph currentParagraph = Document.Paragraphs[pIdx];
            string plainText = currentParagraph.GetPlainText();

            int startIndex = (pIdx == start.ParagraphIndex) ? start.Offset : 0;
            int endIndex = (pIdx == end.ParagraphIndex) ? end.Offset : plainText.Length;

            resultBuilder.Append(plainText.Substring(startIndex, endIndex - startIndex));
            if (pIdx < end.ParagraphIndex) resultBuilder.AppendLine();
        }

        return resultBuilder.ToString();
    }
}
