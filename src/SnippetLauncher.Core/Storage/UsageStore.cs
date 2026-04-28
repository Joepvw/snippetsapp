using System.Text.Json;
using System.Text.Json.Serialization;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.Core.Storage;

/// <summary>
/// Persists per-machine usage stats to a local JSON file (never committed to Git).
/// Stats are stored inline in SnippetRepository and flushed here on a debounced timer.
/// </summary>
public sealed class UsageStore : IDisposable
{
    private readonly string _filePath;
    private readonly IClock _clock;
    private readonly Dictionary<string, UsageEntry> _entries = [];
    private readonly Timer _flushTimer;
    private bool _dirty;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UsageStore(string filePath, IClock clock)
    {
        _filePath = filePath;
        _clock = clock;
        _flushTimer = new Timer(_ => FlushIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    public SnippetUsage Get(string id)
    {
        if (_entries.TryGetValue(id, out var e))
            return new SnippetUsage(e.UsageCount, e.LastUsed, e.MergedFrom ?? []);
        return new SnippetUsage(0, DateTimeOffset.MinValue, []);
    }

    public void RecordUse(string id)
    {
        if (!_entries.TryGetValue(id, out var e))
            e = new UsageEntry { Id = id };

        e.UsageCount++;
        e.LastUsed = _clock.UtcNow;
        _entries[id] = e;
        _dirty = true;

        // Debounce: reset 30s timer on each use
        _flushTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    public void MergeFrom(string newId, string oldId)
    {
        var entry = _entries.TryGetValue(newId, out var e) ? e : new UsageEntry { Id = newId };
        entry.MergedFrom ??= [];
        if (!entry.MergedFrom.Contains(oldId))
            entry.MergedFrom.Add(oldId);

        if (_entries.TryGetValue(oldId, out var old))
        {
            entry.UsageCount += old.UsageCount;
            if (old.LastUsed > entry.LastUsed)
                entry.LastUsed = old.LastUsed;
            _entries.Remove(oldId);
        }

        _entries[newId] = entry;
        _dirty = true;
        _flushTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    public void Flush()
    {
        if (!_dirty) return;
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        var data = new UsageData { ById = _entries };
        File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
        _dirty = false;
    }

    private void FlushIfDirty() => Flush();

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<UsageData>(json, JsonOptions);
            if (data?.ById is null) return;
            foreach (var kv in data.ById)
                _entries[kv.Key] = kv.Value;
        }
        catch
        {
            // Corrupt stats file — start fresh, don't crash.
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }

    private sealed class UsageData
    {
        [JsonPropertyName("by_id")]
        public Dictionary<string, UsageEntry>? ById { get; set; }
    }

    private sealed class UsageEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("usage_count")]
        public int UsageCount { get; set; }

        [JsonPropertyName("last_used")]
        public DateTimeOffset LastUsed { get; set; }

        [JsonPropertyName("merged_from")]
        public List<string>? MergedFrom { get; set; }
    }
}
