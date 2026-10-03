using FluentAssertions;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Tests;

public sealed class UsageStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task ConcurrentRecordsAndFlushes_PersistEveryUse()
    {
        var path = Path.Combine(_directory, "usage.json");
        using (var store = new UsageStore(path, _clock))
        {
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() =>
            {
                store.RecordUse("one");
                if (i % 20 == 0) store.Flush();
            })));
            store.Flush();
        }
        using var reloaded = new UsageStore(path, _clock);
        reloaded.Get("one").UsageCount.Should().Be(1000);
    }

    [Fact]
    public void FailedFlush_RetainsPendingDataForRetry()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "blocked");
        var path = Path.Combine(blocker, "usage.json");
        using var store = new UsageStore(path, _clock);
        store.RecordUse("one");
        store.Flush();
        store.LastPersistenceError.Should().NotBeNull();
        File.Delete(blocker);
        store.Flush();
        store.LastPersistenceError.Should().BeNull();
        using var reloaded = new UsageStore(path, _clock);
        reloaded.Get("one").UsageCount.Should().Be(1);
    }

    [Fact]
    public void CorruptFile_IsPreservedBeforeWritingNewStats()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "usage.json");
        File.WriteAllText(path, "broken json");
        using var store = new UsageStore(path, _clock);
        store.LastPersistenceError.Should().NotBeNull();
        store.RecordUse("one");
        store.Flush();
        var recovery = Directory.GetFiles(_directory, "usage.json.corrupt-*");
        recovery.Should().ContainSingle();
        File.ReadAllText(recovery[0]).Should().Be("broken json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
