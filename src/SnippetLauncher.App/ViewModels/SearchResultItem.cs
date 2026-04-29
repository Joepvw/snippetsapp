using CommunityToolkit.Mvvm.ComponentModel;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.App.ViewModels;

public sealed partial class SearchResultItem : ObservableObject
{
    public Snippet Snippet { get; }
    public string Title => Snippet.Title;
    public string TagsDisplay => Snippet.Tags.Count > 0 ? string.Join(", ", Snippet.Tags) : string.Empty;
    public string BodyPreview => Snippet.Body.Length > 80 ? Snippet.Body[..80].Replace('\n', ' ') + "…" : Snippet.Body.Replace('\n', ' ');

    [ObservableProperty] private bool _isSelected;

    public SearchResultItem(Snippet snippet) => Snippet = snippet;
}
