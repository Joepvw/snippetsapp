namespace SnippetLauncher.Core.Domain;

public sealed record SnippetUsage(
    int UsageCount,
    DateTimeOffset LastUsed,
    IReadOnlyList<string> MergedFrom);
