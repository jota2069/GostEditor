using System;
using System.Collections.Generic;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

public enum RecoveryStartupState
{
    None,
    Recoverable,
    Corrupted
}

public enum RecoveryCandidateKind
{
    CurrentGeneration,
    OtherGeneration,
    Legacy
}

public sealed record RecoveryStartupIssue(
    string ArtifactPath,
    Exception Exception);

public sealed record RecoveryStartupResult
{
    public required RecoveryStartupState State { get; init; }

    public GostDocument? Document { get; init; }

    public RecoveryMetadata? Metadata { get; init; }

    public RecoveryCandidateKind? CandidateKind { get; init; }

    public int RecoverableCandidateCount { get; init; }

    public required IReadOnlyList<RecoveryStartupIssue> Issues { get; init; }
}
