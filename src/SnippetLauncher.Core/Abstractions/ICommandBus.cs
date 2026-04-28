namespace SnippetLauncher.Core.Abstractions;

public interface ICommand { }

/// <summary>
/// In-proc command bus. Decouples hotkey service from ViewModels.
/// Future: replace with named-pipe IPC when system-wide triggers run in a separate process.
/// </summary>
public interface ICommandBus
{
    void Publish<T>(T command) where T : ICommand;
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : ICommand;
}
