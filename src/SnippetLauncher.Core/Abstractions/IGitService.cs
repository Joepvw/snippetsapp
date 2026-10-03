using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.Core.Abstractions;

public interface IGitService : IDisposable, IAsyncDisposable
{
    GitSyncStatus Status { get; }
    event EventHandler<GitSyncStatus>? StatusChanged;

    Task InitOrOpenAsync();
    Task AuthenticateAsync();
    void StopAutoSyncAndCancelNetwork();
    Task CommitAndQueuePushAsync(string message);
    Task RetryPushNowAsync();
    Task PullNowAsync();
    void StartAutoSync(int pullIntervalSeconds, Func<bool> isEditorDirty);
}
