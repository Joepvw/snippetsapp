using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using SnippetLauncher.Core.Infrastructure;
using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public AppSettings Current { get; private set; }

    public bool IsFirstRun => Current.IsFirstRun || string.IsNullOrEmpty(Current.SnippetsDirectory);

    public SettingsService(string appDataDir)
    {
        _path = Path.Combine(appDataDir, "settings.json");
        Current = Load();
        if (!string.IsNullOrWhiteSpace(Current.PendingSnippetsDirectory))
        {
            Current.SnippetsDirectory = Current.PendingSnippetsDirectory;
            Current.PendingSnippetsDirectory = null;
            Save();
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path) && !File.Exists(_path + ".bak")) return new AppSettings();
        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, s_json) ?? throw new JsonException();
            var unsafeRemote = ContainsUnsafeRemote(json);
            var normalized = Normalize(loaded);
            if (unsafeRemote)
            {
                AtomicJsonFile.Write(_path, JsonSerializer.Serialize(normalized, s_json), preserveBackup: false);
                if (File.Exists(_path + ".bak"))
                {
                    try
                    {
                        var backup = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path + ".bak"), s_json);
                        if (backup is not null)
                            AtomicJsonFile.Write(_path + ".bak", JsonSerializer.Serialize(Normalize(backup), s_json), preserveBackup: false);
                    }
                    catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
                    {
                        Log.Warning("Settings backup could not be normalized; active settings remain usable");
                    }
                }
            }
            return normalized;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warning("Settings could not be loaded; attempting backup recovery");
            if (File.Exists(_path))
                File.Copy(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            if (!File.Exists(_path + ".bak"))
                throw new InvalidDataException("Instellingen zijn beschadigd. Herstel de bewaarde instellingen voordat je verdergaat.");
            try
            {
                var recovered = Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path + ".bak"), s_json)
                    ?? throw new JsonException());
                AtomicJsonFile.Write(_path, JsonSerializer.Serialize(recovered, s_json), preserveBackup: false);
                return recovered;
            }
            catch (Exception backupError) when (backupError is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidDataException("Instellingen en reservekopie zijn niet leesbaar. De bestanden zijn bewaard voor herstel.");
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            ValidatePullInterval(Current.PullIntervalSeconds);
            Current.RemoteUrl = RemoteUrlValidator.Validate(Current.RemoteUrl);
            AtomicJsonFile.Write(_path, JsonSerializer.Serialize(Current, s_json));
        }
    }

    public static void ValidatePullInterval(int seconds)
    {
        if (seconds is < 1 or > 86400)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Kies een sync-interval van 1 tot en met 86400 seconden.");
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (settings.PullIntervalSeconds is < 1 or > 86400)
            settings.PullIntervalSeconds = 60;
        try { settings.RemoteUrl = RemoteUrlValidator.Validate(settings.RemoteUrl); }
        catch (ArgumentException)
        {
            settings.RemoteUrl = "";
            Log.Warning("Unsafe remote configuration removed; configure a URL without credentials");
        }
        return settings;
    }

    private static bool ContainsUnsafeRemote(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, s_json);
        if (settings is null) return false;
        try { RemoteUrlValidator.Validate(settings.RemoteUrl); return false; }
        catch (ArgumentException) { return true; }
    }
}
