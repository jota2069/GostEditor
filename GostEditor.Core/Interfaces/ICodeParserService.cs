using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

public interface ICodeParserService
{
    Task<IReadOnlyList<CodeListing>> ParseDirectoryAsync(string directoryPath);
    CodeListing ParseFile(string filePath);
}