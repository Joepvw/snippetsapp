using FluentAssertions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Tests;

public sealed class SnippetSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new Snippet(
            Id: "faq-pricing-nl",
            Title: "FAQ — Prijzen NL",
            Tags: ["faq", "pricing"],
            Body: "Hoi {customer_name}, zie https://example.com",
            Placeholders: [new Placeholder("customer_name", "Klantnaam", "Klant")],
            Created: new DateTimeOffset(2026, 4, 28, 10, 0, 0, TimeSpan.Zero),
            Updated: new DateTimeOffset(2026, 4, 28, 11, 0, 0, TimeSpan.Zero));

        var serialized = SnippetSerializer.Serialize(original);
        var restored = SnippetSerializer.Deserialize(original.Id, serialized);

        restored.Id.Should().Be(original.Id);
        restored.Title.Should().Be(original.Title);
        restored.Tags.Should().BeEquivalentTo(original.Tags);
        restored.Body.Should().Be(original.Body);
        restored.Placeholders.Should().HaveCount(1);
        restored.Placeholders[0].Name.Should().Be("customer_name");
        restored.Placeholders[0].Label.Should().Be("Klantnaam");
        restored.Placeholders[0].Default.Should().Be("Klant");
        restored.Created.Should().Be(original.Created);
        restored.Updated.Should().Be(original.Updated);
    }

    [Fact]
    public void Deserialize_NoFrontmatter_UsesTitleFromId()
    {
        var result = SnippetSerializer.Deserialize("my-snippet", "Just a body.");
        result.Id.Should().Be("my-snippet");
        result.Title.Should().Be("my-snippet");
        result.Body.Should().Be("Just a body.");
    }

    [Fact]
    public void Deserialize_EmptyTags_ReturnsEmptyList()
    {
        var md = "---\ntitle: Test\n---\nBody";
        var result = SnippetSerializer.Deserialize("test", md);
        result.Tags.Should().BeEmpty();
        result.Placeholders.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_MalformedYaml_ThrowsException()
    {
        var md = "---\n: : invalid yaml : :\n---\nBody";
        var act = () => SnippetSerializer.Deserialize("bad", md);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Serialize_ProducesFrontmatterWithTripleDash()
    {
        var snippet = new Snippet("x", "X", [], "body", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var result = SnippetSerializer.Serialize(snippet);
        result.Should().StartWith("---\n");
        result.Should().Contain("---\nbody");
    }
}
