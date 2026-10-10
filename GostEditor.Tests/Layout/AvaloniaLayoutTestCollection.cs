using System.Reflection;
using Avalonia.Skia;

namespace GostEditor.Tests.Layout;

internal static class TestCollections
{
    public const string AvaloniaLayout = "Avalonia layout";
}

[CollectionDefinition(
    TestCollections.AvaloniaLayout,
    DisableParallelization = true)]
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

public sealed class AvaloniaLayoutIsolationTests
{
    [Fact]
    public void AvaloniaLayoutCollection_IsExclusiveAndContainsAllUiLayoutTests()
    {
        CollectionDefinitionAttribute definition =
            Assert.IsType<CollectionDefinitionAttribute>(
                Attribute.GetCustomAttribute(
                    typeof(AvaloniaLayoutTestCollection),
                    typeof(CollectionDefinitionAttribute)));

        Assert.True(definition.DisableParallelization);
        AssertUsesAvaloniaLayoutCollection(typeof(ImageLayoutTests));
        AssertUsesAvaloniaLayoutCollection(typeof(IncrementalPageLayoutTests));
        AssertUsesAvaloniaLayoutCollection(
            typeof(DocumentEngineRevisionSignalTests));
        AssertUsesAvaloniaLayoutCollection(
            typeof(EditorFocusHitTestingTests));
    }

    private static void AssertUsesAvaloniaLayoutCollection(Type testClass)
    {
        CustomAttributeData collection = Assert.Single(
            testClass.CustomAttributes,
            attribute =>
                attribute.AttributeType == typeof(CollectionAttribute));
        CustomAttributeTypedArgument collectionName = Assert.Single(
            collection.ConstructorArguments);

        Assert.Equal(TestCollections.AvaloniaLayout, collectionName.Value);
    }
}
