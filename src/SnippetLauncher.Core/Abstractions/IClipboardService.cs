namespace SnippetLauncher.Core.Abstractions;

public interface IClipboardService
{
    bool HasText { get; }
    Task<string?> GetTextAsync();
    Task SetTextAsync(string text);
}
