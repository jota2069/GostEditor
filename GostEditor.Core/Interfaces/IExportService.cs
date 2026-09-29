using GostEditor.Core.Models;
using GostEditor.Core.Serialization;

namespace GostEditor.Core.Interfaces;

public interface IExportService
{
    Task ExportToDocxAsync(
        GostDocument document,
        string outputPath,
        CancellationToken cancellationToken = default);

    Task ExportToDocxAsync(
        DocumentPersistenceSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken = default);
}
