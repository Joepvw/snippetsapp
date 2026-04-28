using SnippetLauncher.Core.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SnippetLauncher.Core.Storage;

/// <summary>
/// Parses and serializes snippets from/to Markdown files with YAML frontmatter.
/// Format:
///   ---
///   id: my-snippet
///   title: My Snippet
///   tags: [faq, waba]
///   placeholders:
///     - name: customer_name
///       label: Klantnaam
///       default: ""
///   created: 2026-04-28T10:00:00Z
///   updated: 2026-04-28T10:00:00Z
///   ---
///   Body text here.
/// </summary>
public static class SnippetSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ssK";

    public static Snippet Deserialize(string id, string content)
    {
        var (frontmatter, body) = SplitFrontmatter(content);
        if (string.IsNullOrWhiteSpace(frontmatter))
            return new Snippet(id, id, [], body.Trim(), [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var dto = Deserializer.Deserialize<SnippetDto>(frontmatter);

        return new Snippet(
            Id: id,
            Title: dto.Title ?? id,
            Tags: dto.Tags ?? [],
            Body: body.Trim(),
            Placeholders: dto.Placeholders?.Select(p => new Placeholder(p.Name ?? "", p.Label ?? p.Name ?? "", p.Default ?? "")).ToList() ?? [],
            Created: TryParseDate(dto.Created),
            Updated: TryParseDate(dto.Updated));
    }

    public static string Serialize(Snippet snippet)
    {
        var dto = new SnippetDto
        {
            Id = snippet.Id,
            Title = snippet.Title,
            Tags = snippet.Tags.Count > 0 ? [.. snippet.Tags] : null,
            Placeholders = snippet.Placeholders.Count > 0
                ? snippet.Placeholders.Select(p => new PlaceholderDto { Name = p.Name, Label = p.Label, Default = p.Default }).ToList()
                : null,
            Created = snippet.Created.ToString(DateFormat),
            Updated = snippet.Updated.ToString(DateFormat),
        };

        var yaml = Serializer.Serialize(dto);
        return $"---\n{yaml}---\n{snippet.Body}\n";
    }

    private static DateTimeOffset TryParseDate(string? s) =>
        s is not null && DateTimeOffset.TryParse(s, out var dt) ? dt : DateTimeOffset.UtcNow;

    private static (string frontmatter, string body) SplitFrontmatter(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
            return ("", content);

        var closeIndex = Array.IndexOf(lines, "---", 1);
        if (closeIndex < 0)
            return ("", content);

        var frontmatter = string.Join("\n", lines[1..closeIndex]);
        var body = string.Join("\n", lines[(closeIndex + 1)..]);
        return (frontmatter, body);
    }

#pragma warning disable CS8618
    private sealed class SnippetDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public List<string>? Tags { get; set; }
        public List<PlaceholderDto>? Placeholders { get; set; }
        public string? Created { get; set; }
        public string? Updated { get; set; }
    }

    private sealed class PlaceholderDto
    {
        public string? Name { get; set; }
        public string? Label { get; set; }
        public string? Default { get; set; }
    }
#pragma warning restore CS8618
}
