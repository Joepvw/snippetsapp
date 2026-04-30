using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.App.ViewModels;

public enum WizardStep { Welcome, RepoPath, Remote, Hotkeys, Done }

public sealed partial class FirstRunWizardViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    [ObservableProperty] private WizardStep _currentStep = WizardStep.Welcome;
    [ObservableProperty] private string _repoPath = "";
    [ObservableProperty] private string _remoteUrl = "";
    [ObservableProperty] private string _searchHotkey = "Ctrl+Shift+Space";
    [ObservableProperty] private string _quickAddHotkey = "Ctrl+Shift+N";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _canGoNext = true;

    public bool IsWelcome => CurrentStep == WizardStep.Welcome;
    public bool IsRepoPath => CurrentStep == WizardStep.RepoPath;
    public bool IsRemote => CurrentStep == WizardStep.Remote;
    public bool IsHotkeys => CurrentStep == WizardStep.Hotkeys;
    public bool IsDone => CurrentStep == WizardStep.Done;

    public bool IsNotFirst => CurrentStep != WizardStep.Welcome;
    public bool IsNotLast => CurrentStep != WizardStep.Done;
    public string NextLabel => CurrentStep switch
    {
        WizardStep.Welcome => "Aan de slag →",
        WizardStep.RepoPath => "Volgende →",
        WizardStep.Remote => "Volgende →",
        WizardStep.Hotkeys => "Voltooien",
        _ => "Sluiten",
    };

    public event EventHandler? Completed;

    public FirstRunWizardViewModel(SettingsService settings)
    {
        _settings = settings;
        _searchHotkey = settings.Current.SearchHotkey;
        _quickAddHotkey = settings.Current.QuickAddHotkey;
    }

    partial void OnCurrentStepChanged(WizardStep value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsRepoPath));
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsHotkeys));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsNotFirst));
        OnPropertyChanged(nameof(IsNotLast));
        OnPropertyChanged(nameof(NextLabel));
        StatusMessage = "";
        HasError = false;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Kies de map voor je snippets-repository",
            Multiselect = false,
        };
        if (!string.IsNullOrEmpty(RepoPath))
            dialog.InitialDirectory = RepoPath;

        if (dialog.ShowDialog() == true)
            RepoPath = dialog.FolderName;
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == WizardStep.Done)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!ValidateCurrentStep()) return;

        CurrentStep = CurrentStep switch
        {
            WizardStep.Welcome => WizardStep.RepoPath,
            WizardStep.RepoPath => WizardStep.Remote,
            WizardStep.Remote => WizardStep.Hotkeys,
            WizardStep.Hotkeys => WizardStep.Done,
            _ => CurrentStep,
        };

        if (CurrentStep == WizardStep.Done)
            ApplySettings();
    }

    [RelayCommand]
    private void Back()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.RepoPath => WizardStep.Welcome,
            WizardStep.Remote => WizardStep.RepoPath,
            WizardStep.Hotkeys => WizardStep.Remote,
            WizardStep.Done => WizardStep.Hotkeys,
            _ => CurrentStep,
        };
    }

    private bool ValidateCurrentStep()
    {
        HasError = false;
        StatusMessage = "";

        if (CurrentStep == WizardStep.RepoPath)
        {
            if (string.IsNullOrWhiteSpace(RepoPath))
            {
                StatusMessage = "Kies een map voor de snippets-repository.";
                HasError = true;
                return false;
            }

            try
            {
                Directory.CreateDirectory(RepoPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Kan map niet aanmaken: {ex.Message}";
                HasError = true;
                return false;
            }

            // Block non-empty non-git folders
            if (Directory.Exists(RepoPath))
            {
                var files = Directory.EnumerateFileSystemEntries(RepoPath).ToList();
                var isGit = Directory.Exists(Path.Combine(RepoPath, ".git"));
                if (files.Count > 0 && !isGit)
                {
                    StatusMessage = "De gekozen map is niet leeg en bevat geen Git-repository. " +
                                    "Kies een lege map of een bestaande Git-clone.";
                    HasError = true;
                    return false;
                }
            }
        }

        if (CurrentStep == WizardStep.Hotkeys)
        {
            if (HotkeyBinding.TryParse(SearchHotkey) is null)
            {
                StatusMessage = $"Ongeldige hotkey: '{SearchHotkey}'. Gebruik bijv. Ctrl+Shift+Space.";
                HasError = true;
                return false;
            }
            if (HotkeyBinding.TryParse(QuickAddHotkey) is null)
            {
                StatusMessage = $"Ongeldige hotkey: '{QuickAddHotkey}'. Gebruik bijv. Ctrl+Shift+N.";
                HasError = true;
                return false;
            }
        }

        return true;
    }

    private void ApplySettings()
    {
        _settings.Current.SnippetsDirectory = RepoPath;
        _settings.Current.SearchHotkey = SearchHotkey;
        _settings.Current.QuickAddHotkey = QuickAddHotkey;
        _settings.Current.RemoteUrl = (RemoteUrl ?? "").Trim();
        _settings.Current.IsFirstRun = false;

        _settings.Save();
    }
}
