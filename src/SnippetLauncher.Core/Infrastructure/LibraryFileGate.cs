namespace SnippetLauncher.Core.Infrastructure;

/// <summary>Serializes library filesystem changes across the repository and Git workers.</summary>
public sealed class LibraryFileGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IDisposable Enter(CancellationToken cancellationToken = default)
    {
        _gate.Wait(cancellationToken);
        return new Lease(_gate);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
        }
    }
}
