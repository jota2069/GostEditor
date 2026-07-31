using System.IO.Compression;
using System.Text;
using GostEditor.Core.Serialization.Format;
using GostEditor.Core.Serialization.Migrations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GostEditor.Core.Serialization;

internal sealed class GostArchiveReadResult
{
    public required GostMigratableDocument Document { get; init; }
    public required IReadOnlyList<GostSerializationDiagnostic> Diagnostics { get; init; }
}

internal sealed class GostArchivePackageReader
{
    private readonly GostMigrationRegistry _migrationRegistry;

    public GostArchivePackageReader()
        : this(new GostMigrationRegistry(
        [
            new GostV0ToV1Migration(),
            new GostV1ToV2Migration()
        ]))
    {
    }

    internal GostArchivePackageReader(GostMigrationRegistry migrationRegistry)
    {
        _migrationRegistry = migrationRegistry;
    }

    public async Task<GostArchiveReadResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using ZipArchive archive = new(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: true);
        GostArchiveEntryIndex entries = new(archive);
        ZipArchiveEntry manifestEntry = entries.GetRequired(
            GostFormatPaths.Manifest,
            "Файл document.json не найден в архиве .gost.");
        byte[] manifestBytes = await GostArchiveEntryIndex.ReadBytesAsync(
            manifestEntry,
            GostArchiveEntryIndex.MaxManifestBytes,
            cancellationToken);
        JObject manifest = ParseManifest(manifestBytes);
        int formatVersion = ReadFormatVersion(manifest);

        if (formatVersion > GostFormatVersions.Current)
        {
            throw new NotSupportedException(
                $"Версия формата .gost {formatVersion} новее поддерживаемой версии {GostFormatVersions.Current}.");
        }

        if (formatVersion is GostFormatVersions.LegacyWithoutVersion or GostFormatVersions.Legacy)
        {
            GostDocumentV1Dto legacyDocument =
                Deserialize<GostDocumentV1Dto>(manifest);
            NormalizeLegacyCollections(legacyDocument);

            GostMigrationContext migrationContext = new(
                formatVersion,
                legacyDocument,
                entries);
            await _migrationRegistry.MigrateToAsync(
                migrationContext,
                GostFormatVersions.Current,
                cancellationToken);

            return new GostArchiveReadResult
            {
                Document = migrationContext.Document
                    ?? throw new InvalidOperationException(
                        "Цепочка миграций не создала каноническую модель документа."),
                Diagnostics = migrationContext.Diagnostics.Items
            };
        }

        GostSerializationDiagnostics diagnostics = new();
        ReportIgnoredLegacyV2Fields(manifest, diagnostics);
        GostDocumentV2Dto currentDocument =
            Deserialize<GostDocumentV2Dto>(manifest);
        NormalizeCurrentCollections(currentDocument);

        GostMigratableDocument document = await GostV2PackageMapper.ReadAsync(
            currentDocument,
            entries,
            diagnostics,
            cancellationToken);

        return new GostArchiveReadResult
        {
            Document = document,
            Diagnostics = diagnostics.Items
        };
    }

    private static JObject ParseManifest(byte[] manifestBytes)
    {
        try
        {
            using MemoryStream stream = new(manifestBytes, writable: false);
            using StreamReader streamReader = new(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            using JsonTextReader jsonReader = new(streamReader);

            return JObject.Load(
                jsonReader,
                new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling =
                        DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Load
                });
        }
        catch (Exception exception)
            when (exception is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException(
                "Файл document.json содержит некорректный JSON.",
                exception);
        }
    }

    private static int ReadFormatVersion(JObject manifest)
    {
        JToken? versionToken = manifest.GetValue(
            nameof(GostDocumentV2Dto.FormatVersion),
            StringComparison.Ordinal);

        if (versionToken == null)
        {
            return GostFormatVersions.LegacyWithoutVersion;
        }

        if (versionToken.Type != JTokenType.Integer)
        {
            throw new InvalidDataException(
                "FormatVersion должен быть целым числом.");
        }

        int formatVersion;
        try
        {
            formatVersion = versionToken.Value<int>();
        }
        catch (Exception exception)
            when (exception is OverflowException or FormatException)
        {
            throw new InvalidDataException(
                "FormatVersion находится вне допустимого диапазона.",
                exception);
        }

        if (formatVersion < 0)
        {
            throw new InvalidDataException(
                "FormatVersion не может быть отрицательным.");
        }

        return formatVersion;
    }

    private static T Deserialize<T>(JObject manifest)
    {
        try
        {
            return manifest.ToObject<T>()
                ?? throw new InvalidDataException(
                    "Не удалось десериализовать document.json.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "document.json не соответствует заявленной версии формата.",
                exception);
        }
    }

    private static void NormalizeLegacyCollections(GostDocumentV1Dto document)
    {
        document.Paragraphs ??= new List<GostParagraphV1Dto>();
        document.Images ??= new List<GostImageV1Dto>();

        foreach (GostParagraphV1Dto paragraph in document.Paragraphs)
        {
            paragraph.Runs ??= new List<GostRunDto>();
        }
    }

    private static void NormalizeCurrentCollections(GostDocumentV2Dto document)
    {
        document.Paragraphs ??= new List<GostParagraphV2Dto>();
        document.Images ??= new List<GostImageV2Dto>();

        foreach (GostParagraphV2Dto paragraph in document.Paragraphs)
        {
            paragraph.Runs ??= new List<GostRunDto>();
        }
    }

    private static void ReportIgnoredLegacyV2Fields(
        JObject manifest,
        GostSerializationDiagnostics diagnostics)
    {
        if (manifest["Images"] is JArray images)
        {
            foreach (JObject image in images.OfType<JObject>())
            {
                if (image.GetValue("Data", StringComparison.Ordinal) is
                    { Type: not JTokenType.Null })
                {
                    diagnostics.Warn(
                        "V2_INLINE_IMAGE_IGNORED",
                        "Поле Images[].Data не является источником данных в формате v2.");
                }
            }
        }

        if (manifest["Paragraphs"] is JArray paragraphs)
        {
            foreach (JObject paragraph in paragraphs.OfType<JObject>())
            {
                if (paragraph.GetValue(
                        "ImageFileName",
                        StringComparison.Ordinal) is
                    { Type: not JTokenType.Null })
                {
                    diagnostics.Warn(
                        "V2_LEGACY_IMAGE_PATH_IGNORED",
                        "Поле Paragraphs[].ImageFileName игнорируется в формате v2.");
                }
            }
        }
    }
}
