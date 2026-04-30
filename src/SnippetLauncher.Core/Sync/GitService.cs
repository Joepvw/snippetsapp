using System.Diagnostics;
using System.Threading.Channels;
using LibGit2Sharp;
using Serilog;
using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.Core.Sync;

/// <summary>
/// All LibGit2Sharp calls happen on a single dedicated background thread because
/// LibGit2Sharp Repository handles are not thread-safe. Operations are queued via
/// a Channel and processed sequentially.
/// </summary>
public sealed class GitService : IGitService
{
    private readonly string _repoPath;
    private readonly string? _remoteUrl;
    private readonly IClock _clock;
    private readonly IDialogService _dialog;
    private readonly PushQueueStore _pushQueue;

    private readonly Channel<GitOp> _channel = Channel.CreateUnbounded<GitOp>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();

    private Func<bool> _isEditorDirty = () => false;
    private Timer? _pullTimer;

    private GitSyncStatus _status = GitSyncStatus.Idle;

    public GitSyncStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<GitSyncStatus>? StatusChanged;

    public GitService(string repoPath, IClock clock, IDialogService dialog, PushQueueStore pushQueue, string? remoteUrl = null)
    {
        _repoPath = repoPath;
        _remoteUrl = string.IsNullOrWhiteSpace(remoteUrl) ? null : remoteUrl.Trim();
        _clock = clock;
        _dialog = dialog;
        _pushQueue = pushQueue;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SnippetLauncher.GitWorker" };
        _worker.Start();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public Task InitOrOpenAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(new InitOrOpenOp(tcs));
        return tcs.Task;
    }

    public Task CommitAndQueuePushAsync(string message)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(new CommitOp(message, tcs));
        return tcs.Task;
    }

    public Task RetryPushNowAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(new PushOp(tcs));
        return tcs.Task;
    }

    public void StartAutoSync(int pullIntervalSeconds, Func<bool> isEditorDirty)
    {
        _isEditorDirty = isEditorDirty;
        _pullTimer?.Dispose();

        var interval = TimeSpan.FromSeconds(pullIntervalSeconds);
        _pullTimer = new Timer(_ =>
        {
            if (!_isEditorDirty())
                _channel.Writer.TryWrite(new PullOp(null));
        }, null, interval, interval);
    }

    // ── Worker loop ──────────────────────────────────────────────────────────

    private void WorkerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            GitOp op;
            try
            {
                op = _channel.Reader.ReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            try
            {
                Execute(op);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitService: op {Op} failed", op.GetType().Name);
                Status = GitSyncStatus.Error;
                if (op is CompletableOp c) c.TrySetException(ex);
            }
        }
    }

    private void Execute(GitOp op)
    {
        switch (op)
        {
            case InitOrOpenOp init:
                ExecuteInitOrOpen();
                init.TrySetResult();
                break;

            case PullOp pull:
                ExecutePull();
                pull.TrySetResult();
                break;

            case CommitOp commit:
                ExecuteCommit(commit.Message);
                commit.TrySetResult();
                break;

            case PushOp push:
                ExecutePush();
                push.TrySetResult();
                break;
        }
    }

    // ── Git operations (all on worker thread) ────────────────────────────────

    private void ExecuteInitOrOpen()
    {
        if (!Repository.IsValid(_repoPath))
        {
            Directory.CreateDirectory(_repoPath);
            var isEmpty = !Directory.EnumerateFileSystemEntries(_repoPath).Any();

            if (_remoteUrl is not null && isEmpty)
            {
                try
                {
                    Status = GitSyncStatus.Syncing;
                    Repository.Clone(_remoteUrl, _repoPath, new CloneOptions
                    {
                        FetchOptions = { CredentialsProvider = CredentialsProvider },
                    });
                    Log.Information("GitService: cloned {Url} into {Path}", _remoteUrl, _repoPath);
                    Status = GitSyncStatus.Idle;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "GitService: clone failed, falling back to init");
                    Status = GitSyncStatus.Error;
                    Repository.Init(_repoPath);
                    EnsureOriginConfigured();
                }
            }
            else
            {
                Repository.Init(_repoPath);
                Log.Information("GitService: initialized new repo at {Path}", _repoPath);
                EnsureOriginConfigured();
            }
        }
        else
        {
            Log.Information("GitService: opened existing repo at {Path}", _repoPath);
            EnsureOriginConfigured();
        }

        // Drain any push queue left over from a previous session
        if (_pushQueue.HasPending)
        {
            Log.Information("GitService: {Count} entries in push queue from previous session — retrying", _pushQueue.Pending.Count);
            ExecutePush();
        }
    }

    private void EnsureOriginConfigured()
    {
        if (_remoteUrl is null) return;
        if (!Repository.IsValid(_repoPath)) return;

        try
        {
            using var repo = new Repository(_repoPath);
            var existing = repo.Network.Remotes["origin"];
            if (existing is null)
            {
                repo.Network.Remotes.Add("origin", _remoteUrl);
                Log.Information("GitService: added origin {Url}", _remoteUrl);
            }
            else if (!string.Equals(existing.Url, _remoteUrl, StringComparison.OrdinalIgnoreCase))
            {
                repo.Network.Remotes.Update("origin", r => r.Url = _remoteUrl);
                Log.Information("GitService: updated origin URL to {Url}", _remoteUrl);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitService: failed to ensure origin remote");
        }
    }

    private bool BootstrapFromRemote(Repository repo)
    {
        // Local repo has no commits yet — fetch from origin and check out the default branch.
        try
        {
            var origin = repo.Network.Remotes["origin"];
            if (origin is null) return false;

            var fetchSpecs = origin.FetchRefSpecs.Select(r => r.Specification).ToList();
            LibGit2Sharp.Commands.Fetch(repo, "origin", fetchSpecs, BuildFetchOptions(), null);

            // Resolve origin's default branch (origin/HEAD), fall back to common names.
            string? remoteBranchName =
                repo.Refs["refs/remotes/origin/HEAD"]?.ResolveToDirectReference()?.CanonicalName
                ?? (repo.Branches["origin/main"] is not null ? "refs/remotes/origin/main" : null)
                ?? (repo.Branches["origin/master"] is not null ? "refs/remotes/origin/master" : null);

            if (remoteBranchName is null)
            {
                Log.Warning("GitService: could not determine default branch on origin — empty remote?");
                return false;
            }

            var remoteBranch = repo.Branches[remoteBranchName.Replace("refs/remotes/", "")];
            if (remoteBranch is null) return false;

            var localName = remoteBranch.FriendlyName.StartsWith("origin/")
                ? remoteBranch.FriendlyName["origin/".Length..]
                : remoteBranch.FriendlyName;

            var localBranch = repo.CreateBranch(localName, remoteBranch.Tip);
            repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
            LibGit2Sharp.Commands.Checkout(repo, localBranch);
            repo.Refs.UpdateTarget("HEAD", localBranch.CanonicalName);

            Log.Information("GitService: bootstrapped from {Branch}", remoteBranch.FriendlyName);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitService: bootstrap from remote failed");
            return false;
        }
    }

    private void ExecutePull()
    {
        if (!Repository.IsValid(_repoPath)) return;

        Status = GitSyncStatus.Syncing;

        try
        {
            using var repo = new Repository(_repoPath);

            if (repo.Network.Remotes["origin"] is null)
            {
                Status = GitSyncStatus.NoRemote;
                return;
            }

            if (repo.Head.Tip is null)
            {
                // Local repo has no commits yet — try to bootstrap from the remote's default branch.
                // This recovers from `git init` against an empty folder where a clone was needed.
                if (BootstrapFromRemote(repo))
                    Status = GitSyncStatus.Idle;
                else
                    Status = GitSyncStatus.Idle;
                return;
            }

            // 1. Fetch
            var fetchSpecs = repo.Network.Remotes["origin"].FetchRefSpecs
                .Select(r => r.Specification).ToList();
            LibGit2Sharp.Commands.Fetch(repo, "origin", fetchSpecs, BuildFetchOptions(), null);

            var trackingBranch = repo.Head.TrackedBranch;
            if (trackingBranch is null)
            {
                Status = GitSyncStatus.Idle;
                return;
            }

            // 2. Check divergence
            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(
                repo.Head.Tip, trackingBranch.Tip);

            if (divergence.BehindBy == 0)
            {
                Status = GitSyncStatus.Idle;
                return;
            }

            // 3. Merge with accept-theirs on conflict (remote wins)
            var sig = MakeSig();
            var mergeOpts = new MergeOptions
            {
                FileConflictStrategy = CheckoutFileConflictStrategy.Theirs,
                CommitOnSuccess = false,
            };

            var result = repo.Merge(trackingBranch.Tip, sig, mergeOpts);

            var backedUp = new List<string>();

            if (result.Status == MergeStatus.Conflicts || repo.Index.Conflicts.Any())
            {
                backedUp = BackupAndResolveConflicts(repo);
                Log.Warning("GitService: conflict resolved (last-writer-wins), {Count} backups", backedUp.Count);
            }

            if (result.Status is MergeStatus.NonFastForward or MergeStatus.Conflicts)
            {
                repo.Commit("Sync: merge remote changes", sig, sig,
                    new CommitOptions { AllowEmptyCommit = false });
            }

            if (backedUp.Count > 0)
            {
                Status = GitSyncStatus.Conflict;
                _ = _dialog.ShowConflictNotificationAsync(backedUp);
            }
            else
            {
                Status = GitSyncStatus.Idle;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitService: pull failed");
            Status = GitSyncStatus.Error;
        }
    }

    private void ExecuteCommit(string message)
    {
        if (!Repository.IsValid(_repoPath)) return;

        try
        {
            using var repo = new Repository(_repoPath);

            // Stage all changes
            var status = repo.RetrieveStatus(new StatusOptions { ExcludeSubmodules = true });
            if (!status.IsDirty) return;

            foreach (var item in status)
            {
                if (item.State.HasFlag(FileStatus.DeletedFromWorkdir) ||
                    item.State.HasFlag(FileStatus.DeletedFromIndex))
                    repo.Index.Remove(item.FilePath);
                else
                    repo.Index.Add(item.FilePath);
            }
            repo.Index.Write();

            var sig = MakeSig();
            var commit = repo.Commit(message, sig, sig, new CommitOptions { AllowEmptyCommit = false });

            _pushQueue.Enqueue(new PushQueueStore.PushEntry
            {
                CommitSha = commit.Sha,
                QueuedAt = _clock.UtcNow,
            });

            Status = GitSyncStatus.Behind;
            Log.Information("GitService: committed {Sha} — queued push", commit.Sha[..7]);

            // Try to push immediately
            _channel.Writer.TryWrite(new PushOp(null));
        }
        catch (EmptyCommitException)
        {
            // Nothing changed — skip
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitService: commit failed");
            Status = GitSyncStatus.Error;
        }
    }

    private void ExecutePush()
    {
        if (!_pushQueue.HasPending) return;
        if (!Repository.IsValid(_repoPath)) return;

        try
        {
            using var repo = new Repository(_repoPath);

            if (repo.Network.Remotes["origin"] is null)
            {
                Status = GitSyncStatus.NoRemote;
                return;
            }

            Status = GitSyncStatus.Syncing;

            var pushRefSpec = $"refs/heads/{repo.Head.FriendlyName}";

            foreach (var entry in _pushQueue.Pending.ToList())
            {
                if (entry.AttemptCount >= 5)
                {
                    Log.Warning("GitService: push entry {Sha} exceeded max retries", entry.CommitSha[..7]);
                    continue;
                }

                try
                {
                    repo.Network.Push(repo.Network.Remotes["origin"], pushRefSpec, BuildPushOptions());
                    _pushQueue.Dequeue(entry);
                    Log.Information("GitService: pushed {Sha}", entry.CommitSha[..7]);
                }
                catch (Exception ex)
                {
                    entry.AttemptCount++;
                    _pushQueue.Save();
                    Log.Warning(ex, "GitService: push attempt {Attempt} failed", entry.AttemptCount);

                    // Exponential backoff: 2^n seconds, max 1 hour
                    var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, entry.AttemptCount) * 30, 3600));
                    _ = Task.Delay(delay, _cts.Token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                            _channel.Writer.TryWrite(new PushOp(null));
                    });

                    break; // Stop trying this session — backoff timer will retry
                }
            }

            Status = _pushQueue.HasPending ? GitSyncStatus.Behind : GitSyncStatus.Idle;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitService: push failed");
            Status = GitSyncStatus.Error;
        }
    }

    // ── Conflict resolution ──────────────────────────────────────────────────

    private List<string> BackupAndResolveConflicts(Repository repo)
    {
        var backed = new List<string>();
        var conflictsDir = Path.Combine(_repoPath, ".local", "conflicts");
        Directory.CreateDirectory(conflictsDir);

        foreach (var conflict in repo.Index.Conflicts.ToList())
        {
            var path = conflict.Ours?.Path ?? conflict.Ancestor?.Path ?? conflict.Theirs?.Path;
            if (path is null) continue;

            var localPath = Path.Combine(repo.Info.WorkingDirectory, path);

            // Backup "ours" content from the git blob (disk may already be overwritten)
            if (conflict.Ours is not null)
            {
                var blob = repo.Lookup<Blob>(conflict.Ours.Id);
                if (blob is not null)
                {
                    var stamp = _clock.UtcNow.ToString("yyyyMMdd-HHmmss");
                    var backupName = $"{Path.GetFileNameWithoutExtension(path)}-{stamp}{Path.GetExtension(path)}";
                    var backupPath = Path.Combine(conflictsDir, backupName);
                    using var inStream = blob.GetContentStream();
                    using var outStream = File.Create(backupPath);
                    inStream.CopyTo(outStream);
                    backed.Add(backupPath);
                }
            }

            // Explicitly write "theirs" blob to disk — remote wins
            if (conflict.Theirs is not null)
            {
                var theirBlob = repo.Lookup<Blob>(conflict.Theirs.Id);
                if (theirBlob is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    using var inStream = theirBlob.GetContentStream();
                    using var outStream = File.Create(localPath);
                    inStream.CopyTo(outStream);
                }
                repo.Index.Add(path);
            }
            else
            {
                if (File.Exists(localPath)) File.Delete(localPath);
                repo.Index.Remove(path);
            }
        }

        repo.Index.Write();
        return backed;
    }

    // ── Credentials (Git Credential Manager via git credential fill) ─────────

    private FetchOptions BuildFetchOptions() => new() { CredentialsProvider = CredentialsProvider };
    private PushOptions BuildPushOptions() => new() { CredentialsProvider = CredentialsProvider };

    private Credentials CredentialsProvider(string url, string? usernameFromUrl, SupportedCredentialTypes types)
    {
        try
        {
            var filled = InvokeGitCredentialFill(url, usernameFromUrl);
            if (filled is not null)
                return new UsernamePasswordCredentials { Username = filled.Value.User, Password = filled.Value.Pass };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "GitService: credential fill failed, falling back to default");
        }
        return new DefaultCredentials();
    }

    private static (string User, string Pass)? InvokeGitCredentialFill(string url, string? username)
    {
        var gitExe = FindGitExe();
        if (gitExe is null) return null;

        var uri = new Uri(url);
        var input = $"protocol={uri.Scheme}\nhost={uri.Host}\n";
        if (!string.IsNullOrEmpty(username)) input += $"username={username}\n";
        input += "\n";

        var psi = new ProcessStartInfo(gitExe, "credential fill")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Write(input);
        proc.StandardInput.Close();
        var output = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(5_000)) { proc.Kill(); return null; }

        var dict = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        return dict.TryGetValue("username", out var u) && dict.TryGetValue("password", out var p)
            ? (u, p)
            : null;
    }

    private static string? FindGitExe()
    {
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "git.exe");
            if (File.Exists(candidate)) return candidate;
        }
        string[] wellKnown =
        [
            @"C:\Program Files\Git\bin\git.exe",
            @"C:\Program Files\Git\cmd\git.exe",
        ];
        return wellKnown.FirstOrDefault(File.Exists);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Signature MakeSig() => new("SnippetLauncher", "sync@local", _clock.UtcNow);

    public void Dispose()
    {
        _cts.Cancel();
        _pullTimer?.Dispose();
        _channel.Writer.Complete();
        _cts.Dispose();
    }

    // ── Channel operation types ──────────────────────────────────────────────

    private abstract class GitOp;

    private abstract class CompletableOp : GitOp
    {
        private readonly TaskCompletionSource? _tcs;
        protected CompletableOp(TaskCompletionSource? tcs) => _tcs = tcs;
        public void TrySetResult() => _tcs?.TrySetResult();
        public void TrySetException(Exception ex) => _tcs?.TrySetException(ex);
    }

    private sealed class InitOrOpenOp(TaskCompletionSource tcs) : CompletableOp(tcs);
    private sealed class PullOp(TaskCompletionSource? tcs) : CompletableOp(tcs);
    private sealed class CommitOp(string message, TaskCompletionSource tcs) : CompletableOp(tcs)
    {
        public string Message { get; } = message;
    }
    private sealed class PushOp(TaskCompletionSource? tcs) : CompletableOp(tcs);
}
