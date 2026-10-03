using FluentAssertions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Tests;

public sealed class SnippetRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly SnippetRepository _repo;
    private readonly UsageStore _usage;

    public SnippetRepositoryTests()
    {
        Directory.CreateDirectory(_tempDir);
        var statsPath = Path.Combine(_tempDir, ".local", "usage.json");
        _usage = new UsageStore(statsPath, _clock);
        _repo = new SnippetRepository(_tempDir, _usage, _clock);
    }

    [Fact]
    public async Task LoadAll_EmptyDir_ReturnsNoSnippets()
    {
        await _repo.LoadAllAsync();
        _repo.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndGet_RoundTrip()
    {
        await _repo.LoadAllAsync();
        var snippet = MakeSnippet("hello-world", "Hello World");

        var saved = await _repo.SaveAsync(snippet);

        saved.Id.Should().Be("hello-world");
        saved.Title.Should().Be("Hello World");
        _repo.Get("hello-world").Should().NotBeNull();
        File.Exists(Path.Combine(_tempDir, "hello-world.md")).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_RemovesFromMemoryAndDisk()
    {
        await _repo.LoadAllAsync();
        await _repo.SaveAsync(MakeSnippet("to-delete", "To Delete"));

        await _repo.DeleteAsync("to-delete");

        _repo.Get("to-delete").Should().BeNull();
        File.Exists(Path.Combine(_tempDir, "to-delete.md")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAll_ParsesMalformedFile_TracksInMalformedList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "bad.md"), "---\n: : bad yaml\n---\nbody");

        await _repo.LoadAllAsync();

        _repo.MalformedSnippetPaths.Should().ContainSingle(p => p.Contains("bad.md"));
        _repo.Get("bad").Should().BeNull();
    }

    [Fact]
    public async Task Save_DoesNotTriggerEchoReload()
    {
        await _repo.LoadAllAsync();
        var changed = new List<string>();
        _repo.SnippetChanged += (_, e) => changed.Add(e.Snippet.Id);

        await _repo.SaveAsync(MakeSnippet("echo-test", "Echo Test"));

        // Wait for potential FSW echo (300ms debounce + margin)
        await Task.Delay(700);

        // Should appear exactly once (from the save itself, not from FSW echo)
        changed.Should().ContainSingle(id => id == "echo-test");
    }

    [Fact]
    public async Task Stats_RecordUse_IncrementsCount()
    {
        await _repo.LoadAllAsync();
        await _repo.SaveAsync(MakeSnippet("stat-test", "Stats Test"));

        _repo.RecordUse("stat-test");
        _repo.RecordUse("stat-test");

        _repo.GetUsage("stat-test").UsageCount.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public async Task BulkLoad_PublishesOnceWithoutLocalMutations(int count)
    {
        for (var i = 0; i < count; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"{i}.md"), SnippetSerializer.Serialize(MakeSnippet($"{i}", "Title")));
        var batches = 0;
        var local = 0;
        _repo.LibraryChanged += (_, _) => batches++;
        _repo.SnippetChanged += (_, _) => local++;
        await _repo.LoadAllAsync();
        _repo.GetAll().Should().HaveCount(count);
        batches.Should().Be(1);
        local.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_DrainsAcceptedSaveAndRejectsNewSave()
    {
        var save = _repo.SaveAsync(MakeSnippet("shutdown", "Shutdown"));
        await _repo.DisposeAsync();
        (await save).Id.Should().Be("shutdown");
        var rejected = () => _repo.SaveAsync(MakeSnippet("later", "Later"));
        await rejected.Should().ThrowAsync<Exception>();
        _usage.RecordUse("host-owned");
    }

    [Fact]
    public async Task Snapshot_RemainsStableDuringWrites()
    {
        await _repo.SaveAsync(MakeSnippet("one", "One"));
        var snapshot = _repo.GetAll();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => _repo.SaveAsync(MakeSnippet($"new-{i}", "New"))));
        snapshot.Should().ContainSingle();
        _repo.GetAll().Should().HaveCount(101);
    }

    [Fact]
    public async Task FileCreatedAtScanCompletion_IsObservedWithoutLocalMutation()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var local = 0;
        var created = false;
        _repo.SnippetChanged += (_, _) => Interlocked.Increment(ref local);
        _repo.LibraryChanged += (_, _) =>
        {
            if (!created)
            {
                created = true;
                File.WriteAllText(Path.Combine(_tempDir, "clone.md"), SnippetSerializer.Serialize(MakeSnippet("clone", "Clone")));
            }
            else if (_repo.Get("clone") is not null) observed.TrySetResult();
        };
        await _repo.LoadAllAsync();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        local.Should().Be(0);
    }

    [Fact]
    public async Task UnreadableExternalFile_DoesNotPreventNextSave()
    {
        await _repo.LoadAllAsync();
        var path = Path.Combine(_tempDir, "locked.md");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await Task.Delay(700);
            await _repo.SaveAsync(MakeSnippet("healthy", "Healthy")).WaitAsync(TimeSpan.FromSeconds(5));
        }
        _repo.Get("healthy").Should().NotBeNull();
    }

    [Fact]
    public async Task Reload_AfterDirectoryReplacement_WatchesActiveDirectory()
    {
        await _repo.LoadAllAsync();
        var preserved = _tempDir + ".preserved";
        try
        {
            Directory.Move(_tempDir, preserved);
            Directory.CreateDirectory(_tempDir);
            await _repo.LoadAllAsync();
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _repo.LibraryChanged += (_, _) =>
            {
                if (_repo.Get("replacement") is not null) observed.TrySetResult();
            };
            File.WriteAllText(Path.Combine(_tempDir, "replacement.md"),
                SnippetSerializer.Serialize(MakeSnippet("replacement", "Replacement")));
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _repo.Get("replacement").Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(preserved)) Directory.Delete(preserved, true);
        }
    }

    private static Snippet MakeSnippet(string id, string title) => new(
        id, title, ["test"], $"Body of {title}", [],
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        _repo.Dispose();
        _usage.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
