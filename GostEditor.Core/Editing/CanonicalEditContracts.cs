using GostEditor.Core.DocumentModel;
using GostEditor.Core.TextEngine.DOM;
using System.Text.Json.Serialization;

namespace GostEditor.Core.Editing;

public static class CanonicalEditContract
{
    public const int CurrentEnvelopeVersion = 1;
    public const int CurrentOperationVersion = 1;
    public const int MaxOperationsPerTransaction = 1024;
    public const int MaxTextCharactersPerOperation = 1_000_000;
    public const int MaxInlinesPerParagraphPayload = 10_000;
    public const int MaxLinkTargetCharacters = 8_192;
    public const int MaxFontFamilyCharacters = 256;
}

public sealed record CanonicalEditEnvelope(
    int Version,
    Guid TransactionId,
    long BaseRevision,
    long ResultRevision,
    IReadOnlyList<CanonicalEditOperation> Operations);

public sealed record CanonicalEditOperation(
    int Version,
    Guid OperationId,
    CanonicalOperationData Data);

public enum CanonicalOperationKind
{
    InsertText,
    DeleteText,
    ReplaceText,
    SplitParagraph,
    MergeParagraphs,
    ChangeTextStyle,
    ChangeParagraphProperties,
    InsertParagraph,
    RemoveBlock,
    MoveBlock
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(InsertTextData), "insertText")]
[JsonDerivedType(typeof(DeleteTextData), "deleteText")]
[JsonDerivedType(typeof(ReplaceTextData), "replaceText")]
[JsonDerivedType(typeof(SplitParagraphData), "splitParagraph")]
[JsonDerivedType(typeof(MergeParagraphsData), "mergeParagraphs")]
[JsonDerivedType(typeof(ChangeTextStyleData), "changeTextStyle")]
[JsonDerivedType(
    typeof(ChangeParagraphPropertiesData),
    "changeParagraphProperties")]
[JsonDerivedType(typeof(InsertParagraphData), "insertParagraph")]
[JsonDerivedType(typeof(RemoveBlockData), "removeBlock")]
[JsonDerivedType(typeof(MoveBlockData), "moveBlock")]
public abstract record CanonicalOperationData
{
    public abstract CanonicalOperationKind Kind { get; }
}

public sealed record InsertTextData(
    DocumentNodeId ParagraphId,
    int Offset,
    string Text,
    CanonicalTextStyleData? Style = null) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.InsertText;
}

public sealed record DeleteTextData(
    DocumentNodeId ParagraphId,
    int Offset,
    int Length) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.DeleteText;
}

public sealed record ReplaceTextData(
    DocumentLocation Start,
    DocumentLocation End,
    string Text,
    CanonicalTextStyleData? Style = null) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.ReplaceText;
}

public sealed record SplitParagraphData(
    DocumentNodeId ParagraphId,
    int Offset,
    DocumentNodeId NewParagraphId) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.SplitParagraph;
}

public sealed record MergeParagraphsData(
    DocumentNodeId FirstParagraphId,
    DocumentNodeId SecondParagraphId) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.MergeParagraphs;
}

public sealed record ChangeTextStyleData(
    DocumentNodeId ParagraphId,
    int Offset,
    int Length,
    CanonicalTextStylePatch Patch) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.ChangeTextStyle;
}

public sealed record ChangeParagraphPropertiesData(
    DocumentNodeId ParagraphId,
    CanonicalParagraphPropertiesData Properties) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.ChangeParagraphProperties;
}

public sealed record InsertParagraphData(
    DocumentNodeId SectionId,
    int Index,
    CanonicalParagraphData Paragraph) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.InsertParagraph;
}

public sealed record RemoveBlockData(
    DocumentNodeId BlockId) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.RemoveBlock;
}

public sealed record MoveBlockData(
    DocumentNodeId BlockId,
    DocumentNodeId DestinationSectionId,
    int DestinationIndex) : CanonicalOperationData
{
    public override CanonicalOperationKind Kind =>
        CanonicalOperationKind.MoveBlock;
}

public sealed record CanonicalTextStyleData(
    bool IsBold,
    bool IsItalic,
    uint Color,
    double FontSize,
    string? FontFamily);

public sealed record CanonicalTextStylePatch(
    bool? IsBold = null,
    bool? IsItalic = null,
    uint? Color = null,
    double? FontSize = null,
    string? FontFamily = null,
    bool SetFontFamily = false)
{
    public bool IsEmpty =>
        IsBold is null && IsItalic is null && Color is null &&
        FontSize is null && !SetFontFamily;
}

public sealed record CanonicalParagraphPropertiesData(
    double FirstLineIndent,
    double LineSpacing,
    GostAlignment Alignment,
    ParagraphStyle Style,
    bool PageBreakBefore);

public sealed record CanonicalParagraphData(
    DocumentNodeId Id,
    CanonicalParagraphPropertiesData Properties,
    IReadOnlyList<CanonicalInlineData> Inlines);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(CanonicalTextInlineData), "text")]
[JsonDerivedType(typeof(CanonicalLinkInlineData), "link")]
[JsonDerivedType(typeof(CanonicalLineBreakInlineData), "lineBreak")]
public abstract record CanonicalInlineData(DocumentNodeId Id);

public sealed record CanonicalTextInlineData(
    DocumentNodeId Id,
    string Text,
    CanonicalTextStyleData Style) : CanonicalInlineData(Id);

public sealed record CanonicalLinkInlineData(
    DocumentNodeId Id,
    string Text,
    string Target,
    CanonicalTextStyleData Style) : CanonicalInlineData(Id);

public sealed record CanonicalLineBreakInlineData(
    DocumentNodeId Id) : CanonicalInlineData(Id);

public enum CanonicalApplyStatus
{
    Applied,
    Rejected
}

public enum CanonicalApplyFailureCode
{
    StaleRevision,
    InvalidEnvelope,
    InvalidOperation,
    InvalidTarget,
    ApplyFailed
}

public sealed record CanonicalApplyFailure(
    CanonicalApplyFailureCode Code,
    string Message,
    Guid? OperationId = null);

public sealed record CanonicalApplyResult(
    CanonicalApplyStatus Status,
    Guid TransactionId,
    long BaseRevision,
    long CurrentRevision,
    CanonicalApplyFailure? Failure)
{
    public bool IsSuccess => Status == CanonicalApplyStatus.Applied;
}
