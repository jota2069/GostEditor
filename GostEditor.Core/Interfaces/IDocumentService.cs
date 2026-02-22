using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

public interface IDocumentService
{
    GostDocument CreateNew();
    Task<GostDocument> LoadAsync(string filePath);
    Task SaveAsync(GostDocument document, string filePath);
}