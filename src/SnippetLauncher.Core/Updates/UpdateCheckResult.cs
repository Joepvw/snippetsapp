namespace SnippetLauncher.Core.Updates;

public sealed record UpdateCheckResult(
    Version? NewVersion,
    string? ReleaseUrl,
    string? FailureReason);
