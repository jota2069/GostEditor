using GostEditor.Core.Models;

namespace GostEditor.Core.Serialization;

public enum GostArchiveDiagnosticSeverity
{
    Information,
    Warning
}

public sealed record GostArchiveDiagnostic(
    GostArchiveDiagnosticSeverity Severity,
    string Code,
    string Message);

public sealed class GostArchiveLoadResult
{
    public GostArchiveLoadResult(
        GostDocument document,
        int? sourceVersion,
        int currentVersion,
        IReadOnlyList<GostArchiveDiagnostic> diagnostics)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        SourceVersion = sourceVersion;
        CurrentVersion = currentVersion;
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public GostDocument Document { get; }

    public int? SourceVersion { get; }

    public int CurrentVersion { get; }

    public IReadOnlyList<GostArchiveDiagnostic> Diagnostics { get; }

    public bool? WasMigrated => SourceVersion.HasValue
        ? SourceVersion.Value != CurrentVersion
        : null;
}
