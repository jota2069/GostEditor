using System.Diagnostics;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed record GostSerializationDiagnostic(string Code, string Message);

internal sealed class GostSerializationDiagnostics
{
    private readonly List<GostSerializationDiagnostic> _items = new();

    public IReadOnlyList<GostSerializationDiagnostic> Items => _items;

    public void Warn(string code, string message)
    {
        GostSerializationDiagnostic diagnostic = new(code, message);
        _items.Add(diagnostic);
        Debug.WriteLine($"[ARCHIVE] WARNING {diagnostic.Code}: {diagnostic.Message}");
    }
}
