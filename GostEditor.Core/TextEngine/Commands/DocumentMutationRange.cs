namespace GostEditor.Core.TextEngine.Commands;

public readonly record struct DocumentMutationRange(int Start, int Count)
{
    public static DocumentMutationRange EmptyAt(int start) =>
        new(Math.Max(0, start), 0);

    public DocumentMutationRange Normalize(int paragraphCount)
    {
        int safeStart = Math.Clamp(Start, 0, Math.Max(0, paragraphCount));
        int safeCount = Math.Clamp(
            Count,
            0,
            Math.Max(0, paragraphCount - safeStart));
        return new DocumentMutationRange(safeStart, safeCount);
    }
}
