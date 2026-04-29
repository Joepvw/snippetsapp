using FluentAssertions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Placeholders;

namespace SnippetLauncher.Core.Tests;

public sealed class PlaceholderEngineTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 4, 28, 14, 30, 0, TimeSpan.Zero));
    private PlaceholderEngine Engine => new(_clock);

    // ── Resolve built-ins ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DateToken_ReturnsFormattedDate()
    {
        var result = Engine.Resolve("Today is {date}", Empty, null);
        result.Should().Be("Today is 2026-04-28");
    }

    [Fact]
    public void Resolve_TimeToken_ReturnsFormattedTime()
    {
        var result = Engine.Resolve("Time: {time}", Empty, null);
        // Local time may vary, just verify format
        result.Should().MatchRegex(@"Time: \d{2}:\d{2}");
    }

    [Fact]
    public void Resolve_ClipboardToken_InjectsSnapshot()
    {
        var result = Engine.Resolve("Clip: {clipboard}", Empty, "hello clipboard");
        result.Should().Be("Clip: hello clipboard");
    }

    [Fact]
    public void Resolve_ClipboardToken_NullSnapshot_ReturnsEmpty()
    {
        var result = Engine.Resolve("{clipboard}", Empty, null);
        result.Should().BeEmpty();
    }

    // ── Resolve custom tokens ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_CustomToken_ReplacesWithValue()
    {
        var values = new Dictionary<string, string> { ["name"] = "Alice" };
        var result = Engine.Resolve("Hello {name}!", values, null);
        result.Should().Be("Hello Alice!");
    }

    [Fact]
    public void Resolve_UnknownCustomToken_KeepsOriginal()
    {
        var result = Engine.Resolve("Hello {unknown}!", Empty, null);
        result.Should().Be("Hello {unknown}!");
    }

    // ── Escape sequences ──────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DoubleBrace_RendersLiteralBrace()
    {
        var result = Engine.Resolve("{{not a token}}", Empty, null);
        result.Should().Be("{not a token}");
    }

    [Fact]
    public void Resolve_UnclosedBrace_PassesThrough()
    {
        var result = Engine.Resolve("hello {world", Empty, null);
        result.Should().Be("hello {world");
    }

    // ── ExtractCustomNames ────────────────────────────────────────────────────

    [Fact]
    public void ExtractCustomNames_ReturnsOnlyNonBuiltIn()
    {
        var names = Engine.ExtractCustomNames("{date} {name} {time} {customer}");
        names.Should().BeEquivalentTo(["name", "customer"]);
    }

    [Fact]
    public void ExtractCustomNames_DeduplicatesNames()
    {
        var names = Engine.ExtractCustomNames("{name} and {name} again");
        names.Should().ContainSingle(n => n == "name");
    }

    [Fact]
    public void ExtractCustomNames_NoTokens_ReturnsEmpty()
    {
        Engine.ExtractCustomNames("plain text").Should().BeEmpty();
    }

    // ── HasPlaceholders ───────────────────────────────────────────────────────

    [Fact]
    public void HasPlaceholders_WithToken_ReturnsTrue()
        => Engine.HasPlaceholders("{name}").Should().BeTrue();

    [Fact]
    public void HasPlaceholders_NoToken_ReturnsFalse()
        => Engine.HasPlaceholders("plain text").Should().BeFalse();

    // ── MergeWithDeclared ─────────────────────────────────────────────────────

    [Fact]
    public void MergeWithDeclared_UsesLabelFromDeclared()
    {
        var declared = new List<Placeholder> { new("name", "Klantnaam", "standaard") };
        var merged = Engine.MergeWithDeclared("{name}", declared);

        merged.Should().ContainSingle();
        merged[0].Label.Should().Be("Klantnaam");
        merged[0].Default.Should().Be("standaard");
    }

    [Fact]
    public void MergeWithDeclared_UndeclaredToken_GetsGeneratedLabel()
    {
        var merged = Engine.MergeWithDeclared("{foo}", []);
        merged.Should().ContainSingle(p => p.Name == "foo" && p.Label == "foo");
    }

    [Fact]
    public void MergeWithDeclared_NoTokensInBody_ReturnsEmpty()
        => Engine.MergeWithDeclared("no tokens here", []).Should().BeEmpty();

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>();
}
