using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippetLauncher.Core.Sync;

/// <summary>
/// Persists pending push commits to push-queue.json so offline restarts don't lose queued work.
/// </summary>
public sealed class PushQueueStore
{
    private readonly string _filePath;
    private readonly List<PushEntry> _queue = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PushQueueStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public bool HasPending => _queue.Count > 0;
    public IReadOnlyList<PushEntry> Pending => _queue;

    public void Enqueue(PushEntry entry)
    {
        _queue.Add(entry);
        Save();
    }

    public void Dequeue(PushEntry entry)
    {
        _queue.Remove(entry);
        Save();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_queue, JsonOptions));
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<PushEntry>>(json, JsonOptions);
            if (list is not null) _queue.AddRange(list);
        }
        catch { }
    }

    public sealed class PushEntry
    {
        [JsonPropertyName("commit_sha")]
        public string CommitSha { get; set; } = "";

        [JsonPropertyName("queued_at")]
        public DateTimeOffset QueuedAt { get; set; }

        [JsonPropertyName("attempt_count")]
        public int AttemptCount { get; set; }
    }
}
