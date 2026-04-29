using SnippetLauncher.App.Views;

namespace SnippetLauncher.App.Services;

/// <summary>
/// Tracks the currently open <see cref="PlaceholderFillDialog"/>(s) so that a nested
/// snippet pick can be inserted directly into the focused placeholder field instead of
/// going via the clipboard. Stack-based to support nested placeholder dialogs.
/// </summary>
public sealed class PlaceholderFillContext
{
    private readonly Stack<PlaceholderFillDialog> _stack = new();

    public PlaceholderFillDialog? Active => _stack.Count > 0 ? _stack.Peek() : null;

    public void Push(PlaceholderFillDialog dlg) => _stack.Push(dlg);

    public void Remove(PlaceholderFillDialog dlg)
    {
        if (_stack.Count == 0) return;
        if (_stack.Peek() == dlg) { _stack.Pop(); return; }
        // Fallback: rebuild without the closed dialog (rare)
        var remaining = _stack.Where(d => d != dlg).Reverse().ToArray();
        _stack.Clear();
        foreach (var d in remaining) _stack.Push(d);
    }
}
