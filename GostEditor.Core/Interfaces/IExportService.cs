using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

public interface IExportService
{
    Task ExportToDocxAsync(GostDocument document, string outputPath);
}