using System.Collections.Generic;
using System.Threading.Tasks;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Interfaces;

public interface ICodeParserService
{
    Task<IReadOnlyList<CodeListing>> ParseDirectoryAsync(string directoryPath);
    CodeListing ParseFile(string filePath);
    List<Paragraph> GenerateAppendixParagraphs(IEnumerable<CodeListing> listings);
}
