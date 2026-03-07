using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Commands;

public class ChangeParagraphStyleCommand : IEditorCommand
{
    private readonly DocumentEditor _editor;
    private readonly ParagraphStyle _newStyle;
    private readonly int _startIndex;
    private readonly int _endIndex;

    private readonly List<ParagraphStyle> _oldStyles = [];
    private readonly List<GostAlignment> _oldAlignments = [];
    private readonly List<bool> _oldPageBreaks = [];

    public ChangeParagraphStyleCommand(DocumentEditor editor, ParagraphStyle newStyle, int startIndex, int endIndex)
    {
        _editor = editor;
        _newStyle = newStyle;
        _startIndex = startIndex;
        _endIndex = endIndex;
    }

    public void Execute()
    {
        _oldStyles.Clear();
        _oldAlignments.Clear();
        _oldPageBreaks.Clear();

        for (int i = _startIndex; i <= _endIndex; i++)
        {
            Paragraph p = _editor.Document.Paragraphs[i];

            _oldStyles.Add(p.Style);
            _oldAlignments.Add(p.Alignment);
            _oldPageBreaks.Add(p.PageBreakBefore);

            p.Style = _newStyle;

            if (_newStyle == ParagraphStyle.Heading1)
            {
                p.Alignment = GostAlignment.Center;
                p.FirstLineIndent = 0;
                p.PageBreakBefore = true; // Заголовок 1 уровня всегда с новой страницы!
            }
            else if (_newStyle == ParagraphStyle.Heading2)
            {
                p.Alignment = GostAlignment.Center;
                p.PageBreakBefore = false;
            }
            else if (_newStyle == ParagraphStyle.Normal)
            {
                p.Alignment = GostAlignment.Justify;
                p.PageBreakBefore = false;
            }
            else if (_newStyle == ParagraphStyle.Code)
            {
                p.Alignment = GostAlignment.Left;
                p.PageBreakBefore = false;
            }
        }
    }

    public void Undo()
    {
        for (int i = _startIndex; i <= _endIndex; i++)
        {
            Paragraph p = _editor.Document.Paragraphs[i];

            p.Style = _oldStyles[i - _startIndex];
            p.Alignment = _oldAlignments[i - _startIndex];
            p.PageBreakBefore = _oldPageBreaks[i - _startIndex];
        }
    }
}
