namespace GostEditor.Tests.Infrastructure;

internal sealed class ManualUtcTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private DateTimeOffset _utcNow;

    public ManualUtcTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow.ToUniversalTime();
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        lock (_sync)
        {
            _utcNow = _utcNow.Add(elapsed);
        }
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            _utcNow = utcNow.ToUniversalTime();
        }
    }
}
