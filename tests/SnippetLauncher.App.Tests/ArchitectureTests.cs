using FluentAssertions;
using NetArchTest.Rules;

namespace SnippetLauncher.App.Tests;

/// <summary>
/// Enforces that SnippetLauncher.Core has no references to WPF assemblies.
/// This keeps Core portable for a future Avalonia/Mac port.
/// </summary>
public sealed class ArchitectureTests
{
    private const string CoreAssembly = "SnippetLauncher.Core";

    [Fact]
    public void Core_ShouldNotReferenceWpf()
    {
        var result = Types.InAssembly(typeof(SnippetLauncher.Core.Domain.Snippet).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "PresentationFramework",
                "PresentationCore",
                "WindowsBase",
                "System.Windows")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Core assembly '{CoreAssembly}' must not reference WPF. " +
            $"Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
