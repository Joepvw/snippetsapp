namespace SnippetLauncher.Core.Sync;

public enum GitSyncStatus
{
    Idle,
    Syncing,
    Behind,
    Conflict,
    Error,
    NoRemote,
}
