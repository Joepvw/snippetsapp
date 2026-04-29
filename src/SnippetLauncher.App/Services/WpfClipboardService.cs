using System.Windows;
using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.App.Services;

/// <summary>
/// Clipboard access via System.Windows.Clipboard (must be called on UI thread).
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public bool HasText => Application.Current.Dispatcher.Invoke(Clipboard.ContainsText);

    public Task<string?> GetTextAsync()
    {
        var text = Application.Current.Dispatcher.Invoke(() =>
            Clipboard.ContainsText() ? Clipboard.GetText() : null);
        return Task.FromResult(text);
    }

    public Task SetTextAsync(string text)
    {
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
        return Task.CompletedTask;
    }
}
