using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.Serialization;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Services;
using GostEditor.UI.ViewModels;

namespace GostEditor.Tests.Services;

public class PersistedMutationTrackingTests
{
    [Fact]
    public void ViewModelMutations_UpdateLiveDocumentAndRevision()
    {
        DocumentSessionState session = new();
        session.StartNew();
        MainWindowViewModel viewModel = CreateViewModel(session);
        GostDocument document = new();
        viewModel.CurrentDocument = document;
        long revision = session.ChangeVersion;

        viewModel.WorkTitle = "New title";
        AssertMutation(ref revision, session);
        Assert.Equal("New title", document.TitlePage.WorkTitle);

        viewModel.WorkTitle = "New title";
        Assert.Equal(revision, session.ChangeVersion);

        viewModel.HasAppendix = !viewModel.HasAppendix;
        AssertMutation(ref revision, session);
        Assert.Equal(viewModel.HasAppendix, document.Modules.HasAppendix);

        CodeListingViewModel listing = new()
        {
            Listing = new CodeListing { FileName = "Program.cs" },
            IsSelected = true
        };
        viewModel.CodeListings.Add(listing);
        AssertMutation(ref revision, session);
        Assert.Same(listing.Listing, Assert.Single(document.CodeListings));

        listing.IsSelected = false;
        AssertMutation(ref revision, session);
        Assert.False(document.CodeListings[0].IsSelected);

        BibliographySourceViewModel source = new()
        {
            Source = new BibliographySource { Description = "Source" },
            IsSelected = true
        };
        viewModel.BibliographySources.Add(source);
        AssertMutation(ref revision, session);
        Assert.Same(
            source.Source,
            Assert.Single(document.BibliographySources));

        source.Description = "Updated source";
        AssertMutation(ref revision, session);
        Assert.Equal(
            "Updated source",
            document.BibliographySources[0].Description);

        source.IsSelected = false;
        AssertMutation(ref revision, session);
        Assert.False(document.BibliographySources[0].IsSelected);
        Assert.Equal(0, document.Counters.SourcesCount);
    }

    [Fact]
    public void ClearingAlreadyEmptyPersistedCollections_IsNotAMutation()
    {
        DocumentSessionState session = new();
        session.StartNew();
        MainWindowViewModel viewModel = CreateViewModel(session);
        viewModel.CurrentDocument = new GostDocument();
        long revision = session.ChangeVersion;

        viewModel.ClearCodeListingsCommand.Execute(null);
        viewModel.ClearBibliographySourcesCommand.Execute(null);

        Assert.Equal(revision, session.ChangeVersion);
        Assert.False(session.IsDirty);
        Assert.Empty(viewModel.CurrentDocument.CodeListings);
        Assert.Empty(viewModel.CurrentDocument.BibliographySources);
    }

    [Fact]
    public void DerivedDisplayNumberNotification_IsNotPersistedMutation()
    {
        DocumentSessionState session = new();
        session.StartNew();
        MainWindowViewModel viewModel = CreateViewModel(session);
        viewModel.CurrentDocument = new GostDocument();
        BibliographySourceViewModel source = new()
        {
            Source = new BibliographySource { Description = "Source" }
        };
        viewModel.BibliographySources.Add(source);
        session.MarkSaved(
            Path.Combine(Path.GetTempPath(), "display-number.gost"),
            DateTimeOffset.UtcNow,
            session.ChangeVersion);
        long revision = session.ChangeVersion;

        source.RefreshDisplayNumber();

        Assert.Equal(revision, session.ChangeVersion);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void EditorMutationSignal_TracksTextFormattingImagesUndoAndRedo()
    {
        DocumentSessionState session = new();
        session.StartNew();
        GostEditor.Core.TextEngine.DocumentEditor editor = new();
        editor.DocumentChanged += (_, _) => session.RecordMutation();
        long revision = session.ChangeVersion;

        editor.InsertText("text");
        AssertMutation(ref revision, session);

        editor.SelectAll();
        editor.ApplyBold();
        AssertMutation(ref revision, session);

        editor.InsertImage(
            new CreateImageRequest(
                TestImageData.CreatePng(),
                new ImageSize(100, 50),
                "image.png"));
        AssertMutation(ref revision, session);

        editor.History.Undo();
        AssertMutation(ref revision, session);
        editor.History.Redo();
        AssertMutation(ref revision, session);
    }

    [Fact]
    public void PersistedSettingsMutation_UsesSessionMutationContract()
    {
        DocumentSessionState session = new();
        session.StartNew();
        GostDocument document = new();
        long revision = session.ChangeVersion;

        document.PageWidth = 900;
        document.MarginLeft = 100;
        document.Modules.AutoGenerateTOC = false;
        document.Modules.TOCMaxLevel = 4;
        document.Counters.TablesCount = 2;
        session.RecordMutation();

        AssertMutation(ref revision, session);
    }

    private static void AssertMutation(
        ref long previousRevision,
        DocumentSessionState session)
    {
        Assert.Equal(previousRevision + 1, session.ChangeVersion);
        Assert.True(session.IsDirty);
        previousRevision = session.ChangeVersion;
    }

    private static MainWindowViewModel CreateViewModel(
        DocumentSessionState session)
    {
        ArchiveService archive = new();
        PersistenceIoCoordinator coordinator = new();
        NoOpExportService export = new();
        return new MainWindowViewModel(
            archive,
            new DocumentExportService(export, session, coordinator),
            new NoOpCodeParserService(),
            session,
            new DocumentSaveService(archive, session, coordinator));
    }

    private sealed class NoOpExportService : IExportService
    {
        public Task ExportToDocxAsync(
            GostDocument document,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExportToDocxAsync(
            DocumentPersistenceSnapshot snapshot,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpCodeParserService : ICodeParserService
    {
        public Task<IReadOnlyList<CodeListing>> ParseDirectoryAsync(
            string directoryPath) =>
            Task.FromResult<IReadOnlyList<CodeListing>>([]);

        public CodeListing ParseFile(string filePath) => new();

        public List<Paragraph> GenerateAppendixParagraphs(
            IEnumerable<CodeListing> listings) => [];
    }
}
