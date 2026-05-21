using FluentAssertions;
using SnippetLauncher.Core.Settings;
using SnippetLauncher.Core.Updates;

namespace SnippetLauncher.Core.Tests;

public class UpdateNotificationServiceTests
{
    private static readonly Version Current = new(1, 0, 4);

    private static SettingsService FreshSettings(bool checkEnabled = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "snippet-launcher-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var svc = new SettingsService(dir);
        svc.Current.UpdateCheckEnabled = checkEnabled;
        return svc;
    }

    [Fact]
    public async Task CheckOnceAsync_skips_when_UpdateCheckEnabled_is_false()
    {
        var check = new RecordingCheck(new UpdateCheckResult(new Version(2, 0, 0), "url", null));
        await using var svc = new UpdateNotificationService(
            check, FreshSettings(checkEnabled: false), Current);
        var raised = 0;
        svc.UpdateAvailable += _ => raised++;

        var result = await svc.CheckOnceAsync(CancellationToken.None);

        check.Calls.Should().Be(0);
        raised.Should().Be(0);
        result.NewVersion.Should().BeNull();
    }

    [Fact]
    public async Task CheckOnceAsync_raises_event_when_update_available()
    {
        var newVer = new Version(1, 1, 0);
        var check = new RecordingCheck(new UpdateCheckResult(newVer, "u", null));
        await using var svc = new UpdateNotificationService(check, FreshSettings(), Current);
        UpdateCheckResult? captured = null;
        svc.UpdateAvailable += r => captured = r;

        await svc.CheckOnceAsync(CancellationToken.None);

        check.Calls.Should().Be(1);
        captured.Should().NotBeNull();
        captured!.NewVersion.Should().Be(newVer);
    }

    [Fact]
    public async Task CheckOnceAsync_does_not_raise_event_when_no_update()
    {
        var check = new RecordingCheck(new UpdateCheckResult(null, null, null));
        await using var svc = new UpdateNotificationService(check, FreshSettings(), Current);
        var raised = 0;
        svc.UpdateAvailable += _ => raised++;

        await svc.CheckOnceAsync(CancellationToken.None);

        check.Calls.Should().Be(1);
        raised.Should().Be(0);
    }

    [Fact]
    public async Task Loop_invokes_check_multiple_times_at_short_interval()
    {
        var check = new RecordingCheck(new UpdateCheckResult(null, null, null));
        await using var svc = new UpdateNotificationService(
            check, FreshSettings(), Current,
            initialDelay: TimeSpan.Zero,
            interval: TimeSpan.FromMilliseconds(50));

        svc.Start();
        await Task.Delay(250);

        check.Calls.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task DisposeAsync_cancels_loop_cleanly()
    {
        var check = new RecordingCheck(new UpdateCheckResult(null, null, null));
        var svc = new UpdateNotificationService(
            check, FreshSettings(), Current,
            initialDelay: TimeSpan.Zero,
            interval: TimeSpan.FromMilliseconds(20));
        svc.Start();
        await Task.Delay(80);

        var act = svc.DisposeAsync;

        await act.Invoke();
        // Reaching this line means the loop drained without exception.
    }

    private sealed class RecordingCheck : IUpdateCheckService
    {
        private readonly UpdateCheckResult _result;
        public int Calls { get; private set; }

        public RecordingCheck(UpdateCheckResult result) { _result = result; }

        public Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
