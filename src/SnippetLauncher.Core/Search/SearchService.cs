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

    }

    private readonly object _indexLock = new();
    private IReadOnlyList<Snippet>? _source;
    private Prepared[] _index = [];
    private sealed record Prepared(Snippet Snippet, string Title, string Tags, string Body);

    public IReadOnlyList<ScoredSnippet> Query(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        Prepared[] index;
        var snapshot = _repository.GetAll();
        lock (_indexLock)
        {
            if (!ReferenceEquals(snapshot, _source))
            {
                _index = snapshot.Select(s => new Prepared(s, s.Title.ToLowerInvariant(),
                    string.Join(" ", s.Tags).ToLowerInvariant(),
                    s.Body[..Math.Min(500, s.Body.Length)].ToLowerInvariant())).ToArray();
                _source = snapshot;
            }
            index = _index;
        }
        var q = query.Trim().ToLowerInvariant();
        var results = new List<ScoredSnippet>(index.Length);
        foreach (var item in index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = UsageScore(item.Snippet.Id);
            if (q.Length > 0)
                score += 0.006 * Fuzz.PartialRatio(q, item.Title)
                    + (item.Tags.Length > 0 ? 0.003 * Fuzz.PartialRatio(q, item.Tags) : 0)
                    + 0.001 * Fuzz.PartialRatio(q, item.Body);
            if (q.Length == 0 || score > 0.3) results.Add(new ScoredSnippet(item.Snippet, score));
        }
        return results.OrderByDescending(x => x.Score).Take(limit).ToArray();
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
