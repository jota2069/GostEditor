using System;

namespace GostEditor.UI.Services;

public sealed record RecoveryMetadata
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public Guid SessionId { get; init; }

    public string? OriginalFilePath { get; init; }

    public DateTimeOffset SavedAtUtc { get; init; }
}
