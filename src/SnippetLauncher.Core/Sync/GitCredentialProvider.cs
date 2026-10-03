using System.Diagnostics;
using SnippetLauncher.Core.Abstractions;
namespace SnippetLauncher.Core.Sync;

public sealed class GitCredentialProvider(string? executable = null, TimeSpan? silentTimeout = null, TimeSpan? interactiveTimeout = null, IReadOnlyList<string>? executableArguments = null) : IGitCredentialProvider
{
    public async Task<GitCredential?> GetAsync(string repositoryPath, string url, bool interactive, CancellationToken cancellationToken)
    {
        var output = await RunAsync(repositoryPath, url, "fill", null, interactive, cancellationToken);
        var values = output.Split('\n').Select(line => line.TrimEnd('\r').Split('=', 2)).Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.Last()[1]);
        var uri = new Uri(RemoteUrlValidator.Validate(url));
        if ((values.TryGetValue("protocol", out var protocol) && protocol != uri.Scheme) ||
            (values.TryGetValue("host", out var host) && !host.Equals(uri.Authority, StringComparison.OrdinalIgnoreCase)) ||
            (values.TryGetValue("path", out var path) && path.TrimStart('/') != uri.AbsolutePath.TrimStart('/')))
            throw new GitAuthenticationException("Aanmeldhulp gaf een andere repositorycontext terug.");
        return values.TryGetValue("username", out var user) && values.TryGetValue("password", out var password)
            ? new GitCredential(user, password) : null;
    }
    public async Task ApproveAsync(string repositoryPath, string url, GitCredential credential, CancellationToken cancellationToken)
        => _ = await RunAsync(repositoryPath, url, "approve", credential, false, cancellationToken);
    private async Task<string> RunAsync(string repositoryPath, string url, string operation, GitCredential? credential, bool interactive, CancellationToken cancellationToken)
    {
        var git = executable ?? GitExecutable.Find();
        if (git is null) throw new GitAuthenticationException("Git Credential Manager is niet beschikbaar. Installeer Git voor Windows.");
        var uri = new Uri(RemoteUrlValidator.Validate(url));
        var input = $"protocol={uri.Scheme}\nhost={uri.Authority}\npath={uri.AbsolutePath.TrimStart('/')}\n";
        if (credential is not null && (credential.Username.IndexOfAny(['\r', '\n']) >= 0 || credential.Password.IndexOfAny(['\r', '\n']) >= 0))
            throw new GitAuthenticationException("Ongeldige credentialcontext.");
        if (credential is not null) input += $"username={credential.Username}\npassword={credential.Password}\n";
        input += "\n";
        var start = new ProcessStartInfo(git)
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (executableArguments is not null) foreach (var argument in executableArguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("credential"); start.ArgumentList.Add(operation);
        start.Environment["GCM_INTERACTIVE"] = interactive ? "true" : "false";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(interactive ? interactiveTimeout ?? TimeSpan.FromMinutes(2) : silentTimeout ?? TimeSpan.FromSeconds(5));
        Process process;
        try { process = Process.Start(start) ?? throw new GitAuthenticationException("Aanmeldhulp kon niet starten."); }
        catch (System.ComponentModel.Win32Exception) { throw new GitAuthenticationException("Aanmeldhulp niet beschikbaar. Controleer Git voor Windows."); }
        using var ownedProcess = process;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            if (process.ExitCode != 0) throw new GitAuthenticationException("Aanmelding nodig of geannuleerd. Kies Aanmelden om opnieuw te proberen.");
            return await stdout;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new GitAuthenticationException("Aanmelding geannuleerd of verlopen.");
        }
    }
}
public sealed class GitAuthenticationException(string message) : Exception(message);
