using FuzzySharp;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Search;

/// <summary>
/// In-memory fuzzy search over snippets.
/// Scoring: 0.6 * title + 0.3 * tags + 0.1 * body-preview + recency + frequency boosts.
/// Empty query returns snippets sorted by recent/frequent use.
/// </summary>
public sealed class SearchService
{
    private readonly SnippetRepository _repository;
    private readonly IClock _clock;

    public SearchService(SnippetRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;

        _repository.SnippetChanged += (_, _) => { /* index is live via GetAll() */ };
        _repository.SnippetRemoved += (_, _) => { };
    }

    public IReadOnlyList<ScoredSnippet> Query(string query, int limit = 8)
    {
        var snippets = _repository.GetAll();
        if (snippets.Count == 0) return [];

        if (string.IsNullOrWhiteSpace(query))
            return snippets
                .Select(s => new ScoredSnippet(s, UsageScore(s.Id)))
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .ToList();

        var q = query.Trim().ToLowerInvariant();

        return snippets
            .Select(s => new ScoredSnippet(s, ComputeScore(s, q)))
            .Where(x => x.Score > 0.3)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();
    }

    private double ComputeScore(Snippet snippet, string query)
    {
        var titleScore  = Fuzz.PartialRatio(query, snippet.Title.ToLowerInvariant()) / 100.0;
        var tagsStr     = string.Join(" ", snippet.Tags).ToLowerInvariant();
        var tagsScore   = tagsStr.Length > 0 ? Fuzz.PartialRatio(query, tagsStr) / 100.0 : 0.0;
        var bodyPreview = snippet.Body.Length > 500 ? snippet.Body[..500] : snippet.Body;
        var bodyScore   = Fuzz.PartialRatio(query, bodyPreview.ToLowerInvariant()) / 100.0;

        var fuzzy = 0.6 * titleScore + 0.3 * tagsScore + 0.1 * bodyScore;
        return fuzzy + UsageScore(snippet.Id);
    }

    private double UsageScore(string id)
    {
        var usage = _repository.GetUsage(id);

        // Recency boost: up to 0.2, decays by half every 7 days
        var daysSince = (_clock.UtcNow - usage.LastUsed).TotalDays;
        var recency = usage.LastUsed == DateTimeOffset.MinValue
            ? 0.0
            : 0.2 * Math.Pow(0.5, daysSince / 7.0);

        // Frequency boost: up to 0.15, log curve
        var freq = usage.UsageCount > 0
            ? 0.15 * Math.Min(1.0, Math.Log10(usage.UsageCount + 1) / 2.0)
            : 0.0;

        return recency + freq;
    }
}
