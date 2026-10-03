using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly SnippetRepository _repository;
    private readonly IClock _clock;

    // ── Snippet list ─────────────────────────────────────────────────────────
    [ObservableProperty] private IReadOnlyList<Snippet> _snippets = [];
    private bool _loadingForm;
    private bool _refreshing;
    private bool _leaving;
    private bool _refreshQueued;
    private bool _disposed;
    private bool _saving;
    private long _draftVersion;
    private long _draftIdentity;

    private Snippet? _selectedSnippet;
    public Snippet? SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            if (_refreshing || Equals(value, _selectedSnippet)) return;
            if (IsDirty) { _ = SelectAsync(value); return; }
            SetSelection(value);
        }
    }

    private void SetSelection(Snippet? value)
    {
        _draftIdentity++;
        SetProperty(ref _selectedSnippet, value, nameof(SelectedSnippet));
        LoadForm(value);
    }

    private async Task SelectAsync(Snippet? value)
    {
        if (_leaving) return;
        if (await EnsureCanLeaveAsync()) SetSelection(value);
        else OnPropertyChanged(nameof(SelectedSnippet));
    }

    public Task<bool> PrepareForExitAsync() => EnsureCanLeaveAsync();

    public async Task<bool> EnsureCanLeaveAsync()
    {
        if (_leaving) return false;
        if (!IsDirty) return true;
        _leaving = true;
        try
        {
            var choice = MessageBox.Show("Er zijn onopgeslagen wijzigingen. Wil je deze bewaren?\nJa = opslaan, Nee = verwerpen, Annuleren = blijven.",
                "Onopgeslagen wijzigingen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return false;
            if (choice == MessageBoxResult.Yes) { await SaveAsync(); return !IsDirty; }
            IsDirty = false;
            return true;
        }
        finally { _leaving = false; }
    }
    [ObservableProperty] private bool _hasMalformed;
    public IReadOnlyList<string> MalformedPaths => _repository.MalformedSnippetPaths;

    // ── Form fields ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editTags = string.Empty;
    [ObservableProperty] private string _editBody = string.Empty;
    [ObservableProperty] private bool _isNewSnippet;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>True when unsaved changes exist. GitService uses this to pause auto-pull.</summary>
    [ObservableProperty] private bool _isDirty;

    public ObservableCollection<PlaceholderRowViewModel> EditPlaceholders { get; } = [];

    public EditorViewModel(SnippetRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;

        _repository.LibraryChanged += LibraryChanged;
        EditPlaceholders.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (PlaceholderRowViewModel row in e.OldItems) row.PropertyChanged -= PlaceholderChanged;
            if (e.NewItems is not null)
                foreach (PlaceholderRowViewModel row in e.NewItems) row.PropertyChanged += PlaceholderChanged;
            MarkDirty();
        };
        RefreshList();
    }

    private void PlaceholderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => MarkDirty();
    private void LibraryChanged(object? sender, EventArgs e) => QueueRefresh();

    public void Dispose()
    {
        _disposed = true;
        _repository.LibraryChanged -= LibraryChanged;
    }

    private void QueueRefresh()
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            if (_refreshQueued) return;
            _refreshQueued = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _refreshQueued = false;
                if (_disposed) return;
                RefreshList();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }));
    }

    private void RefreshList()
    {
        var wasSelected = SelectedSnippet?.Id;
        var snapshot = _repository.GetAll().OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (IsDirty && SelectedSnippet is not null)
        {
            var index = snapshot.FindIndex(s => s.Id == SelectedSnippet.Id);
            if (index < 0) snapshot.Add(SelectedSnippet);
            else snapshot[index] = SelectedSnippet;
        }
        _refreshing = true;
        Snippets = snapshot;

        HasMalformed = _repository.MalformedSnippetPaths.Count > 0;

        // Restore selection if still present
        if (!IsDirty && !IsNewSnippet) SetSelection(wasSelected is not null
            ? Snippets.FirstOrDefault(s => s.Id == wasSelected)
            : null);
        else OnPropertyChanged(nameof(SelectedSnippet));
        _refreshing = false;
    }

    private void LoadForm(Snippet? value)
    {
        if (value is null) { ClearForm(); return; }
        _loadingForm = true;

        IsNewSnippet = false;
        EditTitle = value.Title;
        EditTags = string.Join(", ", value.Tags);
        EditBody = value.Body;
        IsDirty = false;
        StatusMessage = string.Empty;

        EditPlaceholders.Clear();
        foreach (var p in value.Placeholders)
            EditPlaceholders.Add(new PlaceholderRowViewModel(p));
        _loadingForm = false;
    }

    partial void OnEditTitleChanged(string value) => MarkDirty();
    partial void OnEditTagsChanged(string value) => MarkDirty();
    partial void OnEditBodyChanged(string value) => MarkDirty();
    private void MarkDirty() { if (!_loadingForm) { IsDirty = true; _draftVersion++; } }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task NewSnippetAsync(string? prefillBody = null)
    {
        if (!await EnsureCanLeaveAsync()) return;
        SetSelection(null);
        ClearForm();
        IsNewSnippet = true;
        EditBody = prefillBody ?? string.Empty;
        StatusMessage = string.IsNullOrEmpty(prefillBody)
            ? string.Empty
            : "Klembordinhoud voorgevuld als body.";
    }

    [RelayCommand]
    public async Task NewSnippetEmptyClipboardAsync()
    {
        if (!await EnsureCanLeaveAsync()) return;
        SetSelection(null);
        ClearForm();
        IsNewSnippet = true;
        StatusMessage = "Klembord bevat geen tekst — voer body handmatig in.";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (_saving) return;
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            StatusMessage = "Titel is verplicht.";
            return;
        }

        if (!IsNewSnippet && SelectedSnippet is not null)
        {
            var current = _repository.Get(SelectedSnippet.Id);
            if (!SameContent(current, SelectedSnippet) && MessageBox.Show(
                "Deze snippet is buiten de editor gewijzigd of verwijderd. Jouw concept toch opslaan?",
                "Bron gewijzigd", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }

        var placeholders = EditPlaceholders
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.ToPlaceholder())
            .ToList();

        Snippet snippet;
        if (IsNewSnippet || SelectedSnippet is null)
        {
            var id = SlugHelper.UniqueSlug(EditTitle, _repository.GetAll().Select(s => s.Id));
            snippet = new Snippet(
                id, EditTitle.Trim(),
                ParseTags(EditTags),
                EditBody,
                placeholders,
                _clock.UtcNow,
                _clock.UtcNow);
        }
        else
        {
            snippet = SelectedSnippet with
            {
                Title = EditTitle.Trim(),
                Tags = ParseTags(EditTags),
                Body = EditBody,
                Placeholders = placeholders,
            };
        }

        Snippet saved;
        var version = _draftVersion;
        var identity = _draftIdentity;
        _saving = true;
        try { saved = await _repository.SaveAsync(snippet); }
        catch (Exception)
        {
            StatusMessage = "Opslaan is niet gelukt. Je concept blijft bewaard; probeer opnieuw.";
            return;
        }
        finally { _saving = false; }
        if (identity != _draftIdentity) return;
        if (version != _draftVersion)
        {
            SetProperty(ref _selectedSnippet, saved, nameof(SelectedSnippet));
            IsNewSnippet = false;
            StatusMessage = "Opgeslagen. Nieuwere wijzigingen staan nog in je concept.";
            return;
        }
        SetSelection(saved);
        IsNewSnippet = false;
        IsDirty = false;
        StatusMessage = "Opgeslagen.";
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (SelectedSnippet is null) return;
        if (!await EnsureCanLeaveAsync()) return;

        var confirm = MessageBox.Show(
            $"Snippet '{SelectedSnippet.Title}' verwijderen?",
            "Bevestigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try { await _repository.DeleteAsync(SelectedSnippet.Id); }
        catch (Exception)
        {
            StatusMessage = "Verwijderen is niet gelukt. Probeer opnieuw.";
            return;
        }
        SelectedSnippet = null;
        ClearForm();
    }

    [RelayCommand]
    public void AddPlaceholderRow() => EditPlaceholders.Add(new PlaceholderRowViewModel());

    [RelayCommand]
    public void RemovePlaceholderRow(PlaceholderRowViewModel row) => EditPlaceholders.Remove(row);

    private void ClearForm()
    {
        _draftIdentity++;
        _loadingForm = true;
        EditTitle = string.Empty;
        EditTags = string.Empty;
        EditBody = string.Empty;
        IsDirty = false;
        StatusMessage = string.Empty;
        EditPlaceholders.Clear();
        IsNewSnippet = false;
        _loadingForm = false;
    }

    private static IReadOnlyList<string> ParseTags(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(t => t.Length > 0)
             .ToList();

    private static bool SameContent(Snippet? first, Snippet second) => first is not null &&
        first.Title == second.Title && first.Body == second.Body &&
        first.Tags.SequenceEqual(second.Tags) && first.Placeholders.SequenceEqual(second.Placeholders);
}
