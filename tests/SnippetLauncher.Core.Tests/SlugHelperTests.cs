using FluentAssertions;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.Core.Tests;

public sealed class SlugHelperTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("FAQ — Prijzen", "faq-prijzen")]
    [InlineData("  spaces  ", "spaces")]
    [InlineData("a!@#b", "a-b")]
    [InlineData("", "snippet")]
    [InlineData("   ", "snippet")]
    public void Slugify_VariousInputs(string input, string expected)
        => SlugHelper.Slugify(input).Should().Be(expected);

    [Fact]
    public void Slugify_LongTitle_TruncatesAt60()
    {
        var long_ = new string('a', 80);
        SlugHelper.Slugify(long_).Length.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(60);
    }

    [Fact]
    public void UniqueSlug_NoCollision_ReturnsBase()
    {
        var slug = SlugHelper.UniqueSlug("Hello World", []);
        slug.Should().Be("hello-world");
    }

    [Fact]
    public void UniqueSlug_CollisionOnBase_AppendsSuffix()
    {
        var slug = SlugHelper.UniqueSlug("Hello World", ["hello-world"]);
        slug.Should().Be("hello-world-2");
    }

    [Fact]
    public void UniqueSlug_MultipleCollisions_FindsFreeSlot()
    {
        var existing = new[] { "hello-world", "hello-world-2", "hello-world-3" };
        var slug = SlugHelper.UniqueSlug("Hello World", existing);
        slug.Should().Be("hello-world-4");
    }
}
