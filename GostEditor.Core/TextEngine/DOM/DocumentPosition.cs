using System;

namespace GostEditor.Core.TextEngine.DOM;

public struct DocumentPosition : IComparable<DocumentPosition>
{
    public int ParagraphIndex { get; set; }
    public int Offset { get; set; }

    public DocumentPosition(int paragraphIndex, int offset)
    {
        ParagraphIndex = paragraphIndex;
        Offset = offset;
    }

    // Учим структуру понимать, кто из них левее в тексте, а кто правее
    public int CompareTo(DocumentPosition other)
    {
        if (ParagraphIndex != other.ParagraphIndex)
            return ParagraphIndex.CompareTo(other.ParagraphIndex);

        return Offset.CompareTo(other.Offset);
    }
}
