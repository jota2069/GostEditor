using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Inlines;
using GostEditor.Core.Editing.Operations;

namespace GostEditor.Core.Editing;

public sealed class CanonicalEditExecutor
{
    public CanonicalApplyResult Apply(
        DocumentEditingSession session,
        CanonicalEditEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(envelope);

        CanonicalApplyFailure? envelopeFailure = ValidateEnvelope(
            session,
            envelope);
        if (envelopeFailure is not null)
        {
            return Rejected(session, envelope, envelopeFailure);
        }

        EditTransaction transaction = new(
            "Canonical edit transaction",
            envelope.TransactionId);
        foreach (CanonicalEditOperation operation in envelope.Operations)
        {
            CanonicalApplyFailure? operationFailure = TryCompile(
                session.Document,
                operation,
                out IEditOperation? compiled);
            if (operationFailure is not null)
            {
                return Rejected(session, envelope, operationFailure);
            }

            transaction.Add(compiled!);
        }

        try
        {
            session.Execute(transaction);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            if (exception.Data.Contains(
                    "GostEditor.EditTransaction.RollbackFailures") ||
                exception.Data.Contains(
                    "GostEditor.OperationHistory.IndexRollbackFailure"))
            {
                throw new InvalidOperationException(
                    "Canonical edit rollback could not restore the document.",
                    exception);
            }

            return Rejected(
                session,
                envelope,
                new CanonicalApplyFailure(
                    CanonicalApplyFailureCode.ApplyFailed,
                    exception.Message));
        }

        if (session.Revision != envelope.ResultRevision)
        {
            throw new InvalidOperationException(
                "Canonical revision diverged from the validated envelope.");
        }

        return new CanonicalApplyResult(
            CanonicalApplyStatus.Applied,
            envelope.TransactionId,
            envelope.BaseRevision,
            session.Revision,
            null);
    }

    public IReadOnlyList<CanonicalApplyResult> Replay(
        DocumentEditingSession session,
        IEnumerable<CanonicalEditEnvelope> envelopes)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(envelopes);
        List<CanonicalApplyResult> results = new();
        foreach (CanonicalEditEnvelope envelope in envelopes)
        {
            CanonicalApplyResult result = Apply(session, envelope);
            results.Add(result);
            if (!result.IsSuccess)
            {
                break;
            }
        }

        return results;
    }

    private static CanonicalApplyFailure? ValidateEnvelope(
        DocumentEditingSession session,
        CanonicalEditEnvelope envelope)
    {
        if (envelope.Version != CanonicalEditContract.CurrentEnvelopeVersion)
        {
            return InvalidEnvelope("Неподдерживаемая версия envelope.");
        }

        if (envelope.TransactionId == Guid.Empty)
        {
            return InvalidEnvelope("Transaction ID не может быть пустым.");
        }

        if (envelope.BaseRevision != session.Revision)
        {
            return new CanonicalApplyFailure(
                CanonicalApplyFailureCode.StaleRevision,
                $"Base revision {envelope.BaseRevision} не совпадает с " +
                $"current revision {session.Revision}.");
        }

        if (envelope.ResultRevision != envelope.BaseRevision + 1)
        {
            return InvalidEnvelope(
                "Result revision должна быть равна base revision + 1.");
        }

        if (envelope.Operations is null || envelope.Operations.Count == 0 ||
            envelope.Operations.Count >
                CanonicalEditContract.MaxOperationsPerTransaction)
        {
            return InvalidEnvelope(
                "Envelope должна содержать bounded непустой список operations.");
        }

        HashSet<Guid> operationIds = new();
        foreach (CanonicalEditOperation? operation in envelope.Operations)
        {
            if (operation is null || operation.OperationId == Guid.Empty ||
                !operationIds.Add(operation.OperationId) ||
                operation.Version !=
                    CanonicalEditContract.CurrentOperationVersion ||
                operation.Data is null)
            {
                return InvalidEnvelope(
                    "Operation ID/version/data в envelope некорректны.");
            }
        }

        return null;
    }

    private static CanonicalApplyFailure? TryCompile(
        DocumentRoot document,
        CanonicalEditOperation operation,
        out IEditOperation? compiled)
    {
        compiled = null;
        DeterministicDocumentNodeIdGenerator ids = new(operation.OperationId);

        switch (operation.Data)
        {
            case InsertTextData data:
                {
                    ParagraphBlock? paragraph = FindParagraph(document, data.ParagraphId);
                    if (paragraph is null)
                    {
                        return InvalidTarget(operation, data.ParagraphId);
                    }

                    if (!IsExactOffset(paragraph, data.Offset) ||
                        string.IsNullOrEmpty(data.Text) ||
                        data.Text.Length >
                            CanonicalEditContract.MaxTextCharactersPerOperation ||
                        !IsValidStyle(data.Style))
                    {
                        return InvalidOperation(operation, "Некорректная вставка текста.");
                    }

                    compiled = new InsertTextOperation(
                        data.ParagraphId,
                        data.Offset,
                        data.Text,
                        ToModel(data.Style),
                        ids.Next);
                    return null;
                }

            case DeleteTextData data:
                {
                    ParagraphBlock? paragraph = FindParagraph(document, data.ParagraphId);
                    if (paragraph is null)
                    {
                        return InvalidTarget(operation, data.ParagraphId);
                    }

                    if (data.Offset < 0 || data.Length <= 0 ||
                        data.Offset + data.Length > paragraph.TextLength)
                    {
                        return InvalidOperation(operation, "Некорректный диапазон удаления.");
                    }

                    compiled = new DeleteTextOperation(
                        data.ParagraphId,
                        data.Offset,
                        data.Length,
                        ids.Next);
                    return null;
                }

            case ReplaceTextData data:
                {
                    ParagraphBlock? start = FindParagraph(
                        document,
                        data.Start.BlockId);
                    ParagraphBlock? end = FindParagraph(document, data.End.BlockId);
                    if (start is null || end is null)
                    {
                        return InvalidTarget(
                            operation,
                            start is null ? data.Start.BlockId : data.End.BlockId);
                    }

                    if (!IsExactOffset(start, data.Start.Offset) ||
                        !IsExactOffset(end, data.End.Offset) ||
                        document.FindSectionContaining(data.Start.BlockId)?.Id !=
                            document.FindSectionContaining(data.End.BlockId)?.Id ||
                        document.GetBlockIndex(data.Start.BlockId) < 0 ||
                        document.GetBlockIndex(data.End.BlockId) < 0 ||
                        data.Text is null ||
                        data.Text.Length >
                            CanonicalEditContract.MaxTextCharactersPerOperation ||
                        !IsValidStyle(data.Style) ||
                        data.Start == data.End && data.Text.Length == 0 ||
                        IsNoOpReplacement(start, end, data))
                    {
                        return InvalidOperation(operation, "Некорректная замена текста.");
                    }

                    compiled = new ReplaceTextRangeOperation(
                        document,
                        new DocumentRange(data.Start, data.End),
                        data.Text,
                        ToModel(data.Style),
                        ids.Next);
                    return null;
                }

            case SplitParagraphData data:
                {
                    ParagraphBlock? paragraph = FindParagraph(document, data.ParagraphId);
                    if (paragraph is null)
                    {
                        return InvalidTarget(operation, data.ParagraphId);
                    }

                    if (!IsExactOffset(paragraph, data.Offset) ||
                        data.NewParagraphId.IsEmpty ||
                        document.FindNode(data.NewParagraphId) is not null)
                    {
                        return InvalidOperation(operation, "Некорректное разделение абзаца.");
                    }

                    compiled = new SplitParagraphOperation(
                        data.ParagraphId,
                        data.Offset,
                        data.NewParagraphId,
                        ids.Next);
                    return null;
                }

            case MergeParagraphsData data:
                {
                    ParagraphBlock? first = FindParagraph(
                        document,
                        data.FirstParagraphId);
                    ParagraphBlock? second = FindParagraph(
                        document,
                        data.SecondParagraphId);
                    if (first is null || second is null)
                    {
                        return InvalidTarget(
                            operation,
                            first is null
                                ? data.FirstParagraphId
                                : data.SecondParagraphId);
                    }

                    DocumentSection? firstSection = document.FindSectionContaining(
                        data.FirstParagraphId);
                    DocumentSection? secondSection = document.FindSectionContaining(
                        data.SecondParagraphId);
                    int firstIndex = firstSection?.Blocks.FindIndex(
                        block => block.Id == data.FirstParagraphId) ?? -1;
                    int secondIndex = secondSection?.Blocks.FindIndex(
                        block => block.Id == data.SecondParagraphId) ?? -1;
                    if (firstSection?.Id != secondSection?.Id ||
                        secondIndex != firstIndex + 1)
                    {
                        return InvalidOperation(
                            operation,
                            "Объединяемые абзацы не являются соседними.");
                    }

                    compiled = new MergeParagraphsOperation(
                        data.FirstParagraphId,
                        data.SecondParagraphId);
                    return null;
                }

            case ChangeTextStyleData data:
                {
                    ParagraphBlock? paragraph = FindParagraph(document, data.ParagraphId);
                    if (paragraph is null)
                    {
                        return InvalidTarget(operation, data.ParagraphId);
                    }

                    if (data.Patch is null || data.Patch.IsEmpty ||
                        !data.Patch.SetFontFamily &&
                            data.Patch.FontFamily is not null ||
                    data.Patch.SetFontFamily &&
                        data.Patch.FontFamily?.Length >
                            CanonicalEditContract.MaxFontFamilyCharacters ||
                        data.Offset < 0 || data.Length <= 0 ||
                        data.Offset + data.Length > paragraph.TextLength ||
                        data.Patch.FontSize is double size &&
                            (!double.IsFinite(size) || size <= 0) ||
                        !WouldChangeStyle(
                            paragraph,
                            data.Offset,
                            data.Length,
                            data.Patch))
                    {
                        return InvalidOperation(operation, "Некорректное форматирование текста.");
                    }

                    compiled = new ChangeTextStyleOperation(
                        data.ParagraphId,
                        data.Offset,
                        data.Length,
                        style => ApplyPatch(style, data.Patch),
                        ids.Next);
                    return null;
                }

            case ChangeParagraphPropertiesData data:
                {
                    ParagraphBlock? paragraph = FindParagraph(document, data.ParagraphId);
                    if (paragraph is null)
                    {
                        return InvalidTarget(operation, data.ParagraphId);
                    }

                    if (!IsValidProperties(data.Properties))
                    {
                        return InvalidOperation(operation, "Некорректные свойства абзаца.");
                    }

                    ParagraphProperties properties = ToModel(data.Properties);
                    if (PropertiesEqual(paragraph.Properties, properties))
                    {
                        return InvalidOperation(operation, "Операция не изменяет абзац.");
                    }

                    compiled = new ChangeParagraphPropertiesOperation(
                        data.ParagraphId,
                        properties);
                    return null;
                }

            case InsertParagraphData data:
                {
                    DocumentSection? section = document.FindNode(data.SectionId)
                        as DocumentSection;
                    if (section is null)
                    {
                        return InvalidTarget(operation, data.SectionId);
                    }

                    if (data.Index < 0 || data.Index > section.Blocks.Count)
                    {
                        return InvalidOperation(
                            operation,
                            "Некорректная позиция вставки абзаца.");
                    }

                    if (!TryCreateParagraph(
                            document,
                            data.Paragraph,
                            out ParagraphBlock? paragraph,
                            out string? error))
                    {
                        return InvalidOperation(operation, error!);
                    }

                    compiled = new InsertBlockOperation(
                        data.SectionId,
                        data.Index,
                        paragraph!);
                    return null;
                }

            case RemoveBlockData data:
                if (document.FindBlock(data.BlockId) is null)
                {
                    return InvalidTarget(operation, data.BlockId);
                }

                compiled = new RemoveBlockOperation(data.BlockId);
                return null;

            case MoveBlockData data:
                {
                    BlockNode? block = document.FindBlock(data.BlockId);
                    DocumentSection? destination = document.FindNode(
                        data.DestinationSectionId) as DocumentSection;
                    if (block is null || destination is null)
                    {
                        return InvalidTarget(
                            operation,
                            block is null ? data.BlockId : data.DestinationSectionId);
                    }

                    DocumentSection? source = document.FindSectionContaining(
                        data.BlockId);
                    int sourceIndex = source?.Blocks.FindIndex(
                        item => item.Id == data.BlockId) ?? -1;
                    int maxIndex = destination.Blocks.Count -
                        (source?.Id == destination.Id ? 1 : 0);
                    if (sourceIndex < 0 || data.DestinationIndex < 0 ||
                        data.DestinationIndex > maxIndex ||
                        source?.Id == destination.Id &&
                            sourceIndex == data.DestinationIndex)
                    {
                        return InvalidOperation(operation, "Некорректное перемещение блока.");
                    }

                    compiled = new MoveBlockOperation(
                        data.BlockId,
                        data.DestinationSectionId,
                        data.DestinationIndex);
                    return null;
                }

            default:
                return InvalidOperation(
                    operation,
                    $"Operation kind {operation.Data.Kind} не поддерживается.");
        }
    }

    private static bool TryCreateParagraph(
        DocumentRoot document,
        CanonicalParagraphData? data,
        out ParagraphBlock? paragraph,
        out string? error)
    {
        paragraph = null;
        error = null;
        if (data is null || data.Id.IsEmpty ||
            document.FindNode(data.Id) is not null ||
            !IsValidProperties(data.Properties) || data.Inlines is null ||
            data.Inlines.Count == 0 || data.Inlines.Count >
                CanonicalEditContract.MaxInlinesPerParagraphPayload)
        {
            error = "Paragraph payload содержит некорректные поля.";
            return false;
        }

        HashSet<DocumentNodeId> ids = new() { data.Id };
        ParagraphBlock candidate = new(data.Id)
        {
            Properties = ToModel(data.Properties)
        };
        long totalTextCharacters = 0;
        foreach (CanonicalInlineData? inline in data.Inlines)
        {
            if (inline is null || inline.Id.IsEmpty || !ids.Add(inline.Id) ||
                document.FindNode(inline.Id) is not null ||
                !TryCreateInline(inline, out InlineNode? created))
            {
                error = "Paragraph payload содержит некорректный inline.";
                return false;
            }

            totalTextCharacters += created!.TextLength;
            if (totalTextCharacters >
                CanonicalEditContract.MaxTextCharactersPerOperation)
            {
                error = "Paragraph payload превышает text limit.";
                return false;
            }

            candidate.Inlines.Add(created!);
        }

        paragraph = candidate;
        return true;
    }

    private static bool TryCreateInline(
        CanonicalInlineData data,
        out InlineNode? inline)
    {
        inline = data switch
        {
            CanonicalTextInlineData text when
                text.Text is not null && text.Style is not null &&
                IsValidStyle(text.Style) =>
                new TextInline(text.Id, text.Text, ToModel(text.Style)!),
            CanonicalLinkInlineData link when
                link.Text is not null && link.Target is not null &&
                link.Target.Length <=
                    CanonicalEditContract.MaxLinkTargetCharacters &&
                link.Style is not null && IsValidStyle(link.Style) =>
                new LinkInline(link.Id)
                {
                    Text = link.Text,
                    Target = link.Target,
                    Style = ToModel(link.Style)!
                },
            CanonicalLineBreakInlineData lineBreak =>
                new LineBreakInline(lineBreak.Id),
            _ => null
        };
        return inline is not null;
    }

    private static ParagraphBlock? FindParagraph(
        DocumentRoot document,
        DocumentNodeId id) =>
        document.FindBlock(id) as ParagraphBlock;

    private static bool IsExactOffset(ParagraphBlock paragraph, int offset) =>
        offset >= 0 && offset <= paragraph.TextLength;

    private static bool IsValidStyle(CanonicalTextStyleData? style) =>
        style is null ||
        double.IsFinite(style.FontSize) && style.FontSize > 0 &&
        (style.FontFamily is null || style.FontFamily.Length <=
            CanonicalEditContract.MaxFontFamilyCharacters);

    private static bool IsValidProperties(
        CanonicalParagraphPropertiesData? properties) =>
        properties is not null &&
        double.IsFinite(properties.FirstLineIndent) &&
        double.IsFinite(properties.LineSpacing) &&
        properties.LineSpacing > 0 &&
        Enum.IsDefined(properties.Alignment) &&
        Enum.IsDefined(properties.Style);

    private static TextStyle? ToModel(CanonicalTextStyleData? style) =>
        style is null
            ? null
            : new TextStyle
            {
                IsBold = style.IsBold,
                IsItalic = style.IsItalic,
                Color = style.Color,
                FontSize = style.FontSize,
                FontFamily = style.FontFamily
            };

    private static ParagraphProperties ToModel(
        CanonicalParagraphPropertiesData properties) =>
        new()
        {
            FirstLineIndent = properties.FirstLineIndent,
            LineSpacing = properties.LineSpacing,
            Alignment = properties.Alignment,
            Style = properties.Style,
            PageBreakBefore = properties.PageBreakBefore
        };

    private static void ApplyPatch(
        TextStyle style,
        CanonicalTextStylePatch patch)
    {
        if (patch.IsBold.HasValue)
        {
            style.IsBold = patch.IsBold.Value;
        }

        if (patch.IsItalic.HasValue)
        {
            style.IsItalic = patch.IsItalic.Value;
        }

        if (patch.Color.HasValue)
        {
            style.Color = patch.Color.Value;
        }

        if (patch.FontSize.HasValue)
        {
            style.FontSize = patch.FontSize.Value;
        }

        if (patch.SetFontFamily)
        {
            style.FontFamily = string.IsNullOrWhiteSpace(patch.FontFamily)
                ? null
                : patch.FontFamily;
        }
    }

    private static bool WouldChangeStyle(
        ParagraphBlock paragraph,
        int offset,
        int length,
        CanonicalTextStylePatch patch)
    {
        int end = offset + length;
        int current = 0;
        foreach (InlineNode inline in paragraph.Inlines)
        {
            int inlineStart = current;
            int inlineEnd = current + inline.TextLength;
            current = inlineEnd;
            if (inlineEnd <= offset || inlineStart >= end)
            {
                continue;
            }

            TextStyle? style = inline switch
            {
                TextInline text => text.Style,
                LinkInline link => link.Style,
                _ => null
            };
            if (style is null)
            {
                continue;
            }

            string? fontFamily = string.IsNullOrWhiteSpace(patch.FontFamily)
                ? null
                : patch.FontFamily;
            if (patch.IsBold.HasValue &&
                    patch.IsBold.Value != style.IsBold ||
                patch.IsItalic.HasValue &&
                    patch.IsItalic.Value != style.IsItalic ||
                patch.Color.HasValue && patch.Color.Value != style.Color ||
                patch.FontSize.HasValue &&
                    !patch.FontSize.Value.Equals(style.FontSize) ||
                patch.SetFontFamily && !string.Equals(
                    fontFamily,
                    style.FontFamily,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNoOpReplacement(
        ParagraphBlock start,
        ParagraphBlock end,
        ReplaceTextData data)
    {
        if (!ReferenceEquals(start, end) || data.Style is not null)
        {
            return false;
        }

        int startOffset = Math.Min(data.Start.Offset, data.End.Offset);
        int endOffset = Math.Max(data.Start.Offset, data.End.Offset);
        int length = endOffset - startOffset;
        return length == data.Text.Length && string.Equals(
            start.GetPlainText().Substring(startOffset, length),
            data.Text,
            StringComparison.Ordinal);
    }

    private static bool PropertiesEqual(
        ParagraphProperties left,
        ParagraphProperties right) =>
        left.FirstLineIndent.Equals(right.FirstLineIndent) &&
        left.LineSpacing.Equals(right.LineSpacing) &&
        left.Alignment == right.Alignment && left.Style == right.Style &&
        left.PageBreakBefore == right.PageBreakBefore;

    private static CanonicalApplyResult Rejected(
        DocumentEditingSession session,
        CanonicalEditEnvelope envelope,
        CanonicalApplyFailure failure) =>
        new(
            CanonicalApplyStatus.Rejected,
            envelope.TransactionId,
            envelope.BaseRevision,
            session.Revision,
            failure);

    private static CanonicalApplyFailure InvalidEnvelope(string message) =>
        new(CanonicalApplyFailureCode.InvalidEnvelope, message);

    private static CanonicalApplyFailure InvalidOperation(
        CanonicalEditOperation operation,
        string message) =>
        new(
            CanonicalApplyFailureCode.InvalidOperation,
            message,
            operation.OperationId);

    private static CanonicalApplyFailure InvalidTarget(
        CanonicalEditOperation operation,
        DocumentNodeId target) =>
        new(
            CanonicalApplyFailureCode.InvalidTarget,
            $"Target {target} не найден или имеет неверный kind.",
            operation.OperationId);
}
