using FluentAssertions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Search;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly SnippetRepository _repo;
    private readonly SearchService _search;

    public SearchServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
        var statsPath = Path.Combine(_tempDir, ".local", "usage.json");
        _repo = new SnippetRepository(_tempDir, new UsageStore(statsPath, _clock), _clock);
        _search = new SearchService(_repo, _clock);
    }

    [Fact]
    public async Task Query_EmptyQuery_ReturnsAllSortedByUsage()
    {
        await _repo.LoadAllAsync();
        await _repo.SaveAsync(Snippet("faq-1", "FAQ Prijzen"));
        await _repo.SaveAsync(Snippet("faq-2", "FAQ Levering"));
        _repo.RecordUse("faq-1");
        _repo.RecordUse("faq-1");
        _repo.RecordUse("faq-2");

        var results = _search.Query("");

        results.Should().HaveCount(2);
        results[0].Snippet.Id.Should().Be("faq-1"); // more uses = higher score
    }

    [Fact]
    public async Task Query_TitleMatch_ReturnsRelevantSnippet()
    {
        await _repo.LoadAllAsync();
        await _repo.SaveAsync(Snippet("pricing", "Pricing information"));
        await _repo.SaveAsync(Snippet("shipping", "Shipping details"));

        var results = _search.Query("pricing");

        results.Should().NotBeEmpty();
        results[0].Snippet.Id.Should().Be("pricing");
    }

    [Fact]
    public async Task Query_NoMatch_ReturnsEmptyList()
    {
        await _repo.LoadAllAsync();
        await _repo.SaveAsync(Snippet("faq", "Frequently asked"));

        var results = _search.Query("xyznonexistent");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_RespectsLimit()
    {
        await _repo.LoadAllAsync();
        for (var i = 0; i < 15; i++)
            await _repo.SaveAsync(Snippet($"item-{i}", $"Item number {i}"));

        var results = _search.Query("item", limit: 5);

        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task Query_TagMatch_ScoresHigherThanBodyOnly()
    {
        await _repo.LoadAllAsync();
        // Tagged snippet: "waba" in tags
        await _repo.SaveAsync(new Snippet("tagged", "Snelle reactie", ["waba", "quick"], "Some body text", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        // Body-only snippet: "waba" only in body
        await _repo.SaveAsync(new Snippet("body-only", "Another snippet", [], "Contains waba somewhere in body", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var results = _search.Query("waba");

        results.Should().NotBeEmpty();
        var taggedScore = results.FirstOrDefault(r => r.Snippet.Id == "tagged")?.Score ?? 0;
        var bodyScore = results.FirstOrDefault(r => r.Snippet.Id == "body-only")?.Score ?? 0;
        taggedScore.Should().BeGreaterThan(bodyScore);
    }

    private static Snippet Snippet(string id, string title) => new(
        id, title, [], $"Body for {title}", [],
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
