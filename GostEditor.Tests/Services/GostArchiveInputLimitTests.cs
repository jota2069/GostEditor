using GostEditor.UI.Views;

namespace GostEditor.Tests.Services;

public sealed class GostArchiveInputLimitTests
{
    [Fact]
    public async Task BufferArchive_NonSeekableStreamStopsAtConfiguredLimit()
    {
        await using NonSeekableReadStream source = new(
            Enumerable.Range(0, 33).Select(value => (byte)value).ToArray());

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                MainWindow.BufferArchiveAsync(source, maximumLength: 32));

        Assert.Contains("превышает допустимый размер", exception.Message);
    }

    [Fact]
    public async Task BufferArchive_ExactLimitReturnsRewoundBuffer()
    {
        byte[] expected = Enumerable.Range(0, 32)
            .Select(value => (byte)value)
            .ToArray();
        await using NonSeekableReadStream source = new(expected);

        await using MemoryStream actual =
            await MainWindow.BufferArchiveAsync(source, maximumLength: 32);

        Assert.Equal(0, actual.Position);
        Assert.Equal(expected, actual.ToArray());
    }

    private sealed class NonSeekableReadStream : MemoryStream
    {
        internal NonSeekableReadStream(byte[] buffer)
            : base(buffer, writable: false)
        {
        }

        public override bool CanSeek => false;
    }
}
