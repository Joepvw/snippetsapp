namespace SnippetLauncher.App.Services;

/// <summary>Bounds local shutdown phases without cancelling or discarding accepted writes.</summary>
public static class ShutdownDrain
{
    public static async Task RunAsync(Task repositoryDrain, Func<Task>? commit, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await repositoryDrain.WaitAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (commit is not null) await commit().WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Afsluiten duurt te lang; opgeslagen bestanden blijven beschikbaar voor herstel.");
        }
    }
}
