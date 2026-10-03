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
    private readonly object _gate = new();
    private readonly object _flushGate = new();
    private long _version;
    private long _flushedVersion;
    private bool _disposed;
    private bool _preservationFailed;
    public Exception? LastPersistenceError { get; private set; }
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
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var e))
                return new SnippetUsage(e.UsageCount, e.LastUsed, Array.AsReadOnly((e.MergedFrom ?? []).ToArray()));
            return new SnippetUsage(0, DateTimeOffset.MinValue, []);

        }
    }

    public void RecordUse(string id)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(id, out var e))
                e = new UsageEntry { Id = id };

            e.UsageCount++;
            e.LastUsed = _clock.UtcNow;
            _entries[id] = e;
            _version++;

            // Debounce: reset 30s timer on each use
            _flushTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        }
    }

    public void MergeFrom(string newId, string oldId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
            _version++;
            _flushTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        }
    }

    public void Flush()
    {
        lock (_flushGate)
        {
            string json;
            long version;
            lock (_gate)
            {
                if (_preservationFailed) return;
                version = _version;
                if (version == _flushedVersion) return;
                json = JsonSerializer.Serialize(new UsageData { ById = _entries }, JsonOptions);
            }
            var temporary = _filePath + ".tmp";
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(temporary, json);
                if (File.Exists(_filePath)) File.Replace(temporary, _filePath, _filePath + ".bak");
                else File.Move(temporary, _filePath);
                lock (_gate) _flushedVersion = version;
                LastPersistenceError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastPersistenceError = ex;
                lock (_gate)
                    if (!_disposed) _flushTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void FlushIfDirty() => Flush();

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<UsageData>(json, JsonOptions);
            if (data?.ById is null || data.ById.Any(kv => kv.Value is null || kv.Value.UsageCount < 0))
                throw new JsonException("Invalid usage data.");
            foreach (var kv in data.ById)
                _entries[kv.Key] = kv.Value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LastPersistenceError = ex;
            // Preserve the original bytes before allowing subsequent writes.
            try { File.Copy(_filePath, _filePath + ".corrupt-" + Guid.NewGuid().ToString("N")); }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            { LastPersistenceError = copyError; _preservationFailed = true; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();
        }
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
