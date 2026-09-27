using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;

namespace GostEditor.Core.Editing;

internal static class ParagraphTextBuffer
{
    public static TextStyle GetStyleAt(ParagraphBlock paragraph, int offset)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        int safeOffset = Math.Clamp(offset, 0, paragraph.TextLength);
        int current = 0;
        TextStyle? lastStyle = null;

        foreach (InlineNode inline in paragraph.Inlines)
        {
            TextStyle? style = GetStyle(inline);
            if (style is not null)
            {
                lastStyle = style;
            }

            int end = current + inline.TextLength;
            if (safeOffset <= end && style is not null)
            {
                return style.Clone();
            }

            current = end;
        }

        return lastStyle?.Clone() ?? new TextStyle();
    }

    public static List<InlineNode> CloneInlines(
        IEnumerable<InlineNode> inlines) =>
        inlines.Select(CloneInline).ToList();

    public static InlineNode CloneInline(InlineNode inline) => inline switch
    {
        TextInline text => new TextInline(
            text.Id,
            text.Text,
            text.Style.Clone()),
        LinkInline link => new LinkInline(link.Id)
        {
            Text = link.Text,
            Target = link.Target,
            Style = link.Style.Clone()
        },
        LineBreakInline lineBreak => new LineBreakInline(lineBreak.Id),
        _ => throw new NotSupportedException(
            $"Не поддерживается клонирование inline-узла " +
            $"{inline.GetType().Name}.")
    };

    public static List<InlineNode> Slice(
        ParagraphBlock paragraph,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        int start = Math.Clamp(offset, 0, paragraph.TextLength);
        int end = Math.Clamp(start + Math.Max(0, length), start, paragraph.TextLength);
        List<InlineNode> result = new();
        if (start == end)
        {
            return result;
        }

        int current = 0;
        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineStart = current;
            int inlineEnd = current + inline.TextLength;
            current = inlineEnd;

            if (inlineEnd <= start || inlineStart >= end)
            {
                continue;
            }

            int localStart = Math.Clamp(start - inlineStart, 0, inline.TextLength);
            int localEnd = Math.Clamp(end - inlineStart, 0, inline.TextLength);
            AddInlineSlice(result, inline, localStart, localEnd);
        }

        return result;
    }

    public static void Restore(
        ParagraphBlock paragraph,
        IReadOnlyList<InlineNode> snapshot)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(snapshot);

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(CloneInlines(snapshot));
        paragraph.EnsureEditableInline();
    }

    public static void Insert(
        ParagraphBlock paragraph,
        int offset,
        string text,
        TextStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int safeOffset = Math.Clamp(offset, 0, paragraph.TextLength);
        style ??= GetStyleAt(paragraph, safeOffset);

        List<InlineNode> rebuilt = new();
        int current = 0;
        bool inserted = false;

        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineStart = current;
            int inlineEnd = current + inline.TextLength;
            current = inlineEnd;

            if (!inserted && safeOffset >= inlineStart && safeOffset <= inlineEnd)
            {
                int localOffset = Math.Clamp(
                    safeOffset - inlineStart,
                    0,
                    inline.TextLength);

                AddInlineSlice(rebuilt, inline, 0, localOffset);
                rebuilt.Add(new TextInline(text)
                {
                    Style = style.Clone()
                });
                AddInlineSlice(
                    rebuilt,
                    inline,
                    localOffset,
                    inline.TextLength);
                inserted = true;
            }
            else
            {
                rebuilt.Add(CloneInline(inline));
            }
        }

        if (!inserted)
        {
            rebuilt.Add(new TextInline(text)
            {
                Style = style.Clone()
            });
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(rebuilt);
        Normalize(paragraph);
    }

    public static void Delete(
        ParagraphBlock paragraph,
        int offset,
        int length)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (length <= 0 || paragraph.TextLength == 0)
        {
            return;
        }

        int start = Math.Clamp(offset, 0, paragraph.TextLength);
        int end = Math.Clamp(start + length, start, paragraph.TextLength);
        if (start == end)
        {
            return;
        }

        List<InlineNode> rebuilt = new();
        int current = 0;

        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineStart = current;
            int inlineEnd = current + inline.TextLength;
            current = inlineEnd;

            if (inlineEnd <= start || inlineStart >= end)
            {
                rebuilt.Add(CloneInline(inline));
                continue;
            }

            int localStart = Math.Clamp(start - inlineStart, 0, inline.TextLength);
            int localEnd = Math.Clamp(end - inlineStart, 0, inline.TextLength);
            AddInlineSlice(rebuilt, inline, 0, localStart);
            AddInlineSlice(rebuilt, inline, localEnd, inline.TextLength);
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(rebuilt);
        Normalize(paragraph);
    }

    public static void ApplyStyle(
        ParagraphBlock paragraph,
        int offset,
        int length,
        Action<TextStyle> change)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(change);

        int start = Math.Clamp(offset, 0, paragraph.TextLength);
        int end = Math.Clamp(start + length, start, paragraph.TextLength);
        if (start == end)
        {
            return;
        }

        List<InlineNode> rebuilt = new();
        int current = 0;

        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineStart = current;
            int inlineEnd = current + inline.TextLength;
            current = inlineEnd;

            if (inlineEnd <= start || inlineStart >= end)
            {
                rebuilt.Add(CloneInline(inline));
                continue;
            }

            int localStart = Math.Clamp(start - inlineStart, 0, inline.TextLength);
            int localEnd = Math.Clamp(end - inlineStart, 0, inline.TextLength);
            AddInlineSlice(rebuilt, inline, 0, localStart);
            AddStyledInlineSlice(
                rebuilt,
                inline,
                localStart,
                localEnd,
                change);
            AddInlineSlice(rebuilt, inline, localEnd, inline.TextLength);
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(rebuilt);
        Normalize(paragraph);
    }

    public static void ReplaceAll(
        ParagraphBlock paragraph,
        IEnumerable<InlineNode> inlines)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(inlines);

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(CloneInlines(inlines));
        Normalize(paragraph);
    }

    private static TextStyle? GetStyle(InlineNode inline) => inline switch
    {
        TextInline text => text.Style,
        LinkInline link => link.Style,
        _ => null
    };

    private static void AddStyledInlineSlice(
        ICollection<InlineNode> target,
        InlineNode inline,
        int start,
        int end,
        Action<TextStyle> change)
    {
        if (start >= end)
        {
            return;
        }

        switch (inline)
        {
            case TextInline text:
            {
                TextStyle style = text.Style.Clone();
                change(style);
                target.Add(new TextInline(text.Text[start..end])
                {
                    Style = style
                });
                break;
            }

            case LinkInline link:
            {
                TextStyle style = link.Style.Clone();
                change(style);
                target.Add(new LinkInline
                {
                    Text = link.Text[start..end],
                    Target = link.Target,
                    Style = style
                });
                break;
            }

            case LineBreakInline when start == 0 && end == 1:
                target.Add(CloneInline(inline));
                break;
        }
    }

    private static void AddInlineSlice(
        ICollection<InlineNode> target,
        InlineNode inline,
        int start,
        int end)
    {
        if (start >= end)
        {
            return;
        }

        switch (inline)
        {
            case TextInline text:
                target.Add(new TextInline(text.Text[start..end])
                {
                    Style = text.Style.Clone()
                });
                break;

            case LinkInline link:
                target.Add(new LinkInline
                {
                    Text = link.Text[start..end],
                    Target = link.Target,
                    Style = link.Style.Clone()
                });
                break;

            case LineBreakInline when start == 0 && end == 1:
                target.Add(new LineBreakInline());
                break;
        }
    }

    private static void Normalize(ParagraphBlock paragraph)
    {
        List<InlineNode> normalized = new();

        foreach (InlineNode inline in paragraph.Inlines)
        {
            if (inline.TextLength == 0)
            {
                continue;
            }

            if (inline is TextInline currentText &&
                normalized.LastOrDefault() is TextInline previousText &&
                previousText.Style.Equals(currentText.Style))
            {
                previousText.Text += currentText.Text;
                continue;
            }

            if (inline is LinkInline currentLink &&
                normalized.LastOrDefault() is LinkInline previousLink &&
                previousLink.Style.Equals(currentLink.Style) &&
                string.Equals(
                    previousLink.Target,
                    currentLink.Target,
                    StringComparison.Ordinal))
            {
                previousLink.Text += currentLink.Text;
                continue;
            }

            normalized.Add(CloneInline(inline));
        }

        paragraph.Inlines.Clear();
        paragraph.Inlines.AddRange(normalized);
        paragraph.EnsureEditableInline();
    }
}
