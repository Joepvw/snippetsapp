using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.Core.Commands;

/// <summary>Published by GlobalHotkeyService when Ctrl+Shift+N is pressed.</summary>
public sealed record QuickAddCommand : ICommand;
