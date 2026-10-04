using System.IO.Compression;
using System.Text;
using GostEditor.Core.Models;
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
        ArgumentNullException.ThrowIfNull(stream);
        MemoryStream? ownedBuffer = null;
        Stream packageStream = stream;
        if (!stream.CanSeek || stream.Position != 0)
        {
            ownedBuffer = await ReadBoundedArchiveAsync(
                stream,
                cancellationToken);
            packageStream = ownedBuffer;
        }
        else if (stream.Length > GostArchiveLimits.MaxArchiveBytes)
        {
            throw new InvalidDataException(
                $"Файл .gost превышает допустимый размер " +
                $"{GostArchiveLimits.MaxArchiveBytes} байт.");
        }

        try
        {
            GostZipPreflight.Validate(packageStream);
            return await ReadValidatedArchiveAsync(
                packageStream,
                cancellationToken);
        }
        finally
        {
            if (ownedBuffer is not null)
            {
                await ownedBuffer.DisposeAsync();
            }
        }
    }

    private async Task<GostArchiveReadResult> ReadValidatedArchiveAsync(
        Stream stream,
        CancellationToken cancellationToken)
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
            GostArchiveLimits.MaxManifestBytes,
            cancellationToken);
        ValidateManifestComplexity(manifestBytes);
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
            GostArchiveInputValidator.Validate(legacyDocument, entries);

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
        GostArchiveInputValidator.Validate(currentDocument, entries);

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

    private static async Task<MemoryStream> ReadBoundedArchiveAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        MemoryStream destination = new();
        byte[] buffer = new byte[81920];
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    destination.Position = 0;
                    return destination;
                }

                if (destination.Length >
                    GostArchiveLimits.MaxArchiveBytes - read)
                {
                    throw new InvalidDataException(
                        $"Файл .gost превышает допустимый размер " +
                        $"{GostArchiveLimits.MaxArchiveBytes} байт.");
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }
        }
        catch
        {
            await destination.DisposeAsync();
            throw;
        }
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
            jsonReader.MaxDepth = GostArchiveLimits.MaxJsonDepth;

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

    private static void ValidateManifestComplexity(byte[] manifestBytes)
    {
        try
        {
            ReadOnlySpan<byte> utf8 = manifestBytes;
            if (utf8.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                utf8 = utf8[3..];
            }

            System.Text.Json.Utf8JsonReader utf8Reader = new(
                utf8,
                new System.Text.Json.JsonReaderOptions
                {
                    CommentHandling =
                        System.Text.Json.JsonCommentHandling.Skip,
                    MaxDepth = GostArchiveLimits.MaxJsonDepth
                });
            int rawTokens = 0;
            while (utf8Reader.Read())
            {
                if (++rawTokens > GostArchiveLimits.MaxJsonTokens)
                {
                    throw new InvalidDataException(
                        "document.json содержит слишком много JSON-элементов.");
                }

                if (utf8Reader.TokenType ==
                        System.Text.Json.JsonTokenType.Number &&
                    utf8Reader.ValueSpan.Length >
                        GostArchiveLimits.MaxJsonNumberBytes)
                {
                    throw new InvalidDataException(
                        "document.json содержит числовой литерал " +
                        "недопустимой длины.");
                }
            }

            using MemoryStream stream = new(manifestBytes, writable: false);
            using StreamReader streamReader = new(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true);
            using JsonTextReader reader = new(streamReader)
            {
                MaxDepth = GostArchiveLimits.MaxJsonDepth,
                DateParseHandling = DateParseHandling.None
            };

            int tokens = 0;
            long totalStringCharacters = 0;
            while (reader.Read())
            {
                if (++tokens > GostArchiveLimits.MaxJsonTokens)
                {
                    throw new InvalidDataException(
                        "document.json содержит слишком много JSON-элементов.");
                }

                if (reader.Value is string value)
                {
                    if (value.Length > GostArchiveLimits.MaxJsonStringCharacters)
                    {
                        throw new InvalidDataException(
                            "document.json содержит строку недопустимой длины.");
                    }

                    totalStringCharacters += value.Length;
                    if (totalStringCharacters >
                        GostArchiveLimits.MaxTotalJsonStringCharacters)
                    {
                        throw new InvalidDataException(
                            "Суммарный размер строк document.json превышает " +
                            "допустимый предел.");
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is JsonException or DecoderFallbackException or
                  System.Text.Json.JsonException)
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
        NormalizeSharedFields(
            document.TitlePage,
            document.CodeListings,
            document.BibliographySources);

        foreach (GostParagraphV1Dto paragraph in document.Paragraphs)
        {
            if (paragraph is null)
            {
                throw new InvalidDataException(
                    "Коллекция Paragraphs содержит null.");
            }

            paragraph.Runs ??= new List<GostRunDto>();
            NormalizeRuns(paragraph.Runs);
        }

        foreach (GostImageV1Dto image in document.Images)
        {
            if (image is null)
            {
                throw new InvalidDataException(
                    "Коллекция Images содержит null.");
            }

            image.FileName ??= string.Empty;
            image.Caption ??= string.Empty;
        }
    }

    private static void NormalizeCurrentCollections(GostDocumentV2Dto document)
    {
        document.Paragraphs ??= new List<GostParagraphV2Dto>();
        document.Images ??= new List<GostImageV2Dto>();
        NormalizeSharedFields(
            document.TitlePage,
            document.CodeListings,
            document.BibliographySources);

        foreach (GostParagraphV2Dto paragraph in document.Paragraphs)
        {
            if (paragraph is null)
            {
                throw new InvalidDataException(
                    "Коллекция Paragraphs содержит null.");
            }

            paragraph.Runs ??= new List<GostRunDto>();
            NormalizeRuns(paragraph.Runs);
        }

        foreach (GostImageV2Dto image in document.Images)
        {
            if (image is null)
            {
                throw new InvalidDataException(
                    "Коллекция Images содержит null.");
            }

            image.FileName ??= string.Empty;
            image.MediaType ??= GostMediaTypes.Binary;
            image.Caption ??= string.Empty;
        }
    }

    private static void NormalizeRuns(List<GostRunDto> runs)
    {
        foreach (GostRunDto run in runs)
        {
            if (run is null)
            {
                throw new InvalidDataException(
                    "Коллекция Runs содержит null.");
            }

            run.Text ??= string.Empty;
        }
    }

    private static void NormalizeSharedFields(
        TitlePageInfo? titlePage,
        List<CodeListing>? codeListings,
        List<BibliographySource>? bibliographySources)
    {
        if (titlePage is not null)
        {
            titlePage.University ??= string.Empty;
            titlePage.Department ??= string.Empty;
            titlePage.Discipline ??= string.Empty;
            titlePage.WorkType ??= string.Empty;
            titlePage.WorkTitle ??= string.Empty;
            titlePage.StudentName ??= string.Empty;
            titlePage.GroupNumber ??= string.Empty;
            titlePage.TeacherName ??= string.Empty;
            titlePage.City ??= string.Empty;
        }

        foreach (CodeListing listing in codeListings ?? [])
        {
            if (listing is null)
            {
                throw new InvalidDataException(
                    "Коллекция CodeListings содержит null.");
            }

            listing.FileName ??= string.Empty;
            listing.RelativePath ??= string.Empty;
            listing.Language ??= string.Empty;
            listing.Content ??= string.Empty;
        }

        foreach (BibliographySource source in bibliographySources ?? [])
        {
            if (source is null)
            {
                throw new InvalidDataException(
                    "Коллекция BibliographySources содержит null.");
            }

            source.Description ??= string.Empty;
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
