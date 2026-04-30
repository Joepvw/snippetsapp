using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.Core.Storage;

public sealed class SnippetChangedEventArgs(Snippet snippet) : EventArgs
{
    public Snippet Snippet { get; } = snippet;
}

public sealed class SnippetRemovedEventArgs(string id) : EventArgs
{
    public string Id { get; } = id;
}

/// <summary>
/// Single source of truth for snippets. All mutations go through a Channel so
/// the in-memory dictionary has exactly one writer. FileSystemWatcher events
/// are echo-suppressed to avoid reloading files we just wrote ourselves.
/// </summary>
public sealed class SnippetRepository : IDisposable
{
    private readonly string _snippetsDir;
    private readonly UsageStore _usage;
    private readonly IClock _clock;

    private readonly Dictionary<string, Snippet> _snippets = [];
    private readonly Channel<RepoOp> _channel = Channel.CreateUnbounded<RepoOp>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, (string Hash, DateTimeOffset Expiry)> _expectedWrites = new();
    private readonly Task _processorTask;
    private readonly CancellationTokenSource _cts = new();

    private FileSystemWatcher? _watcher;

    public event EventHandler<SnippetChangedEventArgs>? SnippetChanged;
    public event EventHandler<SnippetRemovedEventArgs>? SnippetRemoved;

    // Snippets that failed to parse — exposed so the editor can show a "fix" action.
    public IReadOnlyList<string> MalformedSnippetPaths => _malformed;
    private readonly List<string> _malformed = [];

    public SnippetRepository(string snippetsDir, UsageStore usage, IClock clock)
    {
        _snippetsDir = snippetsDir;
        _usage = usage;
        _clock = clock;
        _processorTask = Task.Run(ProcessChannelAsync);
    }

    public async Task LoadAllAsync()
    {
        var tcs = new TaskCompletionSource();
        await _channel.Writer.WriteAsync(new LoadAllOp(tcs));
        await tcs.Task;
        StartWatcher();
    }

    public IReadOnlyList<Snippet> GetAll() => [.. _snippets.Values];

    public Snippet? Get(string id) => _snippets.GetValueOrDefault(id);

    public async Task<Snippet> SaveAsync(Snippet snippet)
    {
        var tcs = new TaskCompletionSource<Snippet>();
        await _channel.Writer.WriteAsync(new SaveOp(snippet, tcs));
        return await tcs.Task;
    }

    public async Task DeleteAsync(string id)
    {
        var tcs = new TaskCompletionSource();
        await _channel.Writer.WriteAsync(new DeleteOp(id, tcs));
        await tcs.Task;
    }

    public void RecordUse(string id) => _usage.RecordUse(id);

    public SnippetUsage GetUsage(string id) => _usage.Get(id);

    // ── Channel processor (single writer) ───────────────────────────────────

    private async Task ProcessChannelAsync()
    {
        await foreach (var op in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            try
            {
                switch (op)
                {
                    case LoadAllOp load:
                        ExecuteLoadAll();
                        load.Completion.SetResult();
                        break;

                    case SaveOp save:
                        var saved = ExecuteSave(save.Snippet);
                        save.Completion.SetResult(saved);
                        break;

                    case DeleteOp del:
                        ExecuteDelete(del.Id);
                        del.Completion.SetResult();
                        break;

                    case ExternalChangeOp ext:
                        ExecuteExternalChange(ext.Path);
                        break;
                }
            }
            catch (Exception ex) when (op is SaveOp s2)
            {
                s2.Completion.SetException(ex);
            }
            catch (Exception ex) when (op is DeleteOp d2)
            {
                d2.Completion.SetException(ex);
            }
            catch (Exception ex) when (op is LoadAllOp l2)
            {
                l2.Completion.SetException(ex);
            }
        }
    }

    private void ExecuteLoadAll()
    {
        _snippets.Clear();
        _malformed.Clear();

        if (!Directory.Exists(_snippetsDir))
            Directory.CreateDirectory(_snippetsDir);

        foreach (var file in Directory.EnumerateFiles(_snippetsDir, "*.md"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            TryParseAndStore(id, file);
        }
    }

    private Snippet ExecuteSave(Snippet snippet)
    {
        Directory.CreateDirectory(_snippetsDir);

        var updated = snippet with { Updated = _clock.UtcNow };
        var content = SnippetSerializer.Serialize(updated);
        var hash = ComputeHash(content);
        var filePath = SnippetPath(updated.Id);
        var tmpPath = filePath + ".tmp";

        // Register expected write before touching disk
        _expectedWrites[filePath] = (hash, _clock.UtcNow.AddSeconds(2));

        File.WriteAllText(tmpPath, content, Encoding.UTF8);
        File.Move(tmpPath, filePath, overwrite: true);

        _snippets[updated.Id] = updated;
        SnippetChanged?.Invoke(this, new SnippetChangedEventArgs(updated));
        return updated;
    }

    private void ExecuteDelete(string id)
    {
        var filePath = SnippetPath(id);
        if (File.Exists(filePath))
        {
            _expectedWrites[filePath] = ("__deleted__", _clock.UtcNow.AddSeconds(2));
            File.Delete(filePath);
        }
        _snippets.Remove(id);
        SnippetRemoved?.Invoke(this, new SnippetRemovedEventArgs(id));
    }

    private void ExecuteExternalChange(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var removedId = Path.GetFileNameWithoutExtension(filePath);
            if (_snippets.Remove(removedId))
                SnippetRemoved?.Invoke(this, new SnippetRemovedEventArgs(removedId));
            return;
        }

        var content = TryReadFile(filePath);
        if (content is null) return;

        var hash = ComputeHash(content);

        // Echo-suppression: skip if this is a write we made ourselves
        if (_expectedWrites.TryGetValue(filePath, out var expected))
        {
            if (expected.Hash == hash && expected.Expiry > _clock.UtcNow)
            {
                _expectedWrites.TryRemove(filePath, out _);
                return;
            }
            _expectedWrites.TryRemove(filePath, out _);
        }

        var id = Path.GetFileNameWithoutExtension(filePath);
        TryParseAndStore(id, filePath);
    }

    private void TryParseAndStore(string id, string filePath)
    {
        var content = TryReadFile(filePath);
        if (content is null) return;

        try
        {
            var snippet = SnippetSerializer.Deserialize(id, content);
            _snippets[id] = snippet;
            SnippetChanged?.Invoke(this, new SnippetChangedEventArgs(snippet));
            _malformed.Remove(filePath);
        }
        catch
        {
            _malformed.Add(filePath);
        }
    }

    private static string? TryReadFile(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) { Thread.Sleep(50); }
        }
        return null;
    }

    // ── FileSystemWatcher ────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_snippetsDir, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += OnWatcherEvent;
        _watcher.Created += OnWatcherEvent;
        _watcher.Deleted += OnWatcherEvent;
        _watcher.Renamed += (_, e) =>
        {
            OnWatcherEvent(null, new FileSystemEventArgs(WatcherChangeTypes.Deleted, _snippetsDir, e.OldName));
            OnWatcherEvent(null, new FileSystemEventArgs(WatcherChangeTypes.Created, _snippetsDir, e.Name));
        };
    }

    private void OnWatcherEvent(object? sender, FileSystemEventArgs e)
    {
        // Debounce per path: cancel previous timer and start a new 300ms one
        if (_debounceTokens.TryGetValue(e.FullPath, out var prev))
        {
            prev.Cancel();
            prev.Dispose();
        }
        var cts = new CancellationTokenSource();
        _debounceTokens[e.FullPath] = cts;

        Task.Delay(300, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _debounceTokens.TryRemove(e.FullPath, out _);
            _channel.Writer.TryWrite(new ExternalChangeOp(e.FullPath));
        });
    }

    private string SnippetPath(string id) => Path.Combine(_snippetsDir, $"{id}.md");

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        try { _channel.Writer.Complete(); } catch { }
        _watcher?.Dispose();
        _usage.Dispose();
        _cts.Dispose();
    }

    // ── Channel operation types ──────────────────────────────────────────────

    private abstract record RepoOp;
    private sealed record LoadAllOp(TaskCompletionSource Completion) : RepoOp;
    private sealed record SaveOp(Snippet Snippet, TaskCompletionSource<Snippet> Completion) : RepoOp;
    private sealed record DeleteOp(string Id, TaskCompletionSource Completion) : RepoOp;
    private sealed record ExternalChangeOp(string Path) : RepoOp;
}
