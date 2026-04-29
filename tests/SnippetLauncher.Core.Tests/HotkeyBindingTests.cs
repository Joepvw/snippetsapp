using FluentAssertions;
using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.Core.Tests;

public sealed class HotkeyBindingTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("Ctrl+Shift+N")]
    [InlineData("Alt+F4")]
    [InlineData("Ctrl+Alt+A")]
    public void TryParse_ValidBinding_ReturnsValue(string input)
        => HotkeyBinding.TryParse(input).Should().NotBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space")]           // no modifier
    [InlineData("Ctrl+Unknown")]    // unknown key
    public void TryParse_InvalidBinding_ReturnsNull(string? input)
        => HotkeyBinding.TryParse(input).Should().BeNull();

    [Fact]
    public void TryParse_RoundTrip_ToString_Parses()
    {
        var original = HotkeyBinding.TryParse("Ctrl+Shift+Space")!.Value;
        var str = original.ToString();
        var parsed = HotkeyBinding.TryParse(str);

        parsed.Should().NotBeNull();
        parsed!.Value.Should().Be(original);
    }

    [Fact]
    public void TryParse_SetsCorrectModifiers()
    {
        var b = HotkeyBinding.TryParse("Ctrl+Shift+Space")!.Value;
        b.ToString().Should().Contain("Ctrl");
        b.ToString().Should().Contain("Shift");
    }
}
