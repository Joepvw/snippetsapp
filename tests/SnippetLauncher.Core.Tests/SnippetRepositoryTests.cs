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

    private static Snippet MakeSnippet(string id, string title) => new(
        id, title, ["test"], $"Body of {title}", [],
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
