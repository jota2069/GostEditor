namespace GostEditor.Core.Serialization;

internal sealed class GostImageReadBudget
{
    private readonly int _maximumCount;
    private readonly long _maximumBytes;
    private int _count;
    private long _bytes;

    public GostImageReadBudget()
        : this(
            GostArchiveLimits.MaxImages,
            GostArchiveLimits.MaxTotalImageBytes)
    {
    }

    internal GostImageReadBudget(int maximumCount, long maximumBytes)
    {
        if (maximumCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        if (maximumBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        _maximumCount = maximumCount;
        _maximumBytes = maximumBytes;
    }

    public void Accept(ReadOnlyMemory<byte> data)
    {
        if (data.Length > GostArchiveLimits.MaxImageBytes)
        {
            throw new InvalidDataException(
                "Размер одного изображения превышает допустимый предел " +
                $"{GostArchiveLimits.MaxImageBytes} байт.");
        }

        if (_count >= _maximumCount)
        {
            throw new InvalidDataException(
                $"Документ содержит больше {_maximumCount} изображений.");
        }

        if (data.Length > _maximumBytes - _bytes)
        {
            throw new InvalidDataException(
                "Фактический суммарный размер изображений превышает " +
                $"допустимый предел {_maximumBytes} байт.");
        }

        _count++;
        _bytes += data.Length;
    }

    public long GetMaximumNextPayloadBytes()
    {
        if (_count >= _maximumCount)
        {
            throw new InvalidDataException(
                $"Документ содержит больше {_maximumCount} изображений.");
        }

        return Math.Min(
            GostArchiveLimits.MaxImageBytes,
            _maximumBytes - _bytes);
    }
}
