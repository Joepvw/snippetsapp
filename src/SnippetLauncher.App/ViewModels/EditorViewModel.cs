using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly SnippetRepository _repository;
    private readonly IClock _clock;

    // ── Snippet list ─────────────────────────────────────────────────────────
    public ObservableCollection<Snippet> Snippets { get; } = [];

    [ObservableProperty] private Snippet? _selectedSnippet;
    [ObservableProperty] private bool _hasMalformed;
    public IReadOnlyList<string> MalformedPaths => _repository.MalformedSnippetPaths;

    // ── Form fields ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _editTitle    = string.Empty;
    [ObservableProperty] private string _editTags     = string.Empty;
    [ObservableProperty] private string _editBody     = string.Empty;
    [ObservableProperty] private bool   _isNewSnippet;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>True when unsaved changes exist. GitService uses this to pause auto-pull.</summary>
    [ObservableProperty] private bool _isDirty;

    public ObservableCollection<PlaceholderRowViewModel> EditPlaceholders { get; } = [];

    public EditorViewModel(SnippetRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;

        _repository.SnippetChanged  += (_, _) => Application.Current.Dispatcher.Invoke(RefreshList);
        _repository.SnippetRemoved  += (_, _) => Application.Current.Dispatcher.Invoke(RefreshList);
        RefreshList();
    }

    private void RefreshList()
    {
        var wasSelected = SelectedSnippet?.Id;
        Snippets.Clear();
        foreach (var s in _repository.GetAll().OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase))
            Snippets.Add(s);

        HasMalformed = _repository.MalformedSnippetPaths.Count > 0;

        // Restore selection if still present
        SelectedSnippet = wasSelected is not null
            ? Snippets.FirstOrDefault(s => s.Id == wasSelected)
            : null;
    }

    partial void OnSelectedSnippetChanged(Snippet? value)
    {
        if (value is null) { ClearForm(); return; }

        IsNewSnippet       = false;
        EditTitle          = value.Title;
        EditTags           = string.Join(", ", value.Tags);
        EditBody           = value.Body;
        IsDirty            = false;
        StatusMessage      = string.Empty;

        EditPlaceholders.Clear();
        foreach (var p in value.Placeholders)
            EditPlaceholders.Add(new PlaceholderRowViewModel(p));
    }

    partial void OnEditTitleChanged(string value)   => MarkDirty();
    partial void OnEditTagsChanged(string value)    => MarkDirty();
    partial void OnEditBodyChanged(string value)    => MarkDirty();
    private void MarkDirty() { if (!IsNewSnippet || EditTitle.Length > 0) IsDirty = true; }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    public void NewSnippet(string? prefillBody = null)
    {
        SelectedSnippet = null;
        ClearForm();
        IsNewSnippet = true;
        EditBody     = prefillBody ?? string.Empty;
        StatusMessage = string.IsNullOrEmpty(prefillBody)
            ? string.Empty
            : "Klembordinhoud voorgevuld als body.";
    }

    [RelayCommand]
    public void NewSnippetEmptyClipboard()
    {
        SelectedSnippet = null;
        ClearForm();
        IsNewSnippet  = true;
        StatusMessage = "Klembord bevat geen tekst — voer body handmatig in.";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            StatusMessage = "Titel is verplicht.";
            return;
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
                Title        = EditTitle.Trim(),
                Tags         = ParseTags(EditTags),
                Body         = EditBody,
                Placeholders = placeholders,
            };
        }

        var saved = await _repository.SaveAsync(snippet);
        SelectedSnippet = saved;
        IsNewSnippet    = false;
        IsDirty         = false;
        StatusMessage   = "Opgeslagen.";
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (SelectedSnippet is null) return;

        var confirm = MessageBox.Show(
            $"Snippet '{SelectedSnippet.Title}' verwijderen?",
            "Bevestigen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        await _repository.DeleteAsync(SelectedSnippet.Id);
        SelectedSnippet = null;
        ClearForm();
    }

    [RelayCommand]
    public void AddPlaceholderRow() => EditPlaceholders.Add(new PlaceholderRowViewModel());

    [RelayCommand]
    public void RemovePlaceholderRow(PlaceholderRowViewModel row) => EditPlaceholders.Remove(row);

    private void ClearForm()
    {
        EditTitle    = string.Empty;
        EditTags     = string.Empty;
        EditBody     = string.Empty;
        IsDirty      = false;
        StatusMessage = string.Empty;
        EditPlaceholders.Clear();
    }

    private static IReadOnlyList<string> ParseTags(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(t => t.Length > 0)
             .ToList();
}
