using FluentAssertions;
using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.Core.Tests;

public sealed class GitExecutableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    [Theory]
    [InlineData("bin")]
    [InlineData("cmd")]
    public void Find_ProgramFilesFallback_WorksWithoutPath(string subfolder)
    {
        var executable = CreateExecutable(Path.Combine(_root, "Program Files", "Git", subfolder));
        GitExecutable.Find("", Path.Combine(_root, "Program Files", "Git")).Should().Be(executable);
    }

    [Fact]
    public void Find_PathTakesPrecedence()
    {
        var onPath = CreateExecutable(Path.Combine(_root, "path"));
        CreateExecutable(Path.Combine(_root, "Git", "bin"));
        GitExecutable.Find($"; {Path.GetDirectoryName(onPath)} ;", Path.Combine(_root, "Git")).Should().Be(onPath);
    }

    [Fact]
    public void Find_AbsentGit_ReturnsNull()
    {
        GitExecutable.Find(null, Path.Combine(_root, "missing")).Should().BeNull();
    }

    private static string CreateExecutable(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "git.exe");
        File.WriteAllText(path, "synthetic executable marker");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
