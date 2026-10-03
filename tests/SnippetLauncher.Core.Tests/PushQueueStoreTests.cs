using FluentAssertions;
using SnippetLauncher.Core.Sync;
namespace SnippetLauncher.Core.Tests;

public sealed class PushQueueStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "queue-test-" + Guid.NewGuid().ToString("N"));
    public PushQueueStoreTests() => Directory.CreateDirectory(_root);
    [Fact]
    public void FactorySeparatesRepositoriesAndRemotes()
    {
        var a = PushQueueStore.ForRepository(_root, Path.Combine(_root, "a"), "https://example.test/a");
        a.Enqueue(new() { CommitSha = "synthetic-commit" });
        PushQueueStore.ForRepository(_root, Path.Combine(_root, "b"), "https://example.test/a").HasPending.Should().BeFalse();
        PushQueueStore.ForRepository(_root, Path.Combine(_root, "a"), "https://example.test/b").HasPending.Should().BeFalse();
        PushQueueStore.ForRepository(_root, Path.Combine(_root, "a"), "https://example.test/a").HasPending.Should().BeTrue();
    }
    [Fact]
    public void CorruptQueueIsQuarantinedAndOriginalRetained()
    {
        var path = Path.Combine(_root, "queue.json"); File.WriteAllText(path, "broken json");
        new PushQueueStore(path).HasPending.Should().BeFalse();
        Directory.GetFiles(_root, "queue.json.corrupt-*").Should().ContainSingle();
        File.ReadAllText(path).Should().Be("broken json");
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
