using System.Text.RegularExpressions;
using GostEditor.Core.Interfaces;

namespace GostEditor.Core.Services;

public class TextNormalizerService : ITextNormalizerService
{
    private static readonly Regex SoftHyphenPattern =
        new Regex(@"\u00AD", RegexOptions.Compiled);

    private static readonly Regex NonBreakingSpacePattern =
        new Regex(@"\u00A0", RegexOptions.Compiled);

    private static readonly Regex ZeroWidthPattern =
        new Regex(@"[\u200B\u200C\u200D\uFEFF]", RegexOptions.Compiled);

    private static readonly Regex MultipleSpacesPattern =
        new Regex(@" {2,}", RegexOptions.Compiled);

    private static readonly Regex MultipleNewlinesPattern =
        new Regex(@"\n{3,}", RegexOptions.Compiled);

    public string Normalize(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        string result = rawText;

        // Убираем мусорные символы.
        result = SoftHyphenPattern.Replace(result, string.Empty);
        result = ZeroWidthPattern.Replace(result, string.Empty);

        // Заменяем неразрывные пробелы на обычные.
        result = NonBreakingSpacePattern.Replace(result, " ");

        // Нормализуем переносы строк (Windows \r\n -> \n).
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // Убираем задвоенные пробелы.
        result = MultipleSpacesPattern.Replace(result, " ");

        // Не более двух переносов подряд.
        result = MultipleNewlinesPattern.Replace(result, "\n\n");

        // Trim каждой строки.
        string[] lines = result.Split('\n');
        string[] trimmedLines = Array.ConvertAll(lines, line => line.Trim());

        return string.Join("\n", trimmedLines).Trim();
    }
}