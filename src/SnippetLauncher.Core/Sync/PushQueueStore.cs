using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
namespace SnippetLauncher.Core.Sync;

public sealed class PushQueueStore
{
    private readonly string _filePath;
    private readonly List<PushEntry> _queue = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public PushQueueStore(string filePath) { _filePath = filePath; Load(); }
    public static PushQueueStore ForRepository(string baseDirectory, string repositoryPath, string? remoteUrl)
    {
        var identity = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant() + "\n" +
            (string.IsNullOrWhiteSpace(remoteUrl) ? "" : RemoteUrlValidator.Validate(remoteUrl));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return new PushQueueStore(Path.Combine(baseDirectory, $"push-queue-{key}.json"));
    }
    public bool HasPending => _queue.Count > 0;
    public IReadOnlyList<PushEntry> Pending => _queue;
    public void Enqueue(PushEntry entry) { if (!_queue.Any(existing => existing.CommitSha == entry.CommitSha)) _queue.Add(entry); Save(); }
    public void Dequeue(PushEntry entry) { _queue.Remove(entry); Save(); }
    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null) Directory.CreateDirectory(dir);
        var temp = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, _queue, JsonOptions); stream.Flush(true); }
            File.Move(temp, _filePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<PushEntry>>(File.ReadAllText(_filePath), JsonOptions);
            if (list is null || list.Any(entry => string.IsNullOrWhiteSpace(entry.CommitSha))) throw new JsonException();
            _queue.AddRange(list);
        }
        catch (JsonException)
        {
            File.Copy(_filePath, _filePath + ".corrupt-" + Guid.NewGuid().ToString("N"));
            Log.Warning("Push queue is corrupt; retained recovery copy, Git history will recover pending commits");
        }
    }
    public sealed class PushEntry
    {
        [JsonPropertyName("commit_sha")] public string CommitSha { get; set; } = "";
        [JsonPropertyName("queued_at")] public DateTimeOffset QueuedAt { get; set; }
        [JsonPropertyName("attempt_count")] public int AttemptCount { get; set; }
    }
}
