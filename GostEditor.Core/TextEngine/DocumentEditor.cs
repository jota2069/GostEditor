using GostEditor.Core.TextEngine.DOM;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GostEditor.Core.TextEngine;

public class DocumentEditor
{
    public GostDocument Document { get; }

    // Позиция мигающего курсора
    public DocumentPosition CaretPosition { get; set; }

    // Точка, где мы нажали мышку (начало выделения)
    public DocumentPosition? SelectionAnchor { get; set; }

    // Проверка, выделено ли что-то в данный момент
    public bool HasSelection => SelectionAnchor.HasValue && SelectionAnchor.Value.CompareTo(CaretPosition) != 0;

    public DocumentEditor(GostDocument document)
    {
        Document = document;
        CaretPosition = new DocumentPosition(0, 0);
    }

    /// <summary>
    /// Возвращает упорядоченные границы выделения (от меньшего к большему).
    /// </summary>
    public (DocumentPosition Start, DocumentPosition End) GetNormalizedSelection()
    {
        if (!HasSelection) return (CaretPosition, CaretPosition);

        return SelectionAnchor!.Value.CompareTo(CaretPosition) < 0
            ? (SelectionAnchor.Value, CaretPosition)
            : (CaretPosition, SelectionAnchor.Value);
    }

    /// <summary>
    /// Сбрасывает выделение, оставляя только курсор.
    /// </summary>
    public void ClearSelection()
    {
        SelectionAnchor = null;
    }

    /// <summary>
    /// Вставляет текст в текущую позицию.
    /// </summary>
    public void InsertText(string text)
    {
        // Если есть выделение, сначала удаляем его (реализуем чуть позже)
        if (HasSelection)
        {
            // DeleteSelection();
        }

        ClearSelection();

        Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];
        int currentOffset = 0;
        TextRun? targetRun = null;

        foreach (var run in currentParagraph.Runs)
        {
            if (CaretPosition.Offset <= currentOffset + run.Text.Length)
            {
                targetRun = run;
                break;
            }
            currentOffset += run.Text.Length;
        }

        if (targetRun != null)
        {
            int insertIndexInRun = CaretPosition.Offset - currentOffset;
            targetRun.Text = targetRun.Text.Insert(insertIndexInRun, text);
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset + text.Length);
        }
    }

    /// <summary>
    /// Перенос строки (Enter).
    /// </summary>
    public void InsertNewLine()
    {
        ClearSelection();

        Paragraph currentParagraph = Document.Paragraphs[CaretPosition.ParagraphIndex];
        string fullText = currentParagraph.GetPlainText();

        string firstPart = fullText.Substring(0, CaretPosition.Offset);
        string secondPart = fullText.Substring(CaretPosition.Offset);

        currentParagraph.Runs.Clear();
        currentParagraph.Runs.Add(new TextRun { Text = firstPart });

        Paragraph newParagraph = new Paragraph();
        newParagraph.Runs.Add(new TextRun { Text = secondPart });

        Document.Paragraphs.Insert(CaretPosition.ParagraphIndex + 1, newParagraph);
        CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
    }

    /// <summary>
    /// Удаление символа (Backspace).
    /// </summary>
    public void Backspace()
    {
        if (HasSelection)
        {
            // DeleteSelection();
            return;
        }

        if (CaretPosition.Offset == 0)
        {
            if (CaretPosition.ParagraphIndex > 0)
            {
                int prevIndex = CaretPosition.ParagraphIndex - 1;
                Paragraph prevP = Document.Paragraphs[prevIndex];
                Paragraph currP = Document.Paragraphs[CaretPosition.ParagraphIndex];

                int newOffset = prevP.GetPlainText().Length;
                prevP.Runs.AddRange(currP.Runs);
                Document.Paragraphs.RemoveAt(CaretPosition.ParagraphIndex);

                CaretPosition = new DocumentPosition(prevIndex, newOffset);
            }
            return;
        }

        Paragraph p = Document.Paragraphs[CaretPosition.ParagraphIndex];
        int currentOffset = 0;
        foreach (var run in p.Runs)
        {
            if (CaretPosition.Offset <= currentOffset + run.Text.Length)
            {
                int deleteIdx = CaretPosition.Offset - currentOffset - 1;
                if (deleteIdx >= 0)
                {
                    run.Text = run.Text.Remove(deleteIdx, 1);
                    CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset - 1);
                }
                break;
            }
            currentOffset += run.Text.Length;
        }
    }

    public void MoveLeft()
    {
        ClearSelection();
        if (CaretPosition.Offset > 0)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset - 1);
        }
        else if (CaretPosition.ParagraphIndex > 0)
        {
            int prevIdx = CaretPosition.ParagraphIndex - 1;
            CaretPosition = new DocumentPosition(prevIdx, Document.Paragraphs[prevIdx].GetPlainText().Length);
        }
    }

    public void MoveRight()
    {
        ClearSelection();
        int currentLength = Document.Paragraphs[CaretPosition.ParagraphIndex].GetPlainText().Length;
        if (CaretPosition.Offset < currentLength)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex, CaretPosition.Offset + 1);
        }
        else if (CaretPosition.ParagraphIndex < Document.Paragraphs.Count - 1)
        {
            CaretPosition = new DocumentPosition(CaretPosition.ParagraphIndex + 1, 0);
        }
    }
}
