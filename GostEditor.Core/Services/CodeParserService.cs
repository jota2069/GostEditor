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
}
