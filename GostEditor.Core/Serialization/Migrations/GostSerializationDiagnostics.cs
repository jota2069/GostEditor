using System.Diagnostics;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostSerializationDiagnostics
{
    private readonly List<GostArchiveDiagnostic> _items = new();

    public IReadOnlyList<GostArchiveDiagnostic> Items => _items;

    public void Info(string code, string message) => Add(
        GostArchiveDiagnosticSeverity.Information,
        code,
        message);

    public void Warn(string code, string message) => Add(
        GostArchiveDiagnosticSeverity.Warning,
        code,
        message);

    private void Add(
        GostArchiveDiagnosticSeverity severity,
        string code,
        string message)
    {
        GostArchiveDiagnostic diagnostic = new(severity, code, message);
        _items.Add(diagnostic);
        Debug.WriteLine(
            $"[ARCHIVE] {diagnostic.Severity} " +
            $"{diagnostic.Code}: {diagnostic.Message}");
    }
}
