namespace SnippetLauncher.Core.Sync;

public static class GitExecutable
{
    public static string? Find() => Find(Environment.GetEnvironmentVariable("PATH"), @"C:\Program Files\Git");

    public static string? Find(string? searchPath, string installationDirectory)
    {
        foreach (var dir in (searchPath ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "git.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return new[] { Path.Combine(installationDirectory, "bin", "git.exe"),
            Path.Combine(installationDirectory, "cmd", "git.exe") }.FirstOrDefault(File.Exists);
    }
}
