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

    // Запоминаем старые стили И старые выравнивания
    private readonly List<ParagraphStyle> _oldStyles = [];
    private readonly List<GostAlignment> _oldAlignments = [];

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

        for (int i = _startIndex; i <= _endIndex; i++)
        {
            Paragraph p = _editor.Document.Paragraphs[i];

            // Сохраняем историю для Ctrl+Z
            _oldStyles.Add(p.Style);
            _oldAlignments.Add(p.Alignment);

            // Применяем новый стиль
            p.Style = _newStyle;

            // Настраиваем выравнивание по ГОСТу
            if (_newStyle == ParagraphStyle.Heading1 || _newStyle == ParagraphStyle.Heading2)
            {
                p.Alignment = GostAlignment.Center;
            }
            else if (_newStyle == ParagraphStyle.Normal)
            {
                p.Alignment = GostAlignment.Justify;
            }
            else if (_newStyle == ParagraphStyle.Code)
            {
                p.Alignment = GostAlignment.Left;
            }
        }
    }

    public void Undo()
    {
        for (int i = _startIndex; i <= _endIndex; i++)
        {
            Paragraph p = _editor.Document.Paragraphs[i];

            // Возвращаем как было
            p.Style = _oldStyles[i - _startIndex];
            p.Alignment = _oldAlignments[i - _startIndex];
        }
    }
}
