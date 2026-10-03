using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Infrastructure;

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
public sealed class SnippetRepository : IDisposable, IAsyncDisposable
{
    private readonly string _snippetsDir;
    private readonly UsageStore _usage;
    private readonly IClock _clock;
    public LibraryFileGate FileGate { get; } = new();

    private readonly Dictionary<string, Snippet> _snippets = [];
    private readonly Channel<RepoOp> _channel = Channel.CreateUnbounded<RepoOp>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, (string Hash, DateTimeOffset Expiry)> _expectedWrites = new();
    private readonly Task _processorTask;
    private IReadOnlyList<Snippet> _snapshot = Array.AsReadOnly(Array.Empty<Snippet>());
    private IReadOnlyList<string> _badSnapshot = Array.AsReadOnly(Array.Empty<string>());
    public event EventHandler? LibraryChanged;
    public Exception? LastStorageError { get; private set; }

    private FileSystemWatcher? _watcher;

    public event EventHandler<SnippetChangedEventArgs>? SnippetChanged;
    public event EventHandler<SnippetRemovedEventArgs>? SnippetRemoved;

    // Snippets that failed to parse — exposed so the editor can show a "fix" action.
    public IReadOnlyList<string> MalformedSnippetPaths => Volatile.Read(ref _badSnapshot);
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
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new LoadAllOp(tcs));
        await tcs.Task;

    }

    public IReadOnlyList<Snippet> GetAll() => Volatile.Read(ref _snapshot);

    public Snippet? Get(string id) => GetAll().FirstOrDefault(s => s.Id == id);

    public async Task<Snippet> SaveAsync(Snippet snippet)
    {
        var tcs = new TaskCompletionSource<Snippet>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new SaveOp(snippet, tcs));
        return await tcs.Task;
    }

    public async Task DeleteAsync(string id)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new DeleteOp(id, tcs));
        await tcs.Task;
    }

    public void RecordUse(string id) => _usage.RecordUse(id);

    public SnippetUsage GetUsage(string id) => _usage.Get(id);

    // ── Channel processor (single writer) ───────────────────────────────────

    private async Task ProcessChannelAsync()
    {
        await foreach (var op in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                using var fileLease = await FileGate.EnterAsync().ConfigureAwait(false);
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

                    case RescanOp:
                        ExecuteLoadAll();
                        break;
                    case DrainOp drain:
                        drain.Completion.SetResult();
                        break;
                    case ExternalChangeOp ext:
                        ExecuteExternalChange(ext.Path);
                        break;
                }
            }
            catch (Exception ex) when (op is ExternalChangeOp or RescanOp)
            {
                LastStorageError = ex;
                // One unreadable external file must not terminate the worker.
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
        var previous = _snippets.ToDictionary();
        var previousMalformed = _malformed.ToArray();
        try
        {
            _snippets.Clear();
            _malformed.Clear();

            if (!Directory.Exists(_snippetsDir))
                Directory.CreateDirectory(_snippetsDir);

            StartWatcher();
            foreach (var file in Directory.EnumerateFiles(_snippetsDir, "*.md"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                try { TryParseAndStore(id, file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LastStorageError = ex;
                    if (previous.TryGetValue(id, out var retained)) _snippets[id] = retained;
                    if (!_malformed.Contains(file)) _malformed.Add(file);
                }
            }
            PublishSnapshot();
        }
        catch
        {
            _snippets.Clear();
            foreach (var entry in previous) _snippets.Add(entry.Key, entry.Value);
            _malformed.Clear();
            _malformed.AddRange(previousMalformed);
            throw;
        }
    }

    private Snippet ExecuteSave(Snippet snippet)
    {
        Directory.CreateDirectory(_snippetsDir);

        var updated = snippet with { Updated = _clock.UtcNow, Tags = Array.AsReadOnly(snippet.Tags.ToArray()), Placeholders = Array.AsReadOnly(snippet.Placeholders.ToArray()) };
        var content = SnippetSerializer.Serialize(updated);
        var hash = ComputeHash(content);
        var filePath = SnippetPath(updated.Id);
        var tmpPath = filePath + ".tmp";

        // Register expected write before touching disk
        _expectedWrites[filePath] = (hash, _clock.UtcNow.AddSeconds(2));

        File.WriteAllText(tmpPath, content, Encoding.UTF8);
        File.Move(tmpPath, filePath, overwrite: true);

        _snippets[updated.Id] = updated;
        PublishSnapshot();
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
        PublishSnapshot();
        SnippetRemoved?.Invoke(this, new SnippetRemovedEventArgs(id));
    }

    private void ExecuteExternalChange(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var removedId = Path.GetFileNameWithoutExtension(filePath);
            if (_snippets.Remove(removedId))
                PublishSnapshot();
            return;
        }

        var content = ReadFileWithRetry(filePath);

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
        if (TryParseAndStore(id, filePath)) PublishSnapshot();
    }

    private bool TryParseAndStore(string id, string filePath)
    {
        var content = ReadFileWithRetry(filePath);

        try
        {
            var snippet = SnippetSerializer.Deserialize(id, content);
            if (_snippets.TryGetValue(id, out var existing)
                && SnippetSerializer.Serialize(existing) == SnippetSerializer.Serialize(snippet)
                && !_malformed.Contains(filePath)) return false;
            _snippets[id] = snippet with { Tags = Array.AsReadOnly(snippet.Tags.ToArray()), Placeholders = Array.AsReadOnly(snippet.Placeholders.ToArray()) };

            _malformed.Remove(filePath);
            return true;
        }
        catch
        {
            if (_malformed.Contains(filePath)) return false;
            _malformed.Add(filePath);
            return true;
        }
    }

    private static string ReadFileWithRetry(string path)
    {
        IOException? error = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException ex) { error = ex; Thread.Sleep(50); }
        }
        throw error!;
    }

    // ── FileSystemWatcher ────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();

    private void StartWatcher()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            // A staged sync recovery can replace the directory at this path.
            // Recreate the handle before scanning so it follows the active directory.
            _watcher?.Dispose();
            foreach (var debounce in _debounceTokens.Values) debounce.Cancel();
            _watcher = new FileSystemWatcher(_snippetsDir, "*.md")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = false,
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
            _watcher.Error += (_, _) => _channel.Writer.TryWrite(new RescanOp());
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void OnWatcherEvent(object? sender, FileSystemEventArgs e)
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            if (_debounceTokens.TryGetValue(e.FullPath, out var previous)) previous.Cancel();
            var cancellation = new CancellationTokenSource();
            _debounceTokens[e.FullPath] = cancellation;
            _ = QueueExternalChangeAsync(e.FullPath, cancellation);
        }
    }

    private async Task QueueExternalChangeAsync(string path, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(300, cancellation.Token).ConfigureAwait(false);
            lock (_lifecycle)
                if (!_disposed && !cancellation.IsCancellationRequested)
                    _channel.Writer.TryWrite(new ExternalChangeOp(path));
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_lifecycle)
            {
                if (_debounceTokens.TryGetValue(path, out var current) && ReferenceEquals(current, cancellation))
                    _debounceTokens.TryRemove(path, out _);
                cancellation.Dispose();
            }
        }
    }

    private string SnippetPath(string id) => Path.Combine(_snippetsDir, $"{id}.md");

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private void PublishSnapshot()
    {
        Volatile.Write(ref _snapshot, Array.AsReadOnly(_snippets.Values.ToArray()));
        Volatile.Write(ref _badSnapshot, Array.AsReadOnly(_malformed.ToArray()));
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DrainAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _channel.Writer.WriteAsync(new DrainOp(completion));
        await completion.Task;
    }

    private bool _disposed;
    private readonly object _lifecycle = new();
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (!_disposed)
            {
                _disposed = true;
                _watcher?.Dispose();
                foreach (var timer in _debounceTokens.Values) timer.Cancel();
                _channel.Writer.TryComplete();
            }
        }
        await _processorTask.ConfigureAwait(false);
    }

    // ── Channel operation types ──────────────────────────────────────────────

    private abstract record RepoOp;
    private sealed record RescanOp : RepoOp;
    private sealed record DrainOp(TaskCompletionSource Completion) : RepoOp;
    private sealed record LoadAllOp(TaskCompletionSource Completion) : RepoOp;
    private sealed record SaveOp(Snippet Snippet, TaskCompletionSource<Snippet> Completion) : RepoOp;
    private sealed record DeleteOp(string Id, TaskCompletionSource Completion) : RepoOp;
    private sealed record ExternalChangeOp(string Path) : RepoOp;
}
