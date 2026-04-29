namespace SnippetLauncher.Core.Settings;

public sealed class AppSettings
{
    public string SnippetsDirectory { get; set; } = "";
    public string SearchHotkey { get; set; } = "Ctrl+Shift+Space";
    public string QuickAddHotkey { get; set; } = "Ctrl+Shift+N";
    public int PullIntervalSeconds { get; set; } = 60;
    public string Theme { get; set; } = "System"; // Light, Dark, System
    public bool IsFirstRun { get; set; } = true;
    public bool StartAtLoginEnabled { get; set; } = false;
}
