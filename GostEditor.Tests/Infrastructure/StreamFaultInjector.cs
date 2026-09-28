using System.Runtime.ExceptionServices;

namespace GostEditor.Tests.Infrastructure;

internal static class StreamFaultInjector
{
    public static async Task WriteThenThrowAsync(
        Stream stream,
        ReadOnlyMemory<byte> partialContents,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(exception);

        await stream.WriteAsync(partialContents, cancellationToken);
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
