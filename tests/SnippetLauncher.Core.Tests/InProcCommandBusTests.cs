using FluentAssertions;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Infrastructure;

namespace SnippetLauncher.Core.Tests;

public sealed class InProcCommandBusTests
{
    private readonly InProcCommandBus _bus = new();

    [Fact]
    public void Publish_WithSubscriber_InvokesHandler()
    {
        var received = new List<TestCommand>();
        _bus.Subscribe<TestCommand>(cmd => { received.Add(cmd); return Task.CompletedTask; });

        _bus.Publish(new TestCommand("hello"));

        received.Should().ContainSingle(c => c.Payload == "hello");
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var act = () => _bus.Publish(new TestCommand("x"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Publish_MultipleSubscribers_AllInvoked()
    {
        var log = new List<string>();
        _bus.Subscribe<TestCommand>(c => { log.Add("A:" + c.Payload); return Task.CompletedTask; });
        _bus.Subscribe<TestCommand>(c => { log.Add("B:" + c.Payload); return Task.CompletedTask; });

        _bus.Publish(new TestCommand("x"));

        log.Should().BeEquivalentTo(["A:x", "B:x"]);
    }

    [Fact]
    public void Subscribe_Dispose_RemovesHandler()
    {
        var count = 0;
        var sub = _bus.Subscribe<TestCommand>(_ => { count++; return Task.CompletedTask; });

        _bus.Publish(new TestCommand("first"));
        sub.Dispose();
        _bus.Publish(new TestCommand("second"));

        count.Should().Be(1);
    }

    [Fact]
    public void Publish_DifferentCommandTypes_OnlyMatchingHandlerInvoked()
    {
        var received = new List<string>();
        _bus.Subscribe<TestCommand>(c => { received.Add("test:" + c.Payload); return Task.CompletedTask; });
        _bus.Subscribe<OtherCommand>(c => { received.Add("other:" + c.Value); return Task.CompletedTask; });

        _bus.Publish(new TestCommand("ping"));

        received.Should().ContainSingle(s => s == "test:ping");
    }

    private sealed record TestCommand(string Payload) : ICommand;
    private sealed record OtherCommand(string Value) : ICommand;
}
