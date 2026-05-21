namespace SnippetLauncher.Core.Updates;

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct);
}
