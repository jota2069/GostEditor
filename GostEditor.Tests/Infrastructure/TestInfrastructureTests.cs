namespace GostEditor.Tests.Infrastructure;

public sealed class TestInfrastructureTests
{
    [Fact]
    public async Task ManualAsyncGate_BlocksUntilExplicitRelease()
    {
        ManualAsyncGate gate = new();
        using CancellationTokenSource timeout =
            new(TimeSpan.FromSeconds(5));

        Task operation = gate.SignalAndWaitAsync(timeout.Token);
        await gate.WaitUntilReachedAsync(timeout.Token);

        try
        {
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            gate.Release();
            gate.Release();
        }

        await operation;
    }

    [Fact]
    public async Task ManualAsyncGate_RejectsSecondSignal()
    {
        ManualAsyncGate gate = new();
        gate.Release();
        await gate.SignalAndWaitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.SignalAndWaitAsync());
    }

    [Fact]
    public void ManualUtcTimeProvider_ReturnsAndAdvancesExactUtcTime()
    {
        DateTimeOffset initial = new(
            2026,
            9,
            28,
            10,
            30,
            0,
            TimeSpan.FromHours(3));
        ManualUtcTimeProvider clock = new(initial);

        Assert.Equal(initial.ToUniversalTime(), clock.GetUtcNow());

        clock.Advance(TimeSpan.FromMinutes(15));

        Assert.Equal(
            initial.ToUniversalTime().AddMinutes(15),
            clock.GetUtcNow());
    }

    [Fact]
    public void ManualUtcTimeProvider_RejectsBackwardAdvanceWithoutChangingTime()
    {
        DateTimeOffset initial = new(
            2026,
            9,
            28,
            10,
            30,
            0,
            TimeSpan.Zero);
        ManualUtcTimeProvider clock = new(initial);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.Advance(TimeSpan.FromTicks(-1)));
        Assert.Equal(initial, clock.GetUtcNow());
    }

    [Fact]
    public async Task StreamFaultInjector_WritesPartialContentThenThrowsExactFailure()
    {
        await using MemoryStream stream = new();
        IOException expected = new("injected write failure");

        IOException actual = await Assert.ThrowsAsync<IOException>(() =>
            StreamFaultInjector.WriteThenThrowAsync(
                stream,
                "partial"u8.ToArray(),
                expected));

        Assert.Same(expected, actual);
        Assert.Equal("partial"u8.ToArray(), stream.ToArray());
    }

    [Fact]
    public void TestTemporaryDirectory_DisposeDeletesOwnedDirectory()
    {
        TestTemporaryDirectory temporaryDirectory = new();
        string directoryPath = temporaryDirectory.DirectoryPath;
        try
        {
            string filePath =
                temporaryDirectory.GetPath("nested", "file.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "test");
        }
        finally
        {
            temporaryDirectory.Dispose();
        }

        temporaryDirectory.Dispose();

        Assert.False(Directory.Exists(directoryPath));
    }

    [Fact]
    public void TestTemporaryDirectory_RejectsPathOutsideOwnedDirectory()
    {
        using TestTemporaryDirectory temporaryDirectory = new();

        Assert.Throws<ArgumentException>(() =>
            temporaryDirectory.GetPath("..", "outside.txt"));
        Assert.Throws<ArgumentException>(() =>
            temporaryDirectory.GetPath(Path.GetTempPath()));
    }

    [Fact]
    public void TestTemporaryDirectory_GetPathAfterDisposeThrows()
    {
        TestTemporaryDirectory temporaryDirectory = new();
        temporaryDirectory.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            temporaryDirectory.GetPath("file.txt"));
    }

    [Fact]
    public void TestTemporaryDirectory_WhenCleanupFails_AllowsRetry()
    {
        int deleteAttempts = 0;
        TestTemporaryDirectory temporaryDirectory = new(path =>
        {
            if (Interlocked.Increment(ref deleteAttempts) == 1)
            {
                throw new IOException("injected cleanup failure");
            }

            Directory.Delete(path, recursive: true);
        });
        string directoryPath = temporaryDirectory.DirectoryPath;

        try
        {
            Assert.Throws<IOException>(() => temporaryDirectory.Dispose());
            Assert.True(Directory.Exists(directoryPath));

            temporaryDirectory.Dispose();

            Assert.Equal(2, deleteAttempts);
            Assert.False(Directory.Exists(directoryPath));
            Assert.Throws<ObjectDisposedException>(() =>
                temporaryDirectory.GetPath("file.txt"));
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, recursive: true);
            }
        }
    }

    [Fact]
    public void TestTemporaryDirectory_InstancesUseDifferentDirectories()
    {
        using TestTemporaryDirectory first = new();
        using TestTemporaryDirectory second = new();

        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
    }
}
