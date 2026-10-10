using System.Globalization;

namespace GostEditor.Core.TextEngine;

/// <summary>
/// Defines the editor boundary contract. Positions remain UTF-16 offsets, but
/// interactive caret, selection, and deletion endpoints must be extended
/// text-element boundaries returned by <see cref="StringInfo"/>.
/// </summary>
public sealed class TextBoundaryService
{
    public static TextBoundaryService Default { get; } = new();

    public IReadOnlyList<int> GetBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int[] boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = text.Length;
        return boundaries;
    }

    public bool IsBoundary(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset < 0 || offset > text.Length)
        {
            return false;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        return offset == text.Length || Array.BinarySearch(starts, offset) >= 0;
    }

    public int Normalize(
        string text,
        int offset,
        TextBoundaryAffinity affinity = TextBoundaryAffinity.Nearest)
    {
        ArgumentNullException.ThrowIfNull(text);
        int clamped = Math.Clamp(offset, 0, text.Length);
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        if (clamped == text.Length || Array.BinarySearch(starts, clamped) >= 0)
        {
            return clamped;
        }

        int nextIndex = ~Array.BinarySearch(starts, clamped);
        int previous = nextIndex == 0 ? 0 : starts[nextIndex - 1];
        int next = nextIndex >= starts.Length ? text.Length : starts[nextIndex];
        return affinity switch
        {
            TextBoundaryAffinity.Backward => previous,
            TextBoundaryAffinity.Forward => next,
            _ => clamped - previous < next - clamped ? previous : next
        };
    }

    public int Previous(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        int clamped = Math.Clamp(offset, 0, text.Length);
        if (clamped == 0)
        {
            return 0;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int index = Array.BinarySearch(starts, clamped);
        if (index >= 0)
        {
            return index == 0 ? 0 : starts[index - 1];
        }

        int insertionIndex = ~index;
        return insertionIndex == 0 ? 0 : starts[insertionIndex - 1];
    }

    public int Next(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        int clamped = Math.Clamp(offset, 0, text.Length);
        if (clamped == text.Length)
        {
            return text.Length;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int index = Array.BinarySearch(starts, clamped);
        int nextIndex = index >= 0 ? index + 1 : ~index;
        return nextIndex >= starts.Length ? text.Length : starts[nextIndex];
    }

    public bool IsValidUtf16(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (char.IsHighSurrogate(current))
            {
                if (index + 1 >= text.Length ||
                    !char.IsLowSurrogate(text[index + 1]))
                {
                    return false;
                }

                index++;
            }
            else if (char.IsLowSurrogate(current))
            {
                return false;
            }
        }

        return true;
    }

    public void EnsureValidUtf16(string text, string? parameterName = null)
    {
        if (!IsValidUtf16(text))
        {
            throw new ArgumentException(
                "Текст содержит непарный UTF-16 surrogate.",
                parameterName);
        }
    }
}

public enum TextBoundaryAffinity
{
    Backward,
    Forward,
    Nearest
}
