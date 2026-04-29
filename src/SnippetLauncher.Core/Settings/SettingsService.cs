using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippetLauncher.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    public AppSettings Current { get; private set; }

    public bool IsFirstRun => Current.IsFirstRun || string.IsNullOrEmpty(Current.SnippetsDirectory);

    public SettingsService(string appDataDir)
    {
        _path = Path.Combine(appDataDir, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, s_json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, s_json);
        File.WriteAllText(_path, json);
    }
}
