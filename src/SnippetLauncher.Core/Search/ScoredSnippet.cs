using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.Core.Search;

public sealed record ScoredSnippet(Snippet Snippet, double Score);
