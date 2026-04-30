using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.Core.Abstractions;

public interface IGitService : IDisposable
{
    GitSyncStatus Status { get; }
    event EventHandler<GitSyncStatus>? StatusChanged;

    Task InitOrOpenAsync();
    Task CommitAndQueuePushAsync(string message);
    Task RetryPushNowAsync();
    Task PullNowAsync();
    void StartAutoSync(int pullIntervalSeconds, Func<bool> isEditorDirty);
}
