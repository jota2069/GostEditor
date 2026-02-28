using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.Services;

public class CodeParserService : ICodeParserService
{
    private static readonly Dictionary<string, string> ExtensionToLanguage =
        new Dictionary<string, string>()
        {
            [".cs"] = "csharp",
            [".py"] = "python",
            [".js"] = "javascript",
            [".ts"] = "typescript",
            [".axaml"] = "xml",
            [".xaml"] = "xml",
        };

    private static readonly HashSet<string> CSharpSkipPrefixes =
        new HashSet<string>()
        {
            "using ", "#nullable", "#pragma"
        };

    public async Task<IReadOnlyList<CodeListing>> ParseDirectoryAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Папка не найдена: {directoryPath}");
        }

        HashSet<string> supportedExtensions =
            new HashSet<string>(ExtensionToLanguage.Keys);

        List<string> files = Directory
            .EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(filePath => supportedExtensions.Contains(
                Path.GetExtension(filePath).ToLower()))
            .OrderBy(filePath => filePath)
            .ToList();

        List<CodeListing> listings = new List<CodeListing>();
        int order = 0;

        foreach (string file in files)
        {
            CodeListing listing = await Task.Run(() => ParseFile(file, directoryPath));
            listing.Order = order++;
            listings.Add(listing);
        }

        return listings;
    }

    public CodeListing ParseFile(string filePath)
    {
        return ParseFile(filePath, Path.GetDirectoryName(filePath) ?? string.Empty);
    }

    private CodeListing ParseFile(string filePath, string rootDirectory)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        string language = ExtensionToLanguage.GetValueOrDefault(extension, "text");
        string[] rawLines = File.ReadAllLines(filePath);
        IEnumerable<string> cleanedLines = CleanLines(rawLines, language);

        // Вычисляем относительный путь от корневой папки.
        string relativePath = Path.GetRelativePath(rootDirectory, filePath);

        return new CodeListing
        {
            FileName = Path.GetFileName(filePath),
            RelativePath = relativePath,
            Language = language,
            Content = string.Join(Environment.NewLine, cleanedLines),
            IsSelected = true
        };
    }

    private static IEnumerable<string> CleanLines(string[] lines, string language)
    {
        List<string> result = new List<string>();
        bool previousWasEmpty = false;

        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();

            if (language == "csharp"
                && CSharpSkipPrefixes.Any(prefix => trimmed.TrimStart().StartsWith(prefix)))
            {
                continue;
            }

            bool isEmpty = string.IsNullOrWhiteSpace(trimmed);

            if (isEmpty)
            {
                if (!previousWasEmpty && result.Count > 0)
                {
                    result.Add(string.Empty);
                }

                previousWasEmpty = true;
                continue;
            }

            result.Add(trimmed);
            previousWasEmpty = false;
        }

        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// Превращает список выбранных листингов кода в готовые абзацы для вставки в редактор.
    /// </summary>
    public List<GostEditor.Core.TextEngine.DOM.Paragraph> GenerateAppendixParagraphs(IEnumerable<CodeListing> listings)
    {
        // ЯВНАЯ ТИПИЗАЦИЯ
        List<GostEditor.Core.TextEngine.DOM.Paragraph> appendixParagraphs = new List<GostEditor.Core.TextEngine.DOM.Paragraph>();

        // Заголовок приложения (По центру)
        GostEditor.Core.TextEngine.DOM.Paragraph titlePara = new GostEditor.Core.TextEngine.DOM.Paragraph { Alignment = GostEditor.Core.TextEngine.DOM.GostAlignment.Center };
        titlePara.Runs.Add(new GostEditor.Core.TextEngine.DOM.TextRun("ПРИЛОЖЕНИЕ А", isBold: true, isItalic: false));
        appendixParagraphs.Add(titlePara);

        // Пустая строка после заголовка
        appendixParagraphs.Add(new GostEditor.Core.TextEngine.DOM.Paragraph());

        int listingCounter = 1;

        foreach (CodeListing listing in listings)
        {
            // Пропускаем файлы, с которых сняли галочку в UI
            if (!listing.IsSelected) continue;

            // Название листинга (Листинг 1. Файл Models/ProcessInfo.cs)
            GostEditor.Core.TextEngine.DOM.Paragraph fileTitlePara = new GostEditor.Core.TextEngine.DOM.Paragraph { Alignment = GostEditor.Core.TextEngine.DOM.GostAlignment.Left };
            fileTitlePara.Runs.Add(new GostEditor.Core.TextEngine.DOM.TextRun($"Листинг {listingCounter}. Файл {listing.RelativePath}", isBold: false, isItalic: false));
            appendixParagraphs.Add(fileTitlePara);

            // Разбиваем код на строки и добавляем
            string[] lines = listing.Content.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string cleanLine = line.Replace("\t", "    ");

                GostEditor.Core.TextEngine.DOM.Paragraph linePara = new GostEditor.Core.TextEngine.DOM.Paragraph { Alignment = GostEditor.Core.TextEngine.DOM.GostAlignment.Left };
                linePara.Runs.Add(new GostEditor.Core.TextEngine.DOM.TextRun(cleanLine, isBold: false, isItalic: false));
                appendixParagraphs.Add(linePara);
            }

            // Отступ между файлами
            appendixParagraphs.Add(new GostEditor.Core.TextEngine.DOM.Paragraph());
            listingCounter++;
        }

        return appendixParagraphs;
    }

}
