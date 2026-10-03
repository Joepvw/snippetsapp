using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.App.Services;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Settings;
using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly WindowsStartupService _startupService;
    private bool _suppressStartAtLoginHandler;
    private bool _suppressUpdateCheckHandler;

    [ObservableProperty] private string _repoPath = "";
    [ObservableProperty] private string _remoteUrl = "";
    [ObservableProperty] private string _searchHotkey = "";
    [ObservableProperty] private string _quickAddHotkey = "";
    [ObservableProperty] private int _pullIntervalSeconds = 60;
    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private bool _startAtLoginEnabled;
    [ObservableProperty] private bool _updateCheckEnabled = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasError;

    public string[] Themes { get; } = ["System", "Light", "Dark"];

    public string AppVersion
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return $"v{info ?? asm.GetName().Version?.ToString(3) ?? "?"}";
        }
    }

    /// <summary>
    /// Raised when the user changes the Git remote URL — caller must rebuild the GitService
    /// so the new URL is applied (clone / origin update).
    /// </summary>
    public Func<Task>? RemoteUrlChangedAction { get; set; }
    public Func<Task>? AuthenticateAction { get; set; }
    public Action<int>? PullIntervalChangedAction { get; set; }

    /// <summary>
    /// Set by the host (App) — invoked when the user clicks "Nu synchroniseren".
    /// Returns a Task that completes when pull + push are done so the UI can show progress.
    /// </summary>
    public Func<Task>? SyncAction { get; set; }

    public SettingsViewModel(SettingsService settings, IGlobalHotkeyService hotkey, WindowsStartupService startupService)
    {
        _settings = settings;
        _hotkey = hotkey;
        _startupService = startupService;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        RepoPath = _settings.Current.PendingSnippetsDirectory ?? _settings.Current.SnippetsDirectory;
        RemoteUrl = _settings.Current.RemoteUrl;
        SearchHotkey = _settings.Current.SearchHotkey;
        QuickAddHotkey = _settings.Current.QuickAddHotkey;
        PullIntervalSeconds = _settings.Current.PullIntervalSeconds;
        SelectedTheme = _settings.Current.Theme;

        _suppressStartAtLoginHandler = true;
        StartAtLoginEnabled = _settings.Current.StartAtLoginEnabled;
        _suppressStartAtLoginHandler = false;

        _suppressUpdateCheckHandler = true;
        UpdateCheckEnabled = _settings.Current.UpdateCheckEnabled;
        _suppressUpdateCheckHandler = false;
    }

    partial void OnUpdateCheckEnabledChanged(bool value)
    {
        if (_suppressUpdateCheckHandler) return;
        _settings.Current.UpdateCheckEnabled = value;
        _settings.Save();
        ShowSuccess(value
            ? "Automatisch controleren op updates ingeschakeld."
            : "Automatisch controleren op updates uitgeschakeld.");
    }

    partial void OnStartAtLoginEnabledChanged(bool value)
    {
        if (_suppressStartAtLoginHandler) return;
        try
        {
            if (value) _startupService.Enable();
            else _startupService.Disable();

            _settings.Current.StartAtLoginEnabled = value;
            _settings.Save();
            ShowSuccess(value
                ? "Snippet Launcher start nu automatisch bij Windows-login."
                : "Automatisch starten uitgeschakeld.");
        }
        catch (Exception ex)
        {
            ShowError($"Kon autostart niet wijzigen: {ex.Message}");
            _suppressStartAtLoginHandler = true;
            StartAtLoginEnabled = !value;
            _suppressStartAtLoginHandler = false;
        }
    }

    [RelayCommand]
    private void BrowseRepo()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kies de snippets-repository map",
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(RepoPath))
            dialog.InitialDirectory = RepoPath;

        if (dialog.ShowDialog() == true)
            RepoPath = dialog.FolderName;
    }

    [RelayCommand]
    private void ApplySearchHotkey()
    {
        var binding = HotkeyBinding.TryParse(SearchHotkey);
        if (binding is null)
        {
            ShowError($"Ongeldige hotkey: '{SearchHotkey}'.");
            return;
        }

        if (!_hotkey.TryRebindSearch(binding.Value))
        {
            ShowError($"Kon hotkey {binding} niet registreren. Al in gebruik door een andere app?");
            // Restore display to current (rolled-back) value
            SearchHotkey = _settings.Current.SearchHotkey;
            return;
        }

        ShowSuccess("Zoek-hotkey bijgewerkt.");
        _settings.Save();
    }

    [RelayCommand]
    private void ApplyQuickAddHotkey()
    {
        var binding = HotkeyBinding.TryParse(QuickAddHotkey);
        if (binding is null)
        {
            ShowError($"Ongeldige hotkey: '{QuickAddHotkey}'.");
            return;
        }

        if (!_hotkey.TryRebindQuickAdd(binding.Value))
        {
            ShowError($"Kon hotkey {binding} niet registreren. Al in gebruik door een andere app?");
            QuickAddHotkey = _settings.Current.QuickAddHotkey;
            return;
        }

        ShowSuccess("Quick-add hotkey bijgewerkt.");
        _settings.Save();
    }

    [RelayCommand]
    private void ApplyRepoPath()
    {
        var newPath = RepoPath.Trim();
        if (string.IsNullOrEmpty(newPath))
        {
            ShowError("Pad mag niet leeg zijn.");
            return;
        }

        if (newPath == _settings.Current.SnippetsDirectory && _settings.Current.PendingSnippetsDirectory is null)
        {
            ShowSuccess("Pad ongewijzigd.");
            return;
        }

        try
        {
            Directory.CreateDirectory(newPath);
        }
        catch (Exception ex)
        {
            ShowError($"Kan map niet aanmaken: {ex.Message}");
            return;
        }

        _settings.Current.PendingSnippetsDirectory = newPath == _settings.Current.SnippetsDirectory ? null : newPath;
        _settings.Save();
        ShowSuccess(_settings.Current.PendingSnippetsDirectory is null
            ? "Mapwijziging geannuleerd. De huidige bibliotheek blijft actief."
            : "Nieuwe snippets-map opgeslagen. Herstart de app om deze te gebruiken; je werkt nu nog in de huidige map.");
    }

    [RelayCommand]
    private async Task ApplyRemoteUrlAsync()
    {
        string newUrl;
        try { newUrl = RemoteUrlValidator.Validate(RemoteUrl ?? ""); }
        catch (ArgumentException)
        {
            ShowError("Gebruik een geldige HTTPS-repository-URL zonder wachtwoord, token of queryparameters.");
            return;
        }
        if (newUrl == _settings.Current.RemoteUrl)
        {
            ShowSuccess("Remote URL ongewijzigd.");
            return;
        }

        _settings.Current.RemoteUrl = newUrl;
        _settings.Save();
        if (RemoteUrlChangedAction is not null)
        {
            try { await RemoteUrlChangedAction(); }
            catch (Exception) { ShowError("De remote kon niet worden gestart. Je snippets blijven lokaal bewaard."); return; }
        }
        ShowSuccess(string.IsNullOrEmpty(newUrl)
            ? "Remote URL gewist."
            : "Remote URL bijgewerkt. Klik op 'Nu synchroniseren' om snippets op te halen.");
    }

    [RelayCommand]
    private void OpenSyncHelp() => Process.Start(new ProcessStartInfo(
        "https://github.com/Joepvw/snippetsapp/blob/master/docs/setup-second-user.md")
    {
        UseShellExecute = true,
    });

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (SyncAction is null)
        {
            ShowError("Sync is niet beschikbaar.");
            return;
        }

        if (_settings.Current.PendingSnippetsDirectory is not null)
            StatusMessage = "Synchroniseren gebruikt nog de huidige map; de nieuwe map wordt actief na herstart.";

        ShowSuccess("Synchronisatie gestart…");
        try
        {
            await SyncAction();
            ShowSuccess("Synchronisatie voltooid.");
        }
        catch (Exception)
        {
            ShowError("Synchronisatie mislukt. Controleer je verbinding, Remote URL en Git-aanmelding. " +
                      "Klik op de hulp bij Synchronisatie.");
        }
    }

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (AuthenticateAction is null) { ShowError("Aanmelden is niet beschikbaar."); return; }
        ShowSuccess("GitHub-aanmelding gestart. Rond de aanmelding af of annuleer het venster.");
        try
        {
            await AuthenticateAction();
            ShowSuccess("Aanmelding en leestoegang gecontroleerd. Synchroniseer om wijzigingen te versturen.");
        }
        catch (Exception)
        {
            ShowError("Aanmelden of repositorytoegang is niet gelukt. Controleer account, URL en rechten. Je kunt lokaal verderwerken.");
        }
    }

    [RelayCommand]
    private void ApplyTheme()
    {
        _settings.Current.Theme = SelectedTheme;
        _settings.Save();
        ShowSuccess($"Thema ingesteld op '{SelectedTheme}'. Herstart de app om het thema toe te passen.");
    }

    [RelayCommand]
    private void ApplyPullInterval()
    {
        try { SettingsService.ValidatePullInterval(PullIntervalSeconds); }
        catch (ArgumentOutOfRangeException) { ShowError("Kies een sync-interval van 1 tot en met 86400 seconden."); return; }
        _settings.Current.PullIntervalSeconds = PullIntervalSeconds;
        _settings.Save();
        PullIntervalChangedAction?.Invoke(PullIntervalSeconds);
        ShowSuccess("Sync-interval opgeslagen.");
    }

    private void ShowError(string msg) { StatusMessage = msg; HasError = true; }
    private void ShowSuccess(string msg) { StatusMessage = msg; HasError = false; }
}
