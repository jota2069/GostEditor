using GostEditor.Core.Serialization;

namespace GostEditor.Tests.Serialization;

public sealed class GostImageReadBudgetTests
{
    [Fact]
    public void Accept_UsesActualPayloadLengthForAggregateBudget()
    {
        GostImageReadBudget budget = new(maximumCount: 2, maximumBytes: 5);
        budget.Accept(new byte[] { 1, 2, 3 });

        Assert.Throws<InvalidDataException>(() =>
            budget.Accept(new byte[] { 4, 5, 6 }));
    }

    [Fact]
    public void Accept_CountsZeroLengthMaterializedImages()
    {
        GostImageReadBudget budget = new(maximumCount: 1, maximumBytes: 5);
        budget.Accept(ReadOnlyMemory<byte>.Empty);

        Assert.Throws<InvalidDataException>(() =>
            budget.Accept(ReadOnlyMemory<byte>.Empty));
    }
}
