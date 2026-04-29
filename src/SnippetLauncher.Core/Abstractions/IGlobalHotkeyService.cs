using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.Core.Abstractions;

public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>
    /// Registers hotkeys from the provided settings on the UI thread.
    /// </summary>
    void Register();

    /// <summary>
    /// Unregisters all hotkeys. Safe to call multiple times.
    /// </summary>
    void Unregister();

    /// <summary>
    /// Attempts to re-bind the search hotkey at runtime.
    /// Rolls back to the previous binding if registration fails.
    /// Returns true on success.
    /// </summary>
    bool TryRebindSearch(HotkeyBinding binding);

    /// <summary>
    /// Attempts to re-bind the quick-add hotkey at runtime.
    /// Rolls back to the previous binding if registration fails.
    /// Returns true on success.
    /// </summary>
    bool TryRebindQuickAdd(HotkeyBinding binding);
}
