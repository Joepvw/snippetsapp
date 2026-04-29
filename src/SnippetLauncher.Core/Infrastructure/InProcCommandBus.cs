using System.Collections.Concurrent;
using SnippetLauncher.Core.Abstractions;

namespace SnippetLauncher.Core.Infrastructure;

/// <summary>
/// Simple in-process command bus. Handlers are invoked synchronously on the
/// caller's thread. Subscriptions are keyed by command type.
/// </summary>
public sealed class InProcCommandBus : ICommandBus
{
    private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _handlers = new();

    public void Publish<T>(T command) where T : ICommand
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) return;

        List<Func<object, Task>> snapshot;
        lock (list) { snapshot = [.. list]; }

        foreach (var handler in snapshot)
            handler(command!).GetAwaiter().GetResult();
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : ICommand
    {
        var list = _handlers.GetOrAdd(typeof(T), _ => []);
        Func<object, Task> boxed = o => handler((T)o);

        lock (list) { list.Add(boxed); }

        return new Subscription(() =>
        {
            lock (list) { list.Remove(boxed); }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
