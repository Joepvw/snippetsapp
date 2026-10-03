namespace SnippetLauncher.Core.Abstractions;

public sealed record GitCredential(string Username, string Password)
{
    public override string ToString() => "GitCredential [redacted]";
}
public interface IGitCredentialProvider
{
    Task<GitCredential?> GetAsync(string repositoryPath, string url, bool interactive, CancellationToken cancellationToken);
    Task ApproveAsync(string repositoryPath, string url, GitCredential credential, CancellationToken cancellationToken);
}
