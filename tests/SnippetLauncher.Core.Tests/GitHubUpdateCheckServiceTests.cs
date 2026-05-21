using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using SnippetLauncher.Core.Updates;

namespace SnippetLauncher.Core.Tests;

public class GitHubUpdateCheckServiceTests
{
    private static readonly Version Current = new(1, 0, 4);

    private static GitHubUpdateCheckService Service(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resp = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        return resp;
    }

    private static string Release(string tag) => $"{{ \"tag_name\": \"{tag}\" }}";

    [Fact]
    public async Task Newer_version_returns_UpdateAvailable_with_safe_url()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.1.0"))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.NewVersion.Should().Be(new Version(1, 1, 0));
        result.ReleaseUrl.Should().Be("https://github.com/Joepvw/snippetsapp/releases/tag/v1.1.0");
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Same_version_returns_no_update()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.0.4"))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.NewVersion.Should().BeNull();
        result.ReleaseUrl.Should().BeNull();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Older_version_returns_no_update()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.0.0"))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.NewVersion.Should().BeNull();
    }

    [Fact]
    public async Task Tag_without_v_prefix_is_accepted()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("1.2.0"))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.NewVersion.Should().Be(new Version(1, 2, 0));
    }

    [Fact]
    public async Task Patch_versions_compare_numerically_not_lexically()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.0.10"))));

        var result = await svc.CheckAsync(new Version(1, 0, 9), CancellationToken.None);

        result.NewVersion.Should().Be(new Version(1, 0, 10));
    }

    [Fact]
    public async Task Malformed_json_returns_failure()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json("{ not valid json")));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.NewVersion.Should().BeNull();
        result.FailureReason.Should().Be("Malformed JSON");
    }

    [Fact]
    public async Task Missing_tag_name_returns_failure()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json("{}")));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().Be("Missing tag_name");
    }

    [Fact]
    public async Task Unparseable_version_returns_failure()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.1.0-beta1"))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().Be("Unparseable version");
    }

    [Fact]
    public async Task Http_404_returns_no_releases_found()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().Be("No releases found");
    }

    [Fact]
    public async Task Http_403_returns_generic_failure()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(
            new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().Be("HTTP 403");
    }

    [Fact]
    public async Task Oversized_response_via_content_length_is_rejected()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Release("v1.1.0"), Encoding.UTF8, "application/json"),
        };
        resp.Content.Headers.ContentLength = 5_000_000; // 5 MB advertised

        var svc = Service(FakeHttpMessageHandler.Returning(resp));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().Be("Response too large");
    }

    [Fact]
    public async Task Network_exception_returns_failure()
    {
        var svc = Service(FakeHttpMessageHandler.Throwing(
            new HttpRequestException("DNS fail")));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        result.FailureReason.Should().StartWith("Network error");
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release("v1.1.0"))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => svc.CheckAsync(Current, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Required_request_headers_are_sent()
    {
        var handler = FakeHttpMessageHandler.Returning(Json(Release("v1.0.4")));
        var svc = Service(handler);

        await svc.CheckAsync(Current, CancellationToken.None);

        var sent = handler.Requests.Single();
        sent.RequestUri!.ToString().Should().Be(
            "https://api.github.com/repos/Joepvw/snippetsapp/releases/latest");
        sent.Headers.UserAgent.ToString().Should().Contain("SnippetLauncher/");
        sent.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
        sent.Headers.GetValues("X-GitHub-Api-Version").Single().Should().Be("2022-11-28");
    }

    // Security: a malicious or compromised upstream could put anything in html_url.
    // We don't read html_url at all — the URL we hand to Process.Start is built
    // from the parsed Version, so it is always well-formed https://github.com/...
    // These tests exercise scary tag_name values to prove the safe URL is produced
    // (or refused) regardless of what the upstream tries to inject.
    [Theory]
    [InlineData("v1.1.0\";evil")]
    [InlineData("v1.1.0\nfoo")]
    [InlineData("v1.1.0 OR 1=1")]
    public async Task Malicious_tag_names_either_fail_to_parse_or_yield_safe_url(string tag)
    {
        var svc = Service(FakeHttpMessageHandler.Returning(Json(Release(tag))));

        var result = await svc.CheckAsync(Current, CancellationToken.None);

        if (result.NewVersion is not null)
        {
            result.ReleaseUrl.Should().StartWith(
                "https://github.com/Joepvw/snippetsapp/releases/tag/v");
            result.ReleaseUrl.Should().NotContain("\"");
            result.ReleaseUrl.Should().NotContain("\n");
            result.ReleaseUrl.Should().NotContain(" ");
        }
        else
        {
            result.FailureReason.Should().NotBeNull();
        }
    }
}
