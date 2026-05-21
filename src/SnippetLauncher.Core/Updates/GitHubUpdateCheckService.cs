using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnippetLauncher.Core.Updates;

public sealed class GitHubUpdateCheckService : IUpdateCheckService
{
    // Hard-coded repo: the app is tied to this single upstream by design.
    private const string Owner = "Joepvw";
    private const string Repo = "snippetsapp";

    private const string ApiEndpoint =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    // /releases/latest payload is ~5 KB; cap at 1 MB to bound memory on a hostile response.
    private const long MaxResponseBytes = 1_000_000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public GitHubUpdateCheckService(HttpClient http)
    {
        _http = http;
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiEndpoint);
            req.Headers.UserAgent.ParseAdd(
                $"SnippetLauncher/{currentVersion} (+https://github.com/{Owner}/{Repo})");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var resp = await _http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
                return Failed("No releases found");
            if (!resp.IsSuccessStatusCode)
                return Failed($"HTTP {(int)resp.StatusCode}");

            if (resp.Content.Headers.ContentLength is { } len && len > MaxResponseBytes)
                return Failed("Response too large");

            await using var stream = await resp.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);

            ReleaseDto? dto;
            try
            {
                dto = await JsonSerializer
                    .DeserializeAsync<ReleaseDto>(stream, JsonOpts, ct)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Failed("Malformed JSON");
            }

            if (dto is null || string.IsNullOrWhiteSpace(dto.TagName))
                return Failed("Missing tag_name");

            var rawTag = dto.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(rawTag, out var newVersion))
                return Failed("Unparseable version");

            if (newVersion <= currentVersion)
                return NoUpdate();

            // Build the release URL ourselves from the validated tag. We deliberately
            // do NOT pass through `html_url` from the response: that string ends up
            // in Process.Start(... UseShellExecute = true), which is a known RCE sink
            // on Windows (file://, \\unc, ms-msdt:, etc.). Building the URL from a
            // parsed Version object guarantees a well-formed https://github.com URL.
            var safeUrl =
                $"https://github.com/{Owner}/{Repo}/releases/tag/v{newVersion.ToString(3)}";
            return new UpdateCheckResult(newVersion, safeUrl, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException without our token being cancelled = HttpClient.Timeout.
            return Failed("Request timed out");
        }
        catch (HttpRequestException ex)
        {
            return Failed($"Network error: {ex.Message}");
        }
    }

    private static UpdateCheckResult NoUpdate() => new(null, null, null);
    private static UpdateCheckResult Failed(string reason) => new(null, null, reason);

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
