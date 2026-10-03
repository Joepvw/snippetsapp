using FluentAssertions;
using SnippetLauncher.Core.Sync;
namespace SnippetLauncher.Core.Tests;

public sealed class CredentialProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "credential-test-" + Guid.NewGuid().ToString("N"));
    public CredentialProviderTests() => Directory.CreateDirectory(_root);
    private GitCredentialProvider Helper(string script, TimeSpan? timeout = null)
    {
        var path = Path.Combine(_root, "helper.ps1"); File.WriteAllText(path, script);
        return new GitCredentialProvider(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            timeout ?? TimeSpan.FromSeconds(20), timeout ?? TimeSpan.FromSeconds(20), ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path]);
    }
    [Fact]
    public async Task SilentHelperDisablesInteractionAndReceivesRepositoryContext()
    {
        var provider = Helper("$inputText = [Console]::In.ReadToEnd(); if ($env:GCM_INTERACTIVE -ne 'false' -or $env:GIT_TERMINAL_PROMPT -ne '0' -or $inputText -notmatch 'path=owner/repo') { exit 2 }; 'username=synthetic'; 'password=synthetic-secret'");
        var credential = await provider.GetAsync(_root, "https://example.test/owner/repo", false, default);
        credential!.Username.Should().Be("synthetic");
    }
    [Fact]
    public async Task InteractiveHelperIsExplicitAndReturnsCredential()
    {
        var provider = Helper("[Console]::In.ReadToEnd() | Out-Null; if ($env:GCM_INTERACTIVE -ne 'true') { exit 2 }; 'username=synthetic'; 'password=synthetic-secret'");
        (await provider.GetAsync(_root, "https://example.test/repo", true, default)).Should().NotBeNull();
    }
    [Fact]
    public async Task HangingStdoutIsBoundedAndProcessKilled()
    {
        var marker = Path.Combine(_root, "pid.txt");
        var provider = Helper("[IO.File]::WriteAllText('" + marker + "', [string]$PID); [Console]::In.ReadToEnd() | Out-Null; Start-Sleep -Seconds 60", TimeSpan.FromSeconds(2));
        var call = () => provider.GetAsync(_root, "https://example.test/repo", false, default).WaitAsync(TimeSpan.FromSeconds(8));
        await call.Should().ThrowAsync<GitAuthenticationException>();
        if (File.Exists(marker))
        {
            var pid = int.Parse(File.ReadAllText(marker));
            var running = System.Diagnostics.Process.GetProcesses().Any(p => p.Id == pid);
            running.Should().BeFalse();
        }
    }
    [Fact]
    public async Task HelperFailureDoesNotExposeOutputSecrets()
    {
        var provider = Helper("[Console]::In.ReadToEnd() | Out-Null; [Console]::Error.WriteLine('synthetic-secret'); exit 1");
        var call = () => provider.GetAsync(_root, "https://example.test/repo", false, default);
        var error = await call.Should().ThrowAsync<GitAuthenticationException>();
        error.Which.ToString().Should().NotContain("synthetic-secret");
    }
    [Fact]
    public async Task MissingHelperIsTypedAndSafe()
    {
        var provider = new GitCredentialProvider(Path.Combine(_root, "missing.exe"));
        var call = () => provider.GetAsync(_root, "https://example.test/repo", false, default);
        await call.Should().ThrowAsync<GitAuthenticationException>();
    }
    [Fact]
    public async Task HelperCannotSubstituteDifferentRepositoryContext()
    {
        var provider = Helper("[Console]::In.ReadToEnd() | Out-Null; 'host=other.test'; 'username=synthetic'; 'password=synthetic-secret'");
        var call = () => provider.GetAsync(_root, "https://example.test/repo", false, default);
        await call.Should().ThrowAsync<GitAuthenticationException>();
    }
    [Fact]
    public async Task CancellationEndsInteractiveHelper()
    {
        var provider = Helper("[Console]::In.ReadToEnd() | Out-Null; Start-Sleep -Seconds 60");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var call = () => provider.GetAsync(_root, "https://example.test/repo", true, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8));
        await call.Should().ThrowAsync<GitAuthenticationException>();
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyRemoteDisablesSync(string url) => RemoteUrlValidator.Validate(url).Should().BeEmpty();
    [Theory]
    [InlineData("https://user:synthetic-secret@example.test/repo")]
    [InlineData("https://example.test/repo?token=synthetic-secret")]
    [InlineData("https://example.test/repo#synthetic-secret")]
    public void UnsafeRemoteDoesNotExposeSecret(string url)
    {
        var call = () => RemoteUrlValidator.Validate(url);
        call.Should().Throw<ArgumentException>().Which.ToString().Should().NotContain("synthetic-secret");
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
