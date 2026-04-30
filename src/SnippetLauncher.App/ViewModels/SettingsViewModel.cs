using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.App.Services;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Settings;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly SnippetRepository _repository;
    private readonly WindowsStartupService _startupService;
    private bool _suppressStartAtLoginHandler;

    [ObservableProperty] private string _repoPath = "";
    [ObservableProperty] private string _remoteUrl = "";
    [ObservableProperty] private string _searchHotkey = "";
    [ObservableProperty] private string _quickAddHotkey = "";
    [ObservableProperty] private int _pullIntervalSeconds = 60;
    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private bool _startAtLoginEnabled;
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
    /// Raised when the user saves repo-path changes — caller must reload the repository.
    /// </summary>
    public event EventHandler<string>? RepoPathChanged;

    /// <summary>
    /// Raised when the user changes the Git remote URL — caller must rebuild the GitService
    /// so the new URL is applied (clone / origin update).
    /// </summary>
    public event EventHandler? RemoteUrlChanged;

    /// <summary>
    /// Raised when the user clicks "Nu synchroniseren" — caller must trigger a pull + push.
    /// </summary>
    public event EventHandler? SyncNowRequested;

    public SettingsViewModel(SettingsService settings, IGlobalHotkeyService hotkey, SnippetRepository repository, WindowsStartupService startupService)
    {
        _settings = settings;
        _hotkey = hotkey;
        _repository = repository;
        _startupService = startupService;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        RepoPath = _settings.Current.SnippetsDirectory;
        RemoteUrl = _settings.Current.RemoteUrl;
        SearchHotkey = _settings.Current.SearchHotkey;
        QuickAddHotkey = _settings.Current.QuickAddHotkey;
        PullIntervalSeconds = _settings.Current.PullIntervalSeconds;
        SelectedTheme = _settings.Current.Theme;

        _suppressStartAtLoginHandler = true;
        StartAtLoginEnabled = _settings.Current.StartAtLoginEnabled;
        _suppressStartAtLoginHandler = false;
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
    private async Task ApplyRepoPathAsync()
    {
        var newPath = RepoPath.Trim();
        if (string.IsNullOrEmpty(newPath))
        {
            ShowError("Pad mag niet leeg zijn.");
            return;
        }

        if (newPath == _settings.Current.SnippetsDirectory)
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

        // Drain and reload
        _repository.Dispose();

        _settings.Current.SnippetsDirectory = newPath;
        _settings.Save();

        RepoPathChanged?.Invoke(this, newPath);
        ShowSuccess("Snippets-map bijgewerkt. De app herlaadt de repository.");
    }

    [RelayCommand]
    private void ApplyRemoteUrl()
    {
        var newUrl = (RemoteUrl ?? "").Trim();
        if (newUrl == _settings.Current.RemoteUrl)
        {
            ShowSuccess("Remote URL ongewijzigd.");
            return;
        }

        _settings.Current.RemoteUrl = newUrl;
        _settings.Save();
        RemoteUrlChanged?.Invoke(this, EventArgs.Empty);
        ShowSuccess(string.IsNullOrEmpty(newUrl)
            ? "Remote URL gewist."
            : "Remote URL bijgewerkt. Klik op 'Nu synchroniseren' om snippets op te halen.");
    }

    [RelayCommand]
    private void SyncNow()
    {
        SyncNowRequested?.Invoke(this, EventArgs.Empty);
        ShowSuccess("Synchronisatie gestart…");
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
        _settings.Current.PullIntervalSeconds = PullIntervalSeconds;
        _settings.Save();
        ShowSuccess("Sync-interval opgeslagen.");
    }

    private void ShowError(string msg) { StatusMessage = msg; HasError = true; }
    private void ShowSuccess(string msg) { StatusMessage = msg; HasError = false; }
}
