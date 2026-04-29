using System.Windows;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;
using SnippetLauncher.App.Views;

namespace SnippetLauncher.App.Services;

public sealed class WpfDialogService : IDialogService
{
    private readonly PlaceholderFillContext _placeholderContext;

    public WpfDialogService(PlaceholderFillContext placeholderContext)
    {
        _placeholderContext = placeholderContext;
    }

    public Task<Dictionary<string, string>?> ShowPlaceholderFillAsync(IReadOnlyList<Placeholder> placeholders)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PlaceholderFillDialog(string.Empty, placeholders, _placeholderContext)
            {
                Owner = Application.Current.MainWindow,
            };
            var ok = dialog.ShowDialog() == true;
            return Task.FromResult(ok ? dialog.Result : null);
        });
    }

    public Task<Dictionary<string, string>?> ShowPlaceholderFillAsync(string snippetTitle, IReadOnlyList<Placeholder> placeholders)
    {
        var tcs = new TaskCompletionSource<Dictionary<string, string>?>();

        Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new PlaceholderFillDialog(snippetTitle, placeholders, _placeholderContext);
            // Non-modal Show() so the search popup can still receive input on top of this dialog.
            dialog.Closed += (_, _) => tcs.TrySetResult(dialog.Result);
            dialog.Show();
            dialog.Activate();
        });

        return tcs.Task;
    }

    public Task ShowConflictNotificationAsync(IReadOnlyList<string> backedUpFilePaths)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var files = string.Join("\n", backedUpFilePaths.Select(f => $"  • {f}"));
            MessageBox.Show(
                $"Git-conflict opgelost (last-writer-wins).\nBackups:\n{files}",
                "Snippet Launcher — Conflict opgelost",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
        return Task.CompletedTask;
    }
}
