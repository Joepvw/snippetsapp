using System.Threading.Channels;
using LibGit2Sharp;
using Serilog;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Infrastructure;

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
    private readonly IGitCredentialProvider _credentials;
    private readonly LibraryFileGate? _fileGate;
    private GitCredential? _credential;
    private bool _authenticationPaused;
    private int _disposed;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public GitService(string repoPath, IClock clock, IDialogService dialog, PushQueueStore pushQueue, string? remoteUrl = null, IGitCredentialProvider? credentials = null, LibraryFileGate? fileGate = null)
    {
        _repoPath = repoPath;
        _remoteUrl = remoteUrl is null ? null : RemoteUrlValidator.Validate(remoteUrl);
        _clock = clock;
        _dialog = dialog;
        _pushQueue = pushQueue;
        _credentials = credentials ?? new GitCredentialProvider();
        _fileGate = fileGate;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SnippetLauncher.GitWorker" };
        _worker.Start();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public Task InitOrOpenAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new InitOrOpenOp(tcs));
        return tcs.Task;
    }

    public Task CommitAndQueuePushAsync(string message)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CommitOp(message, tcs));
        return tcs.Task;
    }

    public Task RetryPushNowAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new PushOp(tcs, true));
        return tcs.Task;
    }

    public Task PullNowAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new PullOp(tcs));
        return tcs.Task;
    }

    private void Enqueue(CompletableOp op)
    {
        if (!_channel.Writer.TryWrite(op)) op.TrySetException(new ObjectDisposedException(nameof(GitService)));
    }
    public Task AuthenticateAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new AuthenticateOp(tcs));
        return tcs.Task;
    }
    public void StartAutoSync(int pullIntervalSeconds, Func<bool> isEditorDirty)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (pullIntervalSeconds is < 1 or > 86400) throw new ArgumentOutOfRangeException(nameof(pullIntervalSeconds));
        _isEditorDirty = isEditorDirty;
        _pullTimer?.Dispose();

        var interval = TimeSpan.FromSeconds(pullIntervalSeconds);
        _pullTimer = new Timer(_ =>
        {
            if (!_authenticationPaused && !_isEditorDirty())
                _channel.Writer.TryWrite(new PullOp(null));
        }, null, interval, interval);
    }

    // ── Worker loop ──────────────────────────────────────────────────────────

    private void WorkerLoop()
    {
        while (true)
        {
            GitOp op;
            try
            {
                op = _channel.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            try
            {
                Execute(op);
            }
            catch (Exception ex)
            {
                Log.Warning("GitService: op {Op} failed ({Type})", op.GetType().Name, ex.GetType().Name);
                Status = ex is GitImportConflictException ? GitSyncStatus.Conflict : _authenticationPaused || ex is GitAuthenticationException ? GitSyncStatus.AuthenticationRequired : ex is LibGit2SharpException ? GitSyncStatus.RemoteUnavailable : GitSyncStatus.Error;
                if (ex is GitAuthenticationException) _authenticationPaused = true;
                if (op is CompletableOp c) c.TrySetException(ex);
            }
        }
        _stopped.TrySetResult();
    }

    private void Execute(GitOp op)
    {
        switch (op)
        {
            case AuthenticateOp auth:
                ExecuteAuthenticate();
                auth.TrySetResult();
                break;
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
                ExecutePush(push.Manual);
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

            if (!string.IsNullOrEmpty(_remoteUrl) && isEmpty)
            {
                try
                {
                    Status = GitSyncStatus.Syncing;
                    var stage = _repoPath.TrimEnd(Path.DirectorySeparatorChar) + ".recovery-" + Guid.NewGuid().ToString("N");
                    Repository.Clone(_remoteUrl, stage, BuildCloneOptions());
                    Repository.Init(_repoPath);
                    EnsureOriginConfigured();
                    RecoverIndependentLibrary(stage);
                    Log.Information("GitService: cloned {Url} into {Path}", _remoteUrl, _repoPath);
                    Status = GitSyncStatus.Idle;
                }
                catch (Exception)
                {
                    Log.Warning("GitService: clone failed, falling back to init");
                    Status = GitSyncStatus.Error;
                    Repository.Init(_repoPath);
                    EnsureOriginConfigured();
                    throw;
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

        using (var repo = new Repository(_repoPath))
        {
            if (repo.RetrieveStatus().IsDirty) ExecuteCommit("Sync: recover interrupted local changes");
            RecoverPending(repo);
        }
        // Drain any push queue left over from a previous session
        if (_pushQueue.HasPending)
        {
            Log.Information("GitService: {Count} entries in push queue from previous session — retrying", _pushQueue.Pending.Count);
            try { ExecutePush(); } catch (Exception ex) { Status = ex is GitAuthenticationException ? GitSyncStatus.AuthenticationRequired : GitSyncStatus.RemoteUnavailable; }
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
            if (_remoteUrl.Length == 0)
            {
                if (existing is not null) repo.Network.Remotes.Remove("origin");
                return;
            }
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
        catch (Exception)
        {
            Log.Warning("GitService: failed to ensure origin remote");
            throw;
        }
    }

    private void BootstrapFromRemote(Repository repo)
    {
        // Local repo has no commits yet — fetch from origin and check out the default branch.
        try
        {
            var origin = repo.Network.Remotes["origin"];
            if (origin is null) return;

            var fetchSpecs = origin.FetchRefSpecs.Select(r => r.Specification).ToList();
            LibGit2Sharp.Commands.Fetch(repo, "origin", fetchSpecs, BuildFetchOptions(), null);

            // Resolve origin's default branch (origin/HEAD), fall back to common names.
            string? remoteBranchName =
                repo.Refs["refs/remotes/origin/HEAD"]?.ResolveToDirectReference()?.CanonicalName
                ?? (repo.Branches["origin/main"] is not null ? "refs/remotes/origin/main" : null)
                ?? (repo.Branches["origin/master"] is not null ? "refs/remotes/origin/master" : null);

            if (remoteBranchName is null)
            {
                if (!repo.Branches.Any(b => b.IsRemote)) return; // A valid empty remote.
                throw new InvalidOperationException("Kan de standaardbranch van de remote niet bepalen.");
            }

            var remoteBranch = repo.Branches[remoteBranchName.Replace("refs/remotes/", "")];
            if (remoteBranch is null)
                throw new InvalidOperationException("De standaardbranch van de remote ontbreekt.");

            var localName = remoteBranch.FriendlyName.StartsWith("origin/")
                ? remoteBranch.FriendlyName["origin/".Length..]
                : remoteBranch.FriendlyName;

            var localBranch = repo.CreateBranch(localName, remoteBranch.Tip);
            repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
            using var fileLease = _fileGate?.Enter(_cts.Token);
            LibGit2Sharp.Commands.Checkout(repo, localBranch);
            repo.Refs.UpdateTarget("HEAD", localBranch.CanonicalName);

            Log.Information("GitService: bootstrapped from {Branch}", remoteBranch.FriendlyName);
        }
        catch (Exception)
        {
            Log.Warning("GitService: bootstrap from remote failed");
            throw;
        }
    }

    private void ExecutePull()
    {
        if (!Repository.IsValid(_repoPath))
            throw new InvalidOperationException("De lokale Git-repository is niet beschikbaar.");

        Status = GitSyncStatus.Syncing;

        try
        {
            using var repo = new Repository(_repoPath);

            if (repo.Network.Remotes["origin"] is null)
            {
                Status = GitSyncStatus.NoRemote;
                return;
            }

            RemoteUrlValidator.Validate(repo.Network.Remotes["origin"].Url);
            if (repo.Head.Tip is null)
            {
                // Local repo has no commits yet — try to bootstrap from the remote's default branch.
                // This recovers from `git init` against an empty folder where a clone was needed.
                BootstrapFromRemote(repo);
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
                trackingBranch = repo.Branches[$"origin/{repo.Head.FriendlyName}"] ?? repo.Branches["origin/main"] ?? repo.Branches["origin/master"];
                if (trackingBranch is not null && repo.ObjectDatabase.FindMergeBase(repo.Head.Tip, trackingBranch.Tip) is null)
                {
                    repo.Dispose();
                    RecoverIndependentLibrary();
                    return;
                }
                if (trackingBranch is not null) repo.Branches.Update(repo.Head, b => b.TrackedBranch = trackingBranch.CanonicalName);
            }
            if (trackingBranch is null)
            {
                Status = GitSyncStatus.Idle;
                return;
            }

            using var fileLease = _fileGate?.Enter(_cts.Token);
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
        catch (Exception)
        {
            Log.Warning("GitService: pull failed");
            Status = GitSyncStatus.Error;
            throw;
        }
    }

    private void RecoverIndependentLibrary(string? preparedStage = null)
    {
        string remote;
        HashSet<string> originalCommits;
        using (var original = new Repository(_repoPath))
        {
            remote = RemoteUrlValidator.Validate(original.Network.Remotes["origin"].Url);
            originalCommits = original.Commits.Select(commit => commit.Sha).ToHashSet();
        }
        var stage = preparedStage ?? _repoPath.TrimEnd(Path.DirectorySeparatorChar) + ".recovery-" + Guid.NewGuid().ToString("N");
        if (preparedStage is null) Repository.Clone(remote, stage, BuildCloneOptions());
        using var fileLease = _fileGate?.Enter(_cts.Token);
        var localFiles = Directory.EnumerateFiles(_repoPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_repoPath, path))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is ".git" or ".local")).ToList();
        var conflicts = localFiles.Where(path => File.Exists(Path.Combine(stage, path)) &&
            !File.ReadAllBytes(Path.Combine(stage, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(_repoPath, path)))).ToList();
        bool? localWins = true;
        if (conflicts.Count > 0) localWins = _dialog.ConfirmImportConflictsAsync(conflicts).GetAwaiter().GetResult();
        if (localWins is null)
        {
            Status = GitSyncStatus.Conflict;
            try
            {
                foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(stage, true);
            }
            catch (IOException) { Log.Warning("GitService: recovery clone retained after cleanup failure"); }
            catch (UnauthorizedAccessException) { Log.Warning("GitService: recovery clone retained after cleanup failure"); }
            throw new GitImportConflictException("Import geannuleerd. De lokale bibliotheek blijft behouden.");
        }
        foreach (var path in localFiles)
        {
            var destination = Path.Combine(stage, path);
            if (conflicts.Contains(path))
            {
                var backup = Path.Combine(stage, ".local", "conflicts", path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(Path.Combine(_repoPath, path), backup + ".local", true);
                File.Copy(destination, backup + ".remote", true);
                if (!localWins.Value) continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(_repoPath, path), destination, true);
        }
        using (var imported = new Repository(stage))
        {
            foreach (var path in localFiles) imported.Index.Add(path);
            imported.Index.Write();
            if (imported.RetrieveStatus().Any(entry => !entry.FilePath.StartsWith(".local/")))
                imported.Commit("Sync: import preserved local library", MakeSig(), MakeSig());
        }
        var originalBackup = _repoPath.TrimEnd(Path.DirectorySeparatorChar) + ".preserved-" + Guid.NewGuid().ToString("N");
        Directory.Move(_repoPath, originalBackup);
        try { Directory.Move(stage, _repoPath); }
        catch { Directory.Move(originalBackup, _repoPath); throw; }
        using (var recovered = new Repository(_repoPath))
        {
            foreach (var entry in _pushQueue.Pending.Where(entry => originalCommits.Contains(entry.CommitSha))) entry.CommitSha = recovered.Head.Tip.Sha;
            _pushQueue.Save();
            RecoverPending(recovered);
        }
        Status = conflicts.Count > 0 ? GitSyncStatus.Conflict : GitSyncStatus.Behind;
    }

    private void ExecuteCommit(string message)
    {
        if (!Repository.IsValid(_repoPath)) return;

        try
        {
            using var repo = new Repository(_repoPath);

            using var fileLease = _fileGate?.Enter();
            // Stage all changes
            var status = repo.RetrieveStatus(new StatusOptions { ExcludeSubmodules = true });
            if (!status.IsDirty) return;

            foreach (var item in status)
            {
                if (item.FilePath.StartsWith(".local/", StringComparison.Ordinal) || item.FilePath.StartsWith(".local\\", StringComparison.Ordinal)) continue;
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
        catch (Exception)
        {
            Log.Warning("GitService: commit failed");
            Status = GitSyncStatus.Error;
            throw;
        }
    }

    private void ExecutePush(bool manual = false)
    {
        if (_authenticationPaused && !manual) return;
        if (!Repository.IsValid(_repoPath))
            throw new InvalidOperationException("De lokale Git-repository is niet beschikbaar.");

        try
        {
            using var repo = new Repository(_repoPath);

            if (repo.Network.Remotes["origin"] is null)
            {
                Status = GitSyncStatus.NoRemote;
                return;
            }

            Status = GitSyncStatus.Syncing;

            RemoteUrlValidator.Validate(repo.Network.Remotes["origin"].Url);
            RecoverPending(repo);
            if (!_pushQueue.HasPending) { Status = GitSyncStatus.Idle; return; }
            if (manual) foreach (var pending in _pushQueue.Pending) pending.AttemptCount = 0;
            var pushRefSpec = $"refs/heads/{repo.Head.FriendlyName}";

            foreach (var entry in _pushQueue.Pending.ToList())
            {
                if (entry.AttemptCount >= 5)
                {
                    Log.Warning("GitService: push entry {Sha} exceeded max retries", entry.CommitSha[..Math.Min(7, entry.CommitSha.Length)]);
                    throw new InvalidOperationException("Push is niet gelukt na vijf pogingen. De wijzigingen blijven lokaal bewaard.");
                }

                try
                {
                    repo.Network.Push(repo.Network.Remotes["origin"], $"{pushRefSpec}:{pushRefSpec}", BuildPushOptions());
                    var trackingName = $"refs/remotes/origin/{repo.Head.FriendlyName}";
                    repo.Refs.Add(trackingName, repo.Head.Tip.Id, true);
                    repo.Branches.Update(repo.Head, b => b.TrackedBranch = trackingName);
                    if (repo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = repo.Head.Tip }).Any(c => c.Sha == entry.CommitSha)) _pushQueue.Dequeue(entry);
                    ApproveCredential(repo.Network.Remotes["origin"].Url);
                    Log.Information("GitService: pushed {Sha}", entry.CommitSha[..Math.Min(7, entry.CommitSha.Length)]);
                }
                catch (Exception ex)
                {
                    entry.AttemptCount++;
                    _pushQueue.Save();
                    Log.Warning("GitService: push attempt {Attempt} failed", entry.AttemptCount);

                    // Exponential backoff: 2^n seconds, max 1 hour
                    var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, entry.AttemptCount) * 30, 3600));
                    if (ex is not GitAuthenticationException) _ = Task.Delay(delay, _cts.Token).ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                            _channel.Writer.TryWrite(new PushOp(null));
                    });

                    throw; // Report failure to manual callers; the timer still retries later.
                }
            }

            Status = _pushQueue.HasPending ? GitSyncStatus.Behind : GitSyncStatus.Idle;
        }
        catch (Exception)
        {
            Log.Warning("GitService: push failed");
            Status = GitSyncStatus.Error;
            throw;
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

    private CloneOptions BuildCloneOptions() => new()
    {
        FetchOptions = { CredentialsProvider = CredentialsProvider, OnTransferProgress = _ => !_cts.IsCancellationRequested },
    };
    private FetchOptions BuildFetchOptions() => new() { CredentialsProvider = CredentialsProvider, OnTransferProgress = _ => !_cts.IsCancellationRequested };
    private PushOptions BuildPushOptions() => new() { CredentialsProvider = CredentialsProvider, OnPushTransferProgress = (_, _, _) => !_cts.IsCancellationRequested, OnPackBuilderProgress = (_, _, _) => !_cts.IsCancellationRequested };

    private Credentials CredentialsProvider(string url, string? usernameFromUrl, SupportedCredentialTypes types)
    {
        GitCredential? credential;
        try { credential = _credential ?? _credentials.GetAsync(_repoPath, url, false, _cts.Token).GetAwaiter().GetResult(); }
        catch (GitAuthenticationException) { _authenticationPaused = true; throw; }
        if (credential is null) { _authenticationPaused = true; throw new GitAuthenticationException("Aanmelding nodig. Kies Aanmelden in de instellingen."); }
        _authenticationPaused = false;
        _credential = credential;
        return new UsernamePasswordCredentials { Username = credential.Username, Password = credential.Password };
    }
    private void ExecuteAuthenticate()
    {
        string remote;
        using (var repo = new Repository(_repoPath))
            remote = repo.Network.Remotes["origin"]?.Url ?? throw new InvalidOperationException("Geen repository ingesteld.");
        _credential = _credentials.GetAsync(_repoPath, remote, true, _cts.Token).GetAwaiter().GetResult()
            ?? throw new GitAuthenticationException("Aanmelding geannuleerd.");
        _authenticationPaused = false;
        try { ExecutePull(); ExecutePush(true); ApproveCredential(remote); }
        catch { _credential = null; throw; }
    }
    private void ApproveCredential(string url)
    {
        if (_credential is not null) _credentials.ApproveAsync(_repoPath, url, _credential, _cts.Token).GetAwaiter().GetResult();
    }
    private void RecoverPending(Repository repo)
    {
        if (repo.Head.Tip is null || repo.Network.Remotes["origin"] is null) return;
        var tracking = repo.Head.TrackedBranch;
        if (tracking?.Tip is not null && repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, tracking.Tip).AheadBy == 0) return;
        if (!_pushQueue.Pending.Any(entry => entry.CommitSha == repo.Head.Tip.Sha))
            _pushQueue.Enqueue(new PushQueueStore.PushEntry { CommitSha = repo.Head.Tip.Sha, QueuedAt = _clock.UtcNow });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Signature MakeSig() => new("SnippetLauncher", "sync@local", _clock.UtcNow);

    public void StopAutoSyncAndCancelNetwork()
    {
        _pullTimer?.Dispose();
        _cts.Cancel();
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _pullTimer?.Dispose(); _channel.Writer.TryComplete(); _cts.Cancel(); }
        await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(15));
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
    private sealed class PushOp(TaskCompletionSource? tcs, bool manual = false) : CompletableOp(tcs) { public bool Manual { get; } = manual; }
    private sealed class AuthenticateOp(TaskCompletionSource tcs) : CompletableOp(tcs);
}

public sealed class GitImportConflictException(string message) : InvalidOperationException(message);
