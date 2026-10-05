using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace GostEditor.Core.Services;

internal sealed class OpenXmlDocxWriter
{
    private const string GlobalFontName = "Times New Roman";
    private const double GlobalFontSize = 14D;
    private const double ParagraphIndentCm = 1.25D;
    private const double CmToTwips = 1440D / 2.54D;
    private const double DipToTwips = 1440D / 96D;
    private const double DipToPoints = 72D / 96D;
    private const long PointsToEmus = 12700L;
    private const string DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string PictureNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/picture";

    private readonly IImageService _imageService;
    private uint _drawingId = 1;

    public OpenXmlDocxWriter(IImageService imageService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
    }

    public void Write(
        GostDocument document,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        using NonClosingStream packageStream = new(output);
        using WordprocessingDocument package = WordprocessingDocument.Create(
            packageStream,
            WordprocessingDocumentType.Document,
            autoSave: true);

        MainDocumentPart mainPart = package.AddMainDocumentPart();
        mainPart.Document = new W.Document(new W.Body());
        AddStyles(mainPart);
        AddSettings(mainPart);
        string footerRelationshipId = AddPageNumberFooter(mainPart);

        W.Body body = mainPart.Document.Body!;
        if (document.Modules.HasTitlePage)
        {
            AddTitlePage(body, document.TitlePage, cancellationToken);
        }

        if (document.Modules.HasTableOfContents)
        {
            AddTableOfContents(
                body,
                document.Modules.TOCMaxLevel,
                cancellationToken);
        }

        AddBody(mainPart, body, document, cancellationToken);

        if (document.Modules.HasBibliography)
        {
            AddBibliography(body, document.BibliographySources, cancellationToken);
        }

        if (document.Modules.HasAppendix &&
            document.CodeListings.Any(listing => listing.IsSelected))
        {
            AddCodeListings(body, document.CodeListings, cancellationToken);
        }

        body.Append(CreateSectionProperties(document, footerRelationshipId));
        cancellationToken.ThrowIfCancellationRequested();
        mainPart.Document.Save();
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        StyleDefinitionsPart stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new W.Styles(
            CreateParagraphStyle(
                "Normal",
                "Normal",
                isDefault: true,
                fontName: GlobalFontName,
                fontSize: GlobalFontSize),
            CreateParagraphStyle(
                "Heading1",
                "heading 1",
                isDefault: false,
                fontName: GlobalFontName,
                fontSize: GlobalFontSize,
                bold: true,
                outlineLevel: 0),
            CreateParagraphStyle(
                "Heading2",
                "heading 2",
                isDefault: false,
                fontName: GlobalFontName,
                fontSize: GlobalFontSize,
                bold: true,
                outlineLevel: 1),
            CreateParagraphStyle(
                "Heading3",
                "heading 3",
                isDefault: false,
                fontName: GlobalFontName,
                fontSize: GlobalFontSize,
                bold: true,
                outlineLevel: 2),
            CreateParagraphStyle(
                "Code",
                "Code",
                isDefault: false,
                fontName: "Consolas",
                fontSize: 10D));
        stylesPart.Styles.Save();
    }

    private static W.Style CreateParagraphStyle(
        string styleId,
        string name,
        bool isDefault,
        string fontName,
        double fontSize,
        bool bold = false,
        int? outlineLevel = null)
    {
        W.Style style = new()
        {
            Type = W.StyleValues.Paragraph,
            StyleId = styleId,
            Default = isDefault
        };
        style.Append(new W.StyleName { Val = name });
        if (!isDefault)
        {
            style.Append(
                new W.BasedOn { Val = "Normal" },
                new W.NextParagraphStyle { Val = "Normal" });
        }

        W.StyleParagraphProperties paragraphProperties = new();
        if (outlineLevel.HasValue)
        {
            paragraphProperties.Append(
                new W.KeepNext(),
                new W.OutlineLevel { Val = outlineLevel.Value });
        }
        style.Append(paragraphProperties);

        W.StyleRunProperties runProperties = new(
            CreateRunFonts(fontName),
            new W.FontSize { Val = ToHalfPoints(fontSize) },
            new W.FontSizeComplexScript { Val = ToHalfPoints(fontSize) });
        if (bold)
        {
            runProperties.InsertAfter(new W.Bold(), runProperties.GetFirstChild<W.RunFonts>());
            runProperties.InsertAfter(new W.BoldComplexScript(), runProperties.GetFirstChild<W.Bold>());
        }
        style.Append(runProperties);
        return style;
    }

    private static void AddSettings(MainDocumentPart mainPart)
    {
        DocumentSettingsPart settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new W.Settings(
            new W.UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    private static string AddPageNumberFooter(MainDocumentPart mainPart)
    {
        FooterPart footerPart = mainPart.AddNewPart<FooterPart>();
        W.Paragraph pageNumber = new(
            new W.ParagraphProperties(
                new W.Justification { Val = W.JustificationValues.Center }));
        pageNumber.Append(CreateFieldRuns(" PAGE ", "1"));
        footerPart.Footer = new W.Footer(pageNumber);
        footerPart.Footer.Save();
        return mainPart.GetIdOfPart(footerPart);
    }

    private static void AddTitlePage(
        W.Body body,
        TitlePageInfo titlePage,
        CancellationToken cancellationToken)
    {
        AppendTextParagraph(body, "Министерство науки и высшего образования Российской Федерации", W.JustificationValues.Center);
        AppendTextParagraph(body, "Федеральное государственное бюджетное образовательное учреждение", W.JustificationValues.Center);
        AppendTextParagraph(body, "высшего образования", W.JustificationValues.Center);
        AppendTextParagraph(body, titlePage.University, W.JustificationValues.Center, bold: true);
        AppendTextParagraph(body, titlePage.Department, W.JustificationValues.Center);
        AppendBlankParagraphs(body, 2);

        AppendTextParagraph(body, titlePage.WorkType, W.JustificationValues.Center);
        AppendTextParagraph(body, $"по дисциплине «{titlePage.Discipline}»", W.JustificationValues.Center);
        AppendTextParagraph(body, $"на тему «{titlePage.WorkTitle}»", W.JustificationValues.Center);
        AppendBlankParagraphs(body, 5);

        AppendTextParagraph(body, "Выполнил:", W.JustificationValues.Right, bold: true);
        AppendTextParagraph(body, $"студент группы {titlePage.GroupNumber}", W.JustificationValues.Right);
        AppendTextParagraph(body, titlePage.StudentName, W.JustificationValues.Right);
        AppendTextParagraph(body, "Принял:", W.JustificationValues.Right, bold: true);
        AppendTextParagraph(body, titlePage.TeacherName, W.JustificationValues.Right);
        AppendBlankParagraphs(body, 5);

        W.Paragraph city = CreateTextParagraph(
            $"{titlePage.City}, {titlePage.Year} г.",
            W.JustificationValues.Center);
        city.Append(new W.Run(new W.Break { Type = W.BreakValues.Page }));
        body.Append(city);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void AddTableOfContents(
        W.Body body,
        int maxLevel,
        CancellationToken cancellationToken)
    {
        int normalizedMaxLevel = Math.Clamp(maxLevel, 1, 3);
        body.Append(
            CreateTextParagraph(
                "СОДЕРЖАНИЕ",
                W.JustificationValues.Center,
                bold: true,
                styleId: "Heading1"));

        W.Paragraph fieldParagraph = new(
            CreateFieldRuns(
                $" TOC \\o \"1-{normalizedMaxLevel}\" \\h \\z \\u ",
                string.Empty));
        fieldParagraph.Append(new W.Run(new W.Break { Type = W.BreakValues.Page }));
        body.Append(fieldParagraph);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void AddBody(
        MainDocumentPart mainPart,
        W.Body body,
        GostDocument document,
        CancellationToken cancellationToken)
    {
        int figureNumber = 1;
        for (int paragraphIndex = 0;
             paragraphIndex < document.Paragraphs.Count;
             paragraphIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paragraph paragraph = document.Paragraphs[paragraphIndex];
            if (paragraph.ImageId.HasValue)
            {
                ImageResult<ResolvedImagePlacement> resolved =
                    _imageService.ResolvePlacement(document, paragraphIndex);
                if (!resolved.IsSuccess)
                {
                    throw CreateImageResolutionException(
                        paragraphIndex,
                        paragraph.ImageId.Value,
                        resolved.Error);
                }

                InsertImage(
                    mainPart,
                    body,
                    paragraph,
                    resolved.Value!,
                    figureNumber);
                figureNumber++;
                continue;
            }

            W.Paragraph wordParagraph = new(CreateParagraphProperties(paragraph));
            bool textAdded = false;
            foreach (TextRun run in paragraph.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                wordParagraph.Append(CreateRun(run, GlobalFontName, GlobalFontSize));
                textAdded = true;
            }

            if (!textAdded)
            {
                wordParagraph.Append(
                    CreateRun("\u00A0", GlobalFontName, GlobalFontSize));
            }
            body.Append(wordParagraph);
        }
    }

    private void InsertImage(
        MainDocumentPart mainPart,
        W.Body body,
        Paragraph sourceParagraph,
        ResolvedImagePlacement image,
        int figureNumber)
    {
        long width = ConvertDipToEmus(image.Size.Width, "ширина", image, figureNumber);
        long height = ConvertDipToEmus(image.Size.Height, "высота", image, figureNumber);

        PreparedImage preparedImage;
        try
        {
            preparedImage = PrepareImage(image.Content.Data);
        }
        catch (Exception ex)
        {
            throw CreateInvalidImageException(image, figureNumber, ex);
        }

        ImagePart imagePart = mainPart.AddImagePart(preparedImage.PartType);
        try
        {
            using MemoryStream imageStream = new(
                preparedImage.Data.ToArray(),
                writable: false);
            imagePart.FeedData(imageStream);
        }
        catch (Exception ex)
        {
            throw CreateInvalidImageException(image, figureNumber, ex);
        }

        string relationshipId = mainPart.GetIdOfPart(imagePart);
        uint drawingId = _drawingId++;
        W.Drawing drawing = CreateInlineDrawing(
            relationshipId,
            image.Content.FileName,
            width,
            height,
            drawingId);

        body.Append(
            new W.Paragraph(
                new W.ParagraphProperties(
                    new W.Justification { Val = W.JustificationValues.Center }),
                new W.Run(drawing)));

        W.Paragraph caption = new(
            new W.ParagraphProperties(
                new W.Justification { Val = W.JustificationValues.Center }),
            CreateRun($"Рисунок {figureNumber}", GlobalFontName, 12D));
        if (!string.IsNullOrWhiteSpace(sourceParagraph.GetPlainText()))
        {
            caption.Append(CreateRun(" — ", GlobalFontName, 12D));
            foreach (TextRun run in sourceParagraph.Runs)
            {
                if (!string.IsNullOrEmpty(run.Text))
                {
                    caption.Append(CreateRun(run, GlobalFontName, 12D));
                }
            }
        }
        body.Append(caption);
    }

    private static PreparedImage PrepareImage(ReadOnlyMemory<byte> data)
    {
        using SKData imageData = SKData.CreateCopy(data.Span);
        using SKCodec? codec = SKCodec.Create(imageData);
        if (codec is null)
        {
            throw new InvalidDataException("Данные изображения не распознаны.");
        }

        PartTypeInfo? originalPartType = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => ImagePartType.Png,
            SKEncodedImageFormat.Jpeg => ImagePartType.Jpeg,
            SKEncodedImageFormat.Gif => ImagePartType.Gif,
            SKEncodedImageFormat.Bmp => ImagePartType.Bmp,
            SKEncodedImageFormat.Ico => ImagePartType.Icon,
            _ => null
        };
        if (originalPartType.HasValue)
        {
            return new PreparedImage(originalPartType.Value, data);
        }

        using SKBitmap? bitmap = SKBitmap.Decode(imageData);
        if (bitmap is null)
        {
            throw new InvalidDataException(
                $"Формат изображения {codec.EncodedFormat} не удалось декодировать.");
        }

        using SKImage decodedImage = SKImage.FromBitmap(bitmap);
        using SKData? png = decodedImage.Encode(SKEncodedImageFormat.Png, 100);
        if (png is null)
        {
            throw new InvalidDataException(
                $"Формат изображения {codec.EncodedFormat} не удалось преобразовать в PNG.");
        }

        return new PreparedImage(ImagePartType.Png, png.ToArray());
    }

    private static InvalidDataException CreateInvalidImageException(
        ResolvedImagePlacement image,
        int figureNumber,
        Exception innerException)
    {
        return new InvalidDataException(
            $"Не удалось экспортировать рисунок {figureNumber} " +
            $"из абзаца {image.ParagraphIndex} " +
            $"(ImageId={image.ImageId}): данные изображения повреждены " +
            "или имеют неподдерживаемый формат.",
            innerException);
    }

    private static W.Drawing CreateInlineDrawing(
        string relationshipId,
        string fileName,
        long width,
        long height,
        uint drawingId)
    {
        PIC.Picture picture = new(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties
                {
                    Id = drawingId,
                    Name = string.IsNullOrWhiteSpace(fileName)
                        ? $"Image {drawingId}"
                        : fileName
                },
                new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList())
                {
                    Preset = A.ShapeTypeValues.Rectangle
                }));

        A.GraphicData graphicData = new(picture) { Uri = PictureNamespace };
        DW.Inline inline = new(
            new DW.Extent { Cx = width, Cy = height },
            new DW.EffectExtent
            {
                LeftEdge = 0L,
                TopEdge = 0L,
                RightEdge = 0L,
                BottomEdge = 0L
            },
            new DW.DocProperties
            {
                Id = drawingId,
                Name = $"Figure {drawingId}"
            },
            new DW.NonVisualGraphicFrameDrawingProperties(
                new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(graphicData))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U
        };
        return new W.Drawing(inline);
    }

    private static long ConvertDipToEmus(
        double value,
        string dimensionName,
        ResolvedImagePlacement image,
        int figureNumber)
    {
        try
        {
            int points = checked((int)Math.Round(
                value * DipToPoints,
                MidpointRounding.AwayFromZero));
            if (points <= 0)
            {
                throw new OverflowException();
            }
            return checked(points * PointsToEmus);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException(
                $"Не удалось экспортировать рисунок {figureNumber} " +
                $"из абзаца {image.ParagraphIndex} " +
                $"(ImageId={image.ImageId}): {dimensionName} {value} DIP " +
                "не может быть представлена в DOCX.",
                ex);
        }
    }

    private static InvalidDataException CreateImageResolutionException(
        int paragraphIndex,
        Guid imageId,
        ImageError? error)
    {
        string details = error is null
            ? "сервис изображений не вернул ни результат, ни описание ошибки"
            : $"{error.Code}: {error.Message}";
        return new InvalidDataException(
            $"Не удалось получить изображение из абзаца {paragraphIndex} " +
            $"(ImageId={imageId}) для экспорта: {details}.");
    }

    private static void AddBibliography(
        W.Body body,
        IEnumerable<BibliographySource> sources,
        CancellationToken cancellationToken)
    {
        List<BibliographySource> selectedSources = sources
            .Where(source => source.IsSelected && !string.IsNullOrWhiteSpace(source.Description))
            .OrderBy(source => source.Order)
            .ToList();
        if (selectedSources.Count == 0)
        {
            return;
        }

        body.Append(CreateSectionHeading("СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ"));
        body.Append(new W.Paragraph());
        for (int index = 0; index < selectedSources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendTextParagraph(
                body,
                $"{index + 1}. {selectedSources[index].Description}",
                W.JustificationValues.Both);
        }
    }

    private static void AddCodeListings(
        W.Body body,
        IEnumerable<CodeListing> listings,
        CancellationToken cancellationToken)
    {
        List<CodeListing> selectedListings = listings
            .Where(listing => listing.IsSelected)
            .ToList();
        if (selectedListings.Count == 0)
        {
            return;
        }

        body.Append(CreateSectionHeading("ПРИЛОЖЕНИЕ А"));
        body.Append(new W.Paragraph());

        for (int index = 0; index < selectedListings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CodeListing listing = selectedListings[index];
            body.Append(
                CreateTextParagraph(
                    $"Листинг {index + 1} — файл {listing.RelativePath}",
                    W.JustificationValues.Left,
                    fontName: GlobalFontName,
                    fontSize: 12D,
                    italic: true));

            string[] lines = listing.Content.Split(
                ["\r\n", "\n"],
                StringSplitOptions.None);
            foreach (string line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string cleanLine = line.Replace("\t", "    ");
                if (string.IsNullOrWhiteSpace(cleanLine))
                {
                    cleanLine = "\u00A0";
                }
                body.Append(
                    CreateTextParagraph(
                        cleanLine,
                        W.JustificationValues.Left,
                        styleId: "Code",
                        fontName: "Consolas",
                        fontSize: 10D));
            }
            body.Append(new W.Paragraph());
        }
    }

    private static W.Paragraph CreateSectionHeading(string text)
    {
        W.ParagraphProperties properties = new(
            new W.ParagraphStyleId { Val = "Heading1" },
            new W.PageBreakBefore(),
            new W.Justification { Val = W.JustificationValues.Center });
        return new W.Paragraph(
            properties,
            CreateRun(text, GlobalFontName, GlobalFontSize, bold: true));
    }

    private static W.ParagraphProperties CreateParagraphProperties(Paragraph paragraph)
    {
        W.ParagraphProperties properties = new();
        string? styleId = paragraph.Style switch
        {
            ParagraphStyle.Heading1 => "Heading1",
            ParagraphStyle.Heading2 => "Heading2",
            ParagraphStyle.Heading3 => "Heading3",
            ParagraphStyle.Code => "Code",
            _ => null
        };
        if (styleId is not null)
        {
            properties.Append(new W.ParagraphStyleId { Val = styleId });
        }
        if (paragraph.PageBreakBefore)
        {
            properties.Append(new W.PageBreakBefore());
        }

        properties.Append(
            new W.SpacingBetweenLines
            {
                Line = Math.Max(1, (int)Math.Round(paragraph.LineSpacing * 240D))
                    .ToString(CultureInfo.InvariantCulture),
                LineRule = W.LineSpacingRuleValues.Auto
            });
        if (paragraph.FirstLineIndent > 0)
        {
            properties.Append(
                new W.Indentation
                {
                    FirstLine = Math.Round(ParagraphIndentCm * CmToTwips)
                        .ToString(CultureInfo.InvariantCulture)
                });
        }
        properties.Append(
            new W.Justification
            {
                Val = paragraph.Alignment switch
                {
                    GostAlignment.Center => W.JustificationValues.Center,
                    GostAlignment.Right => W.JustificationValues.Right,
                    GostAlignment.Justify => W.JustificationValues.Both,
                    _ => W.JustificationValues.Left
                }
            });
        return properties;
    }

    private static W.SectionProperties CreateSectionProperties(
        GostDocument document,
        string footerRelationshipId)
    {
        return new W.SectionProperties(
            new W.FooterReference
            {
                Type = W.HeaderFooterValues.Default,
                Id = footerRelationshipId
            },
            new W.PageSize
            {
                Width = ToUInt32Twips(document.PageWidth),
                Height = ToUInt32Twips(document.PageHeight)
            },
            new W.PageMargin
            {
                Left = ToUInt32Twips(document.MarginLeft),
                Right = ToUInt32Twips(document.MarginRight),
                Top = ToInt32Twips(document.MarginTop),
                Bottom = ToInt32Twips(document.MarginBottom),
                Header = 0U,
                Footer = 0U,
                Gutter = 0U
            });
    }

    private static UInt32Value ToUInt32Twips(double dip)
    {
        uint value = checked((uint)Math.Max(0, Math.Round(dip * DipToTwips)));
        return value;
    }

    private static Int32Value ToInt32Twips(double dip)
    {
        int value = checked((int)Math.Max(0, Math.Round(dip * DipToTwips)));
        return value;
    }

    private static W.Paragraph CreateTextParagraph(
        string text,
        W.JustificationValues alignment,
        bool bold = false,
        bool italic = false,
        string? styleId = null,
        string fontName = GlobalFontName,
        double fontSize = GlobalFontSize)
    {
        W.ParagraphProperties properties = new();
        if (styleId is not null)
        {
            properties.Append(new W.ParagraphStyleId { Val = styleId });
        }
        properties.Append(new W.Justification { Val = alignment });
        return new W.Paragraph(
            properties,
            CreateRun(text, fontName, fontSize, bold, italic));
    }

    private static void AppendTextParagraph(
        W.Body body,
        string text,
        W.JustificationValues alignment,
        bool bold = false,
        bool italic = false)
    {
        body.Append(CreateTextParagraph(text, alignment, bold, italic));
    }

    private static void AppendBlankParagraphs(W.Body body, int count)
    {
        for (int index = 0; index < count; index++)
        {
            body.Append(new W.Paragraph());
        }
    }

    private static W.Run CreateRun(
        TextRun source,
        string fontName,
        double fallbackFontSize)
    {
        return CreateRun(
            source.Text,
            fontName,
            source.FontSize > 0 ? source.FontSize : fallbackFontSize,
            source.IsBold,
            source.IsItalic,
            source.Color);
    }

    private static W.Run CreateRun(
        string text,
        string fontName,
        double fontSize,
        bool bold = false,
        bool italic = false,
        uint color = 0xFF000000)
    {
        W.RunProperties properties = new(
            CreateRunFonts(fontName));
        if (bold)
        {
            properties.Append(new W.Bold(), new W.BoldComplexScript());
        }
        if (italic)
        {
            properties.Append(new W.Italic(), new W.ItalicComplexScript());
        }
        properties.Append(
            new W.Color { Val = $"{color & 0x00FFFFFF:X6}" },
            new W.FontSize { Val = ToHalfPoints(fontSize) },
            new W.FontSizeComplexScript { Val = ToHalfPoints(fontSize) });
        return new W.Run(
            properties,
            new W.Text(text ?? string.Empty)
            {
                Space = SpaceProcessingModeValues.Preserve
            });
    }

    private static W.RunFonts CreateRunFonts(string fontName)
    {
        return new W.RunFonts
        {
            Ascii = fontName,
            HighAnsi = fontName,
            EastAsia = fontName,
            ComplexScript = fontName
        };
    }

    private static StringValue ToHalfPoints(double fontSize)
    {
        double normalized = fontSize > 0 ? fontSize : GlobalFontSize;
        return Math.Round(normalized * 2D, MidpointRounding.AwayFromZero)
            .ToString(CultureInfo.InvariantCulture);
    }

    private static OpenXmlElement[] CreateFieldRuns(
        string instruction,
        string displayText)
    {
        return
        [
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
            new W.Run(
                new W.FieldCode(instruction)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }),
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }),
            new W.Run(
                new W.RunProperties(
                    CreateRunFonts(GlobalFontName),
                    new W.FontSize { Val = ToHalfPoints(GlobalFontSize) },
                    new W.FontSizeComplexScript { Val = ToHalfPoints(GlobalFontSize) }),
                new W.Text(displayText)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }),
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End })
        ];
    }

    private readonly record struct PreparedImage(
        PartTypeInfo PartType,
        ReadOnlyMemory<byte> Data);

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
