using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.Core.Tests;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
