using Avalonia.Skia;

namespace GostEditor.Tests.Layout;

internal static class TestCollections
{
    public const string AvaloniaLayout = "Avalonia layout";
}

[CollectionDefinition(TestCollections.AvaloniaLayout)]
public sealed class AvaloniaLayoutTestCollection : ICollectionFixture<AvaloniaPlatformFixture>
{
}

public sealed class AvaloniaPlatformFixture
{
    public AvaloniaPlatformFixture()
    {
        SkiaPlatform.Initialize();
    }
}
