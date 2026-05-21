using System.Net;
using System.Net.Http;

namespace SnippetLauncher.Core.Tests;

internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    public static FakeHttpMessageHandler Returning(HttpResponseMessage response) =>
        new((_, _) => response);

    public static FakeHttpMessageHandler Throwing(Exception ex) =>
        new((_, _) => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request, cancellationToken));
    }
}
