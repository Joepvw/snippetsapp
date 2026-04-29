using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace SnippetLauncher.App.Services;

public sealed class WindowsStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SnippetLauncher";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(value)) return false;
            return string.Equals(NormalizeQuoted(value), GetExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read autostart registry value");
            return false;
        }
    }

    public void Enable()
    {
        var exePath = GetExePath();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Kon Run-registersleutel niet openen.");
        key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        Log.Information("Autostart enabled: {Path}", exePath);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Information("Autostart disabled");
    }

    private static string GetExePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            path = Process.GetCurrentProcess().MainModule?.FileName;
        return path ?? throw new InvalidOperationException("Kon exe-pad niet bepalen.");
    }

    private static string NormalizeQuoted(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1];
        return trimmed;
    }
}
