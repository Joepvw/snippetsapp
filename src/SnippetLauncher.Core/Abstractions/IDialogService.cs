using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.Core.Abstractions;

public interface IDialogService
{
    /// <summary>
    /// Shows a fill-in dialog for snippet placeholders.
    /// Returns null if the user cancelled.
    /// </summary>
    Task<Dictionary<string, string>?> ShowPlaceholderFillAsync(IReadOnlyList<Placeholder> placeholders);

    /// <summary>
    /// Notifies the user that conflicts were auto-resolved with backups.
    /// </summary>
    Task ShowConflictNotificationAsync(IReadOnlyList<string> backedUpFilePaths);
}
