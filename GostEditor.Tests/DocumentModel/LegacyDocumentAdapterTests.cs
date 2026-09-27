using GostEditor.Core.DocumentModel;
using GostEditor.Core.DocumentModel.Blocks;
using GostEditor.Core.DocumentModel.Legacy;
using GostEditor.Core.Editing;
using GostEditor.Core.Models;
using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Tests.DocumentModel;

public sealed class LegacyDocumentAdapterTests
{
    private readonly LegacyDocumentAdapter _adapter = new();

    [Fact]
    public void ToDocumentModel_MapsParagraphsAndPageSettings()
    {
        GostDocument legacy = new()
        {
            PageWidth = 1000,
            MarginLeft = 100,
            Paragraphs =
            {
                new Paragraph
                {
                    Alignment = GostAlignment.Justify,
                    Style = ParagraphStyle.Heading2,
                    Runs =
                    {
                        new TextRun("Заголовок", isBold: true)
                    }
                }
            }
        };

        DocumentRoot model = _adapter.ToDocumentModel(legacy);

        DocumentSection section = Assert.Single(model.Sections);
        Assert.Equal(1000, section.PageSettings.Width);
        Assert.Equal(100, section.PageSettings.MarginLeft);

        ParagraphBlock paragraph =
            Assert.IsType<ParagraphBlock>(Assert.Single(section.Blocks));
        Assert.Equal("Заголовок", paragraph.GetPlainText());
        Assert.Equal(GostAlignment.Justify, paragraph.Properties.Alignment);
        Assert.Equal(ParagraphStyle.Heading2, paragraph.Properties.Style);
        Assert.False(paragraph.Id.IsEmpty);
        Assert.False(paragraph.Inlines[0].Id.IsEmpty);
    }

    [Fact]
    public void ToLegacyDocument_RoundTripPreservesTextFormatting()
    {
        GostDocument expected = new()
        {
            Paragraphs =
            {
                new Paragraph
                {
                    FirstLineIndent = 25,
                    LineSpacing = 1.2,
                    Alignment = GostAlignment.Center,
                    Style = ParagraphStyle.Heading1,
                    Runs =
                    {
                        new TextRun("Текст", isBold: true, isItalic: true)
                        {
                            FontSize = 18,
                            Color = 0xFF123456
                        }
                    }
                }
            }
        };

        GostDocument actual = _adapter.ToLegacyDocument(
            _adapter.ToDocumentModel(expected));

        Paragraph paragraph = Assert.Single(actual.Paragraphs);
        TextRun run = Assert.Single(paragraph.Runs);
        Assert.Equal("Текст", run.Text);
        Assert.True(run.IsBold);
        Assert.True(run.IsItalic);
        Assert.Equal(18, run.FontSize);
        Assert.Equal(0xFF123456u, run.Color);
        Assert.Equal(25, paragraph.FirstLineIndent);
        Assert.Equal(1.2, paragraph.LineSpacing);
        Assert.Equal(GostAlignment.Center, paragraph.Alignment);
        Assert.Equal(ParagraphStyle.Heading1, paragraph.Style);
    }

    [Fact]
    public void ToDocumentModel_MapsImagesAsFigureBlocks()
    {
        GostDocument legacy = new();
        ImageService imageService = new();
        byte[] imageBytes = TestImageData.CreatePng();

        ImageResult<ImagePlacementInfo> result =
            imageService.InsertPlacement(
                legacy,
                0,
                new CreateImageRequest(
                    imageBytes,
                    new ImageSize(120, 80)));

        Assert.True(result.IsSuccess);

        DocumentRoot model = _adapter.ToDocumentModel(legacy);

        FigureBlock figure = Assert.IsType<FigureBlock>(
            Assert.Single(model.Sections[0].Blocks));
        Assert.Equal(result.Value!.ImageId, figure.ImageId);
        Assert.Equal(120, figure.Width);
        Assert.Equal(80, figure.Height);
        Assert.True(model.Resources.Images.ContainsKey(figure.ImageId));
    }

    [Fact]
    public void ToLegacyDocument_PreservesImageResourceAndPlacement()
    {
        GostDocument legacy = new();
        ImageService imageService = new();
        byte[] imageBytes = TestImageData.CreatePng();
        ImageResult<ImagePlacementInfo> inserted =
            imageService.InsertPlacement(
                legacy,
                0,
                new CreateImageRequest(
                    imageBytes,
                    new ImageSize(90, 60)));
        Assert.True(inserted.IsSuccess);

        GostDocument roundTrip = _adapter.ToLegacyDocument(
            _adapter.ToDocumentModel(legacy));

        Paragraph placement = Assert.Single(roundTrip.Paragraphs);
        Assert.Equal(inserted.Value!.ImageId, placement.ImageId);
        Assert.Equal(90, placement.ImageWidth);
        Assert.Equal(60, placement.ImageHeight);
        Assert.Single(roundTrip.Images);
        Assert.Equal(imageBytes, roundTrip.Images[0].Data.ToArray());
    }

    [Fact]
    public void ToLegacyDocument_WhenModelContainsMultipleSections_Throws()
    {
        DocumentRoot model = new();
        model.Sections.Add(new DocumentSection());
        model.Sections.Add(new DocumentSection());

        Assert.Throws<NotSupportedException>(
            () => _adapter.ToLegacyDocument(model));
    }

    [Fact]
    public void ToLegacyDocument_WhenModelContainsTable_ThrowsInsteadOfLosingData()
    {
        DocumentRoot model = new();
        DocumentSection section = new();
        section.Blocks.Add(new TableBlock());
        model.Sections.Add(section);

        Assert.Throws<NotSupportedException>(
            () => _adapter.ToLegacyDocument(model));
    }

    [Fact]
    public void NodeIdentifiers_RemainStableDuringModelEditing()
    {
        GostDocument legacy = new()
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new TextRun("A")
                    }
                }
            }
        };

        DocumentRoot model = _adapter.ToDocumentModel(legacy);
        ParagraphBlock paragraph =
            Assert.IsType<ParagraphBlock>(model.Sections[0].Blocks[0]);
        DocumentNodeId blockId = paragraph.Id;
        DocumentNodeId inlineId = paragraph.Inlines[0].Id;

        paragraph.Properties.PageBreakBefore = true;
        Assert.IsType<GostEditor.Core.DocumentModel.Inlines.TextInline>(
            paragraph.Inlines[0]).Text += "B";

        Assert.Equal(blockId, paragraph.Id);
        Assert.Equal(inlineId, paragraph.Inlines[0].Id);
    }
}

public sealed class LegacyDocumentEditingBridgeTests
{
    [Fact]
    public void Bridge_RoundTripAppliesStructuredEditingWithoutChangingV2Contract()
    {
        GostDocument legacy = new()
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs = { new TextRun("A") }
                }
            }
        };
        LegacyDocumentEditingBridge bridge = new();
        DocumentEditingSession session = bridge.CreateSession(legacy);
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(
            session.Document.Sections[0].Blocks[0]);

        session.Text.InsertText(
            new DocumentLocation(paragraph.Id, 1),
            "B");
        GostDocument projected = bridge.CreateLegacySnapshot(session);

        Assert.Equal("AB", Assert.Single(projected.Paragraphs).GetPlainText());
    }
}
