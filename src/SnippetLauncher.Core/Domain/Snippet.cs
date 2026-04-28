namespace SnippetLauncher.Core.Domain;

public sealed record Snippet(
    string Id,
    string Title,
    IReadOnlyList<string> Tags,
    string Body,
    IReadOnlyList<Placeholder> Placeholders,
    DateTimeOffset Created,
    DateTimeOffset Updated);
