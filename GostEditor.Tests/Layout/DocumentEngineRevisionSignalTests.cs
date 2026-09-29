using GostEditor.Core.Services;
using GostEditor.Core.TextEngine.DOM;
using GostEditor.UI.Views;

namespace GostEditor.Tests.Layout;

[Collection(TestCollections.AvaloniaLayout)]
public sealed class DocumentEngineRevisionSignalTests
{
    [Fact]
    public void MeaningfulEditorOperations_RaiseOneContentChangeEach()
    {
        DocumentEngineView view = new();
        view.ConfigureImageService(new ImageService());
        int changes = 0;
        view.ContentChanged += () => changes++;

        view.Undo();
        Assert.Equal(0, changes);

        view.ApplyParagraphStyle(ParagraphStyle.Normal);
        Assert.Equal(1, changes);
        view.ApplyParagraphStyle(ParagraphStyle.Normal);
        Assert.Equal(1, changes);

        view.PasteText("text");
        Assert.Equal(2, changes);

        view.ApplyBold();
        Assert.Equal(3, changes);

        view.Undo();
        Assert.Equal(4, changes);

        view.Redo();
        Assert.Equal(5, changes);
    }
}
