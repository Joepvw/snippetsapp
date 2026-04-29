using CommunityToolkit.Mvvm.ComponentModel;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.App.ViewModels;

/// <summary>Editable row for a single placeholder definition inside the editor.</summary>
public sealed partial class PlaceholderRowViewModel : ObservableObject
{
    [ObservableProperty] private string _name    = string.Empty;
    [ObservableProperty] private string _label   = string.Empty;
    [ObservableProperty] private string _default = string.Empty;

    public PlaceholderRowViewModel() { }

    public PlaceholderRowViewModel(Placeholder p)
    {
        _name    = p.Name;
        _label   = p.Label;
        _default = p.Default;
    }

    public Placeholder ToPlaceholder() => new(Name.Trim(), Label.Trim(), Default);
}
