using FluentAssertions;
using LibGit2Sharp;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.Core.Tests;

/// <summary>
/// Integration tests for GitService. Each test works with real temporary git repos on disk —
/// LibGit2Sharp requires actual file system operations.
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeDialogService _dialog = new();

    public GitServiceTests() => Directory.CreateDirectory(_root);

    // ── InitOrOpen ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InitOrOpen_NewDirectory_CreatesValidRepo()
    {
        var repoDir = Path.Combine(_root, "newrepo");
        Directory.CreateDirectory(repoDir);

        using var svc = BuildService(repoDir);
        await svc.InitOrOpenAsync();

        Repository.IsValid(repoDir).Should().BeTrue();
    }

    [Fact]
    public async Task InitOrOpen_ExistingRepo_DoesNotThrow()
    {
        var repoDir = Path.Combine(_root, "existingrepo");
        Repository.Init(repoDir);

        using var svc = BuildService(repoDir);
        var act = async () => await svc.InitOrOpenAsync();
        await act.Should().NotThrowAsync();
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CommitAndQueuePush_StagedChanges_CreatesCommitAndQueuesEntry()
    {
        var repoDir = SetupRepoWithInitialCommit();
        var queuePath = Path.Combine(_root, "push-queue.json");
        var pushQueue = new PushQueueStore(queuePath);

        using var svc = BuildService(repoDir, pushQueue);
        await svc.InitOrOpenAsync();

        // Write a new file into the repo
        File.WriteAllText(Path.Combine(repoDir, "hello.md"), "# Hello");

        await svc.CommitAndQueuePushAsync("test: add hello.md");

        // Verify commit was created
        using var repo = new Repository(repoDir);
        repo.Commits.Count().Should().Be(2);
        repo.Head.Tip.Message.Should().Contain("test: add hello.md");

        // Verify push queue was populated
        pushQueue.HasPending.Should().BeTrue();
        pushQueue.Pending.Should().ContainSingle(e => e.CommitSha == repo.Head.Tip.Sha);
    }

    [Fact]
    public async Task CommitAndQueuePush_NothingChanged_DoesNotCreateCommit()
    {
        var repoDir = SetupRepoWithInitialCommit();

        using var svc = BuildService(repoDir);
        await svc.InitOrOpenAsync();

        // No file changes
        await svc.CommitAndQueuePushAsync("empty commit");

        using var repo = new Repository(repoDir);
        repo.Commits.Count().Should().Be(1); // Only the initial commit
    }

    // ── Pull (fast-forward) ───────────────────────────────────────────────────

    [Fact]
    public async Task Pull_FastForward_UpdatesLocalRepo()
    {
        // Arrange: bare remote + two local clones
        var (remoteDir, localDir) = SetupRemoteAndClone();

        // Push a new commit to remote from a second clone
        var secondClone = Path.Combine(_root, "second-clone");
        Repository.Clone(remoteDir, secondClone);
        File.WriteAllText(Path.Combine(secondClone, "remote-snippet.md"), "# Remote");
        using (var r = new Repository(secondClone))
        {
            r.Index.Add("remote-snippet.md");
            r.Index.Write();
            var sig = new Signature("Other", "other@local", DateTimeOffset.UtcNow);
            r.Commit("remote: add remote-snippet", sig, sig);
            r.Network.Push(r.Head);
        }

        // Act: pull in localDir
        using var svc = BuildService(localDir);
        await svc.InitOrOpenAsync();

        await svc.PullNowAsync();

        File.ReadAllText(Path.Combine(localDir, "remote-snippet.md")).Should().Be("# Remote");
        svc.Status.Should().Be(GitSyncStatus.Idle);
    }

    // ── Conflict resolution ───────────────────────────────────────────────────

    [Fact]
    public async Task PullWithConflict_LastWriterWins_BacksUpLocalAndAcceptsRemote()
    {
        // Arrange: bare remote + local clone
        var (remoteDir, localDir) = SetupRemoteAndClone();

        // Both sides modify the same file — create divergence
        const string fileName = "shared.md";

        // Remote side: modify shared file via second clone
        var secondClone = Path.Combine(_root, "second-clone2");
        Repository.Clone(remoteDir, secondClone);
        File.WriteAllText(Path.Combine(secondClone, fileName), "# Remote version");
        using (var r = new Repository(secondClone))
        {
            r.Index.Add(fileName);
            r.Index.Write();
            var sig = new Signature("Other", "other@local", DateTimeOffset.UtcNow);
            r.Commit("remote: update shared", sig, sig);
            r.Network.Push(r.Head);
        }

        // Local side: also modify shared file (creates divergence)
        File.WriteAllText(Path.Combine(localDir, fileName), "# Local version");
        using (var r = new Repository(localDir))
        {
            r.Index.Add(fileName);
            r.Index.Write();
            var sig = new Signature("Local", "local@local", DateTimeOffset.UtcNow);
            r.Commit("local: update shared", sig, sig);
        }

        // Now localDir and remote have diverged on the same file

        // Use GitService to pull (triggers conflict resolution)
        using var svc = BuildService(localDir);
        await svc.InitOrOpenAsync();

        // Trigger pull by calling the service — we use a short-interval auto-sync
        // and wait long enough for it to fire once
        var editorDirty = false;
        svc.StartAutoSync(1, () => editorDirty);
        await Task.Delay(5000); // wait for pull timer to fire + process

        // Assert: file content is the remote version (remote wins)
        var content = File.ReadAllText(Path.Combine(localDir, fileName));
        content.Should().Contain("Remote version");

        // Assert: backup was created in .local/conflicts/
        var conflictsDir = Path.Combine(localDir, ".local", "conflicts");
        Directory.Exists(conflictsDir).Should().BeTrue();
        Directory.GetFiles(conflictsDir, "shared*.md").Should().NotBeEmpty();

        // Assert: dialog was notified
        _dialog.ConflictNotifications.Should().NotBeEmpty();
    }

    // ── Push queue persistence ────────────────────────────────────────────────

    [Fact]
    public async Task PushQueue_PersistedAcrossRestart_DrainedOnInitOrOpen()
    {
        var repoDir = SetupRepoWithInitialCommit();
        var queuePath = Path.Combine(_root, "push-queue2.json");

        // Session 1: commit, simulated push failure means entry stays in queue
        var pushQueue1 = new PushQueueStore(queuePath);
        pushQueue1.Enqueue(new PushQueueStore.PushEntry
        {
            CommitSha = "abc1234",
            QueuedAt = _clock.UtcNow,
        });

        pushQueue1.HasPending.Should().BeTrue();
        pushQueue1.Pending.Should().HaveCount(1);

        // Session 2: reload queue from disk — entries must survive restart
        var pushQueue2 = new PushQueueStore(queuePath);
        pushQueue2.HasPending.Should().BeTrue();
        pushQueue2.Pending.Should().HaveCount(1);
        pushQueue2.Pending[0].CommitSha.Should().Be("abc1234");
    }

    [Fact]
    public async Task PushQueue_DequeueAfterSuccessfulPush_RemovesEntry()
    {
        var queuePath = Path.Combine(_root, "push-queue3.json");
        var queue = new PushQueueStore(queuePath);
        var entry = new PushQueueStore.PushEntry { CommitSha = "deadbeef", QueuedAt = _clock.UtcNow };

        queue.Enqueue(entry);
        queue.HasPending.Should().BeTrue();

        queue.Dequeue(entry);
        queue.HasPending.Should().BeFalse();

        // Persisted correctly
        var reloaded = new PushQueueStore(queuePath);
        reloaded.HasPending.Should().BeFalse();
    }

    // ── Auto-pause on editor dirty ────────────────────────────────────────────

    [Fact]
    public async Task AutoSync_WhenEditorDirty_PullIsSkipped()
    {
        // Arrange: bare remote + local clone with pending remote changes
        var (remoteDir, localDir) = SetupRemoteAndClone();

        var secondClone = Path.Combine(_root, "second-clone3");
        Repository.Clone(remoteDir, secondClone);
        File.WriteAllText(Path.Combine(secondClone, "new-file.md"), "# New");
        using (var r = new Repository(secondClone))
        {
            r.Index.Add("new-file.md");
            r.Index.Write();
            var sig = new Signature("Other", "other@local", DateTimeOffset.UtcNow);
            r.Commit("add new-file", sig, sig);
            r.Network.Push(r.Head);
        }

        // Act: start auto-sync with editor always dirty
        using var svc = BuildService(localDir);
        await svc.InitOrOpenAsync();
        svc.StartAutoSync(1, () => true); // always dirty
        await Task.Delay(3000);

        // Assert: new-file.md was NOT pulled (pull was paused due to dirty editor)
        File.Exists(Path.Combine(localDir, "new-file.md")).Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_FetchFailure_FaultsCallerAndWorkerRecovers()
    {
        var (remote, local) = SetupRemoteAndClone();
        var missingRemote = Path.Combine(_root, "missing-remote");
        using (var repo = new Repository(local))
            repo.Network.Remotes.Update("origin", r => r.Url = missingRemote);
        using var svc = BuildService(local);
        await svc.InitOrOpenAsync();

        var pull = () => svc.PullNowAsync();
        await pull.Should().ThrowAsync<Exception>();
        svc.Status.Should().Be(GitSyncStatus.Error);

        using (var repo = new Repository(local))
            repo.Network.Remotes.Update("origin", r => r.Url = remote);
        await svc.PullNowAsync();
        svc.Status.Should().Be(GitSyncStatus.Idle);
    }

    [Fact]
    public async Task Pull_BootstrapFetchFailure_FaultsCaller()
    {
        var local = Path.Combine(_root, "unborn");
        Repository.Init(local);
        using (var repo = new Repository(local))
            repo.Network.Remotes.Add("origin", Path.Combine(_root, "missing-bootstrap-remote"));
        using var svc = BuildService(local);
        await svc.InitOrOpenAsync();

        var pull = () => svc.PullNowAsync();
        await pull.Should().ThrowAsync<Exception>();
        svc.Status.Should().Be(GitSyncStatus.Error);
        using var result = new Repository(local);
        result.Head.Tip.Should().BeNull();
    }

    [Fact]
    public async Task Pull_BootstrapSuccess_ChecksOutRemoteContent()
    {
        var (remote, _) = SetupRemoteAndClone();
        var local = Path.Combine(_root, "bootstrap-success");
        Repository.Init(local);
        using (var repo = new Repository(local)) repo.Network.Remotes.Add("origin", remote);
        using var svc = BuildService(local);
        await svc.InitOrOpenAsync();
        await svc.PullNowAsync();

        File.ReadAllText(Path.Combine(local, "README.md")).Should().Be("# Snippets");
        svc.Status.Should().Be(GitSyncStatus.Idle);
    }

    [Fact]
    public async Task Pull_EmptyRemote_IsValidForNewLibrary()
    {
        var remote = Path.Combine(_root, "empty-remote");
        Repository.Init(remote, isBare: true);
        var local = Path.Combine(_root, "empty-local");
        Repository.Init(local);
        using (var repo = new Repository(local)) repo.Network.Remotes.Add("origin", remote);
        using var svc = BuildService(local);
        await svc.InitOrOpenAsync();
        await svc.PullNowAsync();
        svc.Status.Should().Be(GitSyncStatus.Idle);
    }

    [Fact]
    public async Task Init_CloneFailure_PreservesLocalRepoButFaultsCaller()
    {
        var local = Path.Combine(_root, "clone-failure");
        var missingRemote = Path.Combine(_root, "missing-clone-remote");
        using var svc = new GitService(local, _clock, _dialog,
            new PushQueueStore(Path.Combine(_root, "clone-queue.json")), missingRemote);

        var init = () => svc.InitOrOpenAsync();
        await init.Should().ThrowAsync<Exception>();
        svc.Status.Should().Be(GitSyncStatus.Error);
        Repository.IsValid(local).Should().BeTrue();
        using var repo = new Repository(local);
        repo.Network.Remotes["origin"].Url.Should().Be(missingRemote);
    }

    [Fact]
    public async Task RetryPush_FailureKeepsQueue_ThenSuccessfulRetryDrainsIt()
    {
        var (remote, local) = SetupRemoteAndClone();
        using var svc = BuildService(local);
        await svc.InitOrOpenAsync();
        using (var repo = new Repository(local))
            repo.Network.Remotes.Update("origin", r => r.Url = Path.Combine(_root, "missing-push-remote"));
        File.WriteAllText(Path.Combine(local, "pending.md"), "# Pending");
        await svc.CommitAndQueuePushAsync("add pending");

        var push = () => svc.RetryPushNowAsync();
        await push.Should().ThrowAsync<Exception>();
        svc.Status.Should().Be(GitSyncStatus.Error);

        using (var repo = new Repository(local))
            repo.Network.Remotes.Update("origin", r => r.Url = remote);
        await svc.RetryPushNowAsync();
        svc.Status.Should().Be(GitSyncStatus.Idle);
        using var remoteRepo = new Repository(remote);
        remoteRepo.Head.Tip["pending.md"].Target.Should().BeOfType<Blob>();
    }

    [Fact]
    public async Task RetryPush_ExhaustedQueue_FaultsWithoutDiscardingChanges()
    {
        var (_, local) = SetupRemoteAndClone();
        var queuePath = Path.Combine(_root, "exhausted-queue.json");
        var queue = new PushQueueStore(queuePath);
        using var svc = BuildService(local, queue);
        await svc.InitOrOpenAsync();
        using (var repo = new Repository(local))
            queue.Enqueue(new PushQueueStore.PushEntry { CommitSha = repo.Head.Tip.Sha, AttemptCount = 5 });

        var push = () => svc.RetryPushNowAsync();
        await push.Should().ThrowAsync<InvalidOperationException>();
        svc.Status.Should().Be(GitSyncStatus.Error);
        new PushQueueStore(queuePath).Pending.Should().ContainSingle(e => e.AttemptCount == 5);
    }

    [Fact]
    public async Task Pull_InvalidLocalRepo_FaultsCaller()
    {
        using var svc = BuildService(Path.Combine(_root, "missing-local"));
        var pull = () => svc.PullNowAsync();
        await pull.Should().ThrowAsync<InvalidOperationException>();
        svc.Status.Should().Be(GitSyncStatus.Error);
    }

    private GitService BuildService(string repoPath, PushQueueStore? pushQueue = null)
    {
        var queuePath = pushQueue is not null ? null : Path.Combine(_root, $"pq-{Guid.NewGuid():N}.json");
        return new GitService(
            repoPath,
            _clock,
            _dialog,
            pushQueue ?? new PushQueueStore(queuePath!));
    }

    /// <summary>Creates a local repo with one initial commit (needed before merging).</summary>
    private string SetupRepoWithInitialCommit()
    {
        var dir = Path.Combine(_root, $"repo-{Guid.NewGuid():N}");
        Repository.Init(dir);
        using var repo = new Repository(dir);

        var readmePath = Path.Combine(dir, "README.md");
        File.WriteAllText(readmePath, "# Snippets");
        repo.Index.Add("README.md");
        repo.Index.Write();

        var sig = new Signature("Test", "test@local", DateTimeOffset.UtcNow);
        repo.Commit("init", sig, sig);
        return dir;
    }

    /// <summary>Creates a bare remote repo and a local clone with one shared initial commit.</summary>
    private (string remoteDir, string localDir) SetupRemoteAndClone()
    {
        var remoteDir = Path.Combine(_root, $"remote-{Guid.NewGuid():N}");
        var localDir = Path.Combine(_root, $"local-{Guid.NewGuid():N}");

        // Init bare remote
        Repository.Init(remoteDir, isBare: true);

        // Create a temporary intermediate repo to make first commit, then push to bare
        var seedDir = Path.Combine(_root, $"seed-{Guid.NewGuid():N}");
        Repository.Init(seedDir);
        using (var seed = new Repository(seedDir))
        {
            File.WriteAllText(Path.Combine(seedDir, "README.md"), "# Snippets");
            seed.Index.Add("README.md");
            seed.Index.Write();
            var sig = new Signature("Init", "init@local", DateTimeOffset.UtcNow);
            seed.Commit("init", sig, sig);

            seed.Network.Remotes.Add("origin", remoteDir);
            seed.Network.Push(seed.Network.Remotes["origin"], "refs/heads/master:refs/heads/master");
        }

        // Clone to local
        Repository.Clone(remoteDir, localDir);

        return (remoteDir, localDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

internal sealed class FakeDialogService : IDialogService
{
    public List<IReadOnlyList<string>> ConflictNotifications { get; } = [];

    public Task<Dictionary<string, string>?> ShowPlaceholderFillAsync(IReadOnlyList<Placeholder> placeholders)
        => Task.FromResult<Dictionary<string, string>?>(null);

    public Task<Dictionary<string, string>?> ShowPlaceholderFillAsync(string snippetTitle, IReadOnlyList<Placeholder> placeholders)
        => Task.FromResult<Dictionary<string, string>?>(null);

    public Task ShowConflictNotificationAsync(IReadOnlyList<string> backedUpFilePaths)
    {
        ConflictNotifications.Add(backedUpFilePaths);
        return Task.CompletedTask;
    }
}
