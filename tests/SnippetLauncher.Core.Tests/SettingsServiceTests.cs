using FluentAssertions;
using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SettingsServiceTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void CorruptSettings_RestoresLastGoodBackupWithoutReturningToWizard()
    {
        var settings = new SettingsService(_dir);
        settings.Current.SnippetsDirectory = Path.Combine(_dir, "snippets");
        settings.Current.IsFirstRun = false;
        settings.Save();
        settings.Current.Theme = "Dark";
        settings.Save();
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{");

        var recovered = new SettingsService(_dir);

        recovered.IsFirstRun.Should().BeFalse();
        recovered.Current.SnippetsDirectory.Should().Be(settings.Current.SnippetsDirectory);
        Directory.GetFiles(_dir, "settings.json.corrupt-*").Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(86401)]
    public void Save_InvalidPullInterval_IsRejectedWithoutReplacingGoodSettings(int interval)
    {
        var settings = new SettingsService(_dir);
        settings.Save();
        var original = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        settings.Current.PullIntervalSeconds = interval;

        settings.Invoking(s => s.Save()).Should().Throw<ArgumentOutOfRangeException>();
        File.ReadAllText(Path.Combine(_dir, "settings.json")).Should().Be(original);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void PendingDirectory_ChangesOnlyAtNextSettingsLoad()
    {
        var settings = new SettingsService(_dir);
        settings.Current.SnippetsDirectory = Path.Combine(_dir, "active");
        settings.Current.PendingSnippetsDirectory = Path.Combine(_dir, "next");
        settings.Save();

        settings.Current.SnippetsDirectory.Should().EndWith("active");
        var restarted = new SettingsService(_dir);
        restarted.Current.SnippetsDirectory.Should().EndWith("next");
        restarted.Current.PendingSnippetsDirectory.Should().BeNull();
    }

    [Fact]
    public void CorruptSettingsWithoutBackup_FailsExplicitlyAndPreservesFile()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{");
        var load = () => new SettingsService(_dir);
        load.Should().Throw<InvalidDataException>();
        File.ReadAllText(Path.Combine(_dir, "settings.json")).Should().Be("{");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyRemote_IsSupported(string remote)
    {
        var settings = new SettingsService(_dir);
        settings.Current.RemoteUrl = remote;
        settings.Save();
        new SettingsService(_dir).Current.RemoteUrl.Should().BeEmpty();
    }

    [Fact]
    public void UnsafeLegacyRemote_IsScrubbedEvenWithCorruptBackup()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{\"RemoteUrl\":\"https://user:synthetic-secret@example.test/repo\",\"SnippetsDirectory\":\"library\",\"IsFirstRun\":false}");
        File.WriteAllText(Path.Combine(_dir, "settings.json.bak"), "{");
        var settings = new SettingsService(_dir);
        settings.IsFirstRun.Should().BeFalse();
        settings.Current.RemoteUrl.Should().BeEmpty();
        File.ReadAllText(Path.Combine(_dir, "settings.json")).Should().NotContain("synthetic-secret");
    }
}
