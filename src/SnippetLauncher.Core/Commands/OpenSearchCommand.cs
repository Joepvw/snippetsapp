using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.Core.Commands;

/// <summary>Published by GlobalHotkeyService when Ctrl+Shift+Space is pressed.</summary>
public sealed record OpenSearchCommand : ICommand;
