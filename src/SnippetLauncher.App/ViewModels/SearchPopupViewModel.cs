using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnippetLauncher.App.Services;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Placeholders;
using SnippetLauncher.Core.Search;
using SnippetLauncher.Core.Storage;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class SearchPopupViewModel : ObservableObject
{
    private readonly SearchService _search;
    private readonly SnippetRepository _repository;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly PlaceholderEngine _placeholderEngine;
    private readonly PlaceholderFillContext _placeholderContext;

    private CancellationTokenSource? _debounceCts;

    [ObservableProperty] private string _queryText = string.Empty;
    [ObservableProperty] private bool _isNoMatchVisible;
    [ObservableProperty] private bool _isEmptyLibraryVisible;
    [ObservableProperty] private int _selectedIndex = -1;

    public ObservableCollection<SearchResultItem> Results { get; } = [];

    /// <summary>Raised when the popup should close (after clipboard is set).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user wants to create a new snippet from the search query.</summary>
    public event EventHandler<string>? CreateSnippetRequested;

    public SearchPopupViewModel(
        SearchService search,
        SnippetRepository repository,
        IClipboardService clipboard,
        IDialogService dialogs,
        PlaceholderEngine placeholderEngine,
        PlaceholderFillContext placeholderContext)
    {
        _search = search;
        _repository = repository;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _placeholderEngine = placeholderEngine;
        _placeholderContext = placeholderContext;
    }

    public void OnActivated()
    {
        QueryText = string.Empty;
        RunQuery(string.Empty);
    }

    partial void OnQueryTextChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(150, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            Application.Current.Dispatcher.Invoke(() => RunQuery(value));
        }, token, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void RunQuery(string query)
    {
        var results = _search.Query(query, 8);
        Results.Clear();
        foreach (var r in results)
            Results.Add(new SearchResultItem(r.Snippet));

        IsEmptyLibraryVisible = _repository.GetAll().Count == 0;
        IsNoMatchVisible = !IsEmptyLibraryVisible && Results.Count == 0 && !string.IsNullOrWhiteSpace(query);

        SelectedIndex = Results.Count > 0 ? 0 : -1;
        UpdateSelectionHighlight();
    }

    [RelayCommand]
    public void MoveUp()
    {
        if (Results.Count == 0) return;
        SelectedIndex = SelectedIndex <= 0 ? Results.Count - 1 : SelectedIndex - 1;
        UpdateSelectionHighlight();
    }

    [RelayCommand]
    public void MoveDown()
    {
        if (Results.Count == 0) return;
        SelectedIndex = SelectedIndex >= Results.Count - 1 ? 0 : SelectedIndex + 1;
        UpdateSelectionHighlight();
    }

    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            if (IsNoMatchVisible && !string.IsNullOrWhiteSpace(QueryText))
                CreateSnippetRequested?.Invoke(this, QueryText);
            return;
        }

        var snippet = Results[SelectedIndex].Snippet;

        string resolved;
        if (_placeholderEngine.HasPlaceholders(snippet.Body))
        {
            // Snapshot clipboard before showing dialog (user may change clipboard while dialog is open)
            var clipSnapshot = await _clipboard.GetTextAsync();

            var effectivePlaceholders = _placeholderEngine.MergeWithDeclared(snippet.Body, snippet.Placeholders);
            var values = await _dialogs.ShowPlaceholderFillAsync(snippet.Title, effectivePlaceholders);
            if (values is null) return; // user cancelled — don't close popup

            resolved = _placeholderEngine.Resolve(snippet.Body, values, clipSnapshot);
        }
        else
        {
            resolved = snippet.Body;
        }

        // Nested context: if a PlaceholderFillDialog is open, insert directly into its
        // active field instead of going via the clipboard.
        if (_placeholderContext.Active is { } dlg && dlg.ActiveField is { } tb)
        {
            var caret = tb.CaretIndex;
            var selLen = tb.SelectionLength;
            if (selLen > 0)
            {
                tb.Text = tb.Text.Remove(tb.SelectionStart, selLen).Insert(tb.SelectionStart, resolved);
                caret = tb.SelectionStart;
            }
            else
            {
                tb.Text = tb.Text.Insert(caret, resolved);
            }
            tb.CaretIndex = caret + resolved.Length;
            _repository.RecordUse(snippet.Id);
            CloseRequested?.Invoke(this, EventArgs.Empty);
            dlg.Activate();
            tb.Focus();
            return;
        }

        // Top-level: set clipboard, then raise close
        await _clipboard.SetTextAsync(resolved);
        _repository.RecordUse(snippet.Id);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionHighlight()
    {
        for (var i = 0; i < Results.Count; i++)
            Results[i].IsSelected = i == SelectedIndex;
    }
}
