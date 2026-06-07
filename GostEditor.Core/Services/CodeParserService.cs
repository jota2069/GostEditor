using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Services;

/// <summary>
/// Сервис для парсинга исходного кода проекта с очисткой и форматированием под ГОСТ
/// </summary>
public class CodeParserService : ICodeParserService
{
    private static readonly Dictionary<string, string> ExtensionToLanguage = new Dictionary<string, string>
    {
        [".cs"] = "csharp",
        [".py"] = "python",
        [".js"] = "javascript",
        [".ts"] = "typescript",
        [".axaml"] = "xml",
        [".xaml"] = "xml",
        [".cpp"] = "cpp",
        [".c"] = "c",
        [".h"] = "c",
        [".hpp"] = "cpp",
        [".java"] = "java",
        [".html"] = "html",
        [".css"] = "css",
        [".xml"] = "xml",
        [".json"] = "json",
        [".sql"] = "sql"
    };

    private static readonly HashSet<string> CSharpSkipPrefixes = new HashSet<string>
    {
        "using ", "#nullable", "#pragma"
    };

    private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".vscode",
        "packages", "Debug", "Release", "dist", "build"
    };

    /// <summary>
    /// Парсит директорию и возвращает список листингов кода
    /// </summary>
    public async Task<IReadOnlyList<CodeListing>> ParseDirectoryAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Папка не найдена: {directoryPath}");
        }

        Debug.WriteLine($"[CODE PARSER] Начало парсинга: {directoryPath}");

        HashSet<string> supportedExtensions = new HashSet<string>(ExtensionToLanguage.Keys);

        List<string> files = Directory
            .EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(filePath =>
            {
                // Проверяем расширение
                if (!supportedExtensions.Contains(Path.GetExtension(filePath).ToLower()))
                {
                    return false;
                }

                // Исключаем файлы из запрещённых папок
                string relativePath = Path.GetRelativePath(directoryPath, filePath);
                string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);

                foreach (string part in pathParts)
                {
                    if (ExcludedDirectories.Contains(part.ToLower()))
                    {
                        return false;
                    }
                }

                return true;
            })
            .OrderBy(filePath => filePath)
            .ToList();

        List<CodeListing> listings = new List<CodeListing>();
        int order = 0;

        foreach (string file in files)
        {
            CodeListing listing = await Task.Run(() => ParseFile(file, directoryPath));
            listing.Order = order;
            listing.ListingNumber = order + 1;
            order++;
            listings.Add(listing);

            Debug.WriteLine($"[CODE PARSER] Добавлен файл: {listing.RelativePath} ({listing.Language})");
        }

        Debug.WriteLine($"[CODE PARSER] Найдено файлов: {listings.Count}");

        return listings;
    }

    /// <summary>
    /// Парсит один файл (публичный метод для интерфейса)
    /// </summary>
    public CodeListing ParseFile(string filePath)
    {
        return ParseFile(filePath, Path.GetDirectoryName(filePath) ?? string.Empty);
    }

    /// <summary>
    /// Парсит один файл с учётом корневой директории
    /// </summary>
    private CodeListing ParseFile(string filePath, string rootDirectory)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        string language = ExtensionToLanguage.GetValueOrDefault(extension, "text");
        string[] rawLines = File.ReadAllLines(filePath);
        IEnumerable<string> cleanedLines = CleanLines(rawLines, language);

        // Вычисляем относительный путь от корневой папки
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

    /// <summary>
    /// Очищает строки кода от лишних элементов (usings, pragma, пустые строки)
    /// </summary>
    private static IEnumerable<string> CleanLines(string[] lines, string language)
    {
        List<string> result = new List<string>();
        bool previousWasEmpty = false;

        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();

            // Для C# файлов убираем using, #nullable, #pragma
            if (language == "csharp" &&
                CSharpSkipPrefixes.Any(prefix => trimmed.TrimStart().StartsWith(prefix)))
            {
                continue;
            }

            bool isEmpty = string.IsNullOrWhiteSpace(trimmed);

            if (isEmpty)
            {
                // Оставляем максимум одну пустую строку подряд
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

        // Убираем пустые строки в конце
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    /// Генерирует параграфы для приложения с листингами кода
    /// Формат: "Листинг N — файл путь/к/файлу.cs"
    /// </summary>
    public List<Paragraph> GenerateAppendixParagraphs(IEnumerable<CodeListing> listings)
    {
        List<Paragraph> appendixParagraphs = new List<Paragraph>();

        // Заголовок приложения (по центру, жирный, 14pt)
        Paragraph titlePara = new Paragraph
        {
            Alignment = GostAlignment.Center,
            FirstLineIndent = 0
        };
        titlePara.Runs.Add(new TextRun("ПРИЛОЖЕНИЕ А", isBold: true, isItalic: false) { FontSize = 14 });
        appendixParagraphs.Add(titlePara);

        // Пустая строка после заголовка
        appendixParagraphs.Add(new Paragraph { FirstLineIndent = 0 });

        int listingCounter = 1;

        foreach (CodeListing listing in listings)
        {
            // Пропускаем файлы, с которых сняли галочку в UI
            if (!listing.IsSelected)
            {
                continue;
            }

            // Заголовок листинга: "Листинг N — файл путь/к/файлу.cs" (14pt, СПРАВА)
            Paragraph fileTitlePara = new Paragraph
            {
                Alignment = GostAlignment.Right,
                FirstLineIndent = 0
            };
            fileTitlePara.Runs.Add(new TextRun(
                $"Листинг {listingCounter} - файл {listing.RelativePath}",
                isBold: false,
                isItalic: false) { FontSize = 14 });
            appendixParagraphs.Add(fileTitlePara);

            // Разбиваем код на строки
            string[] lines = listing.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string line in lines)
            {
                // Заменяем табы на пробелы
                string cleanLine = line.Replace("\t", "    ");

                // Если строка пустая, вставляем неразрывный пробел
                if (string.IsNullOrWhiteSpace(cleanLine))
                {
                    cleanLine = " ";
                }

                // Строка кода: 12pt, СЛЕВА, без отступа
                Paragraph linePara = new Paragraph
                {
                    Alignment = GostAlignment.Left,
                    FirstLineIndent = 0,
                    Style = ParagraphStyle.Code
                };
                linePara.Runs.Add(new TextRun(cleanLine, isBold: false, isItalic: false) { FontSize = 12 });
                appendixParagraphs.Add(linePara);
            }

            // Пустая строка между листингами
            appendixParagraphs.Add(new Paragraph { FirstLineIndent = 0 });

            listingCounter++;
        }

        Debug.WriteLine($"[CODE PARSER] Сгенерировано параграфов приложения: {appendixParagraphs.Count}");

        return appendixParagraphs;
    }
}
