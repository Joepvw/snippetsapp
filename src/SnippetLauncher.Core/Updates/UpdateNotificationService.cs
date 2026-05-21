using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.Core.Updates;

/// <summary>
/// Periodically asks <see cref="IUpdateCheckService"/> whether a newer release
/// is available and raises <see cref="UpdateAvailable"/> when one is.
///
/// Owns a single loop task driven by <see cref="PeriodicTimer"/>; cancellation
/// flows through <see cref="DisposeAsync"/> so an in-flight HTTP call aborts
/// cleanly at shutdown.
///
/// Lives in Core because nothing here is WPF-specific. App-side code subscribes
/// to <see cref="UpdateAvailable"/> to show a tray notification or update the
/// context-menu.
/// </summary>
public sealed class UpdateNotificationService : IAsyncDisposable
{
    private readonly IUpdateCheckService _check;
    private readonly SettingsService _settings;
    private readonly Version _currentVersion;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public UpdateNotificationService(
        IUpdateCheckService check,
        SettingsService settings,
        Version currentVersion,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null)
    {
        _check = check;
        _settings = settings;
        _currentVersion = currentVersion;
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(30);
        _interval = interval ?? TimeSpan.FromHours(24);
    }

    public void Start()
    {
        if (_loop is not null) return;
        _loop = RunLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Runs one check immediately, bypassing the loop. Returns the result so
    /// callers (or tests) can inspect it directly. Also raises
    /// <see cref="UpdateAvailable"/> if applicable, so subscribers see the
    /// same signal they'd get from the timer.
    /// </summary>
    public async Task<UpdateCheckResult> CheckOnceAsync(CancellationToken ct)
    {
        if (!_settings.Current.UpdateCheckEnabled)
            return new UpdateCheckResult(null, null, null);

        var result = await _check.CheckAsync(_currentVersion, ct).ConfigureAwait(false);
        if (result.NewVersion is not null)
            UpdateAvailable?.Invoke(result);
        return result;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            if (_initialDelay > TimeSpan.Zero)
                await Task.Delay(_initialDelay, ct).ConfigureAwait(false);

            await CheckOnceAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await CheckOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown — expected.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* swallow shutdown noise */ }
        }
        _cts.Dispose();
    }
}
