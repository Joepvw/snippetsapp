using System.Text;
using System.Text.RegularExpressions;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Domain;

namespace SnippetLauncher.Core.Placeholders;

/// <summary>
/// Resolves placeholder tokens in snippet bodies.
/// Token syntax: {name}  — custom placeholder
/// Literal brace: {{     — rendered as {
/// Built-ins: {date}, {time}, {clipboard}
/// </summary>
public sealed class PlaceholderEngine
{
    // Matches {identifier} but not {{ or }}
    private static readonly Regex TokenRegex =
        new(@"(?<!\{)\{([a-z_][a-z0-9_]*)\}(?!\})", RegexOptions.Compiled);

    private readonly IClock _clock;

    public PlaceholderEngine(IClock clock) => _clock = clock;

    /// <summary>
    /// Extracts the names of all custom placeholders (non-built-in) in <paramref name="body"/>.
    /// </summary>
    public IReadOnlyList<string> ExtractCustomNames(string body)
    {
        var builtIns = new HashSet<string>(["date", "time", "clipboard"], StringComparer.Ordinal);
        return TokenRegex.Matches(body)
            .Select(m => m.Groups[1].Value)
            .Where(n => !builtIns.Contains(n))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Resolves all tokens in <paramref name="body"/>.
    /// <paramref name="values"/> maps placeholder name → user-supplied value.
    /// <paramref name="clipboardSnapshot"/> is the clipboard text captured before the dialog opened.
    /// </summary>
    public string Resolve(string body, IReadOnlyDictionary<string, string> values, string? clipboardSnapshot)
    {
        var now = _clock.UtcNow.ToLocalTime();
        var sb = new StringBuilder(body.Length);
        var pos = 0;

        // Process {{ escape sequences and {token} in one pass
        var raw = body;
        var i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '{')
            {
                // Escaped literal {{
                if (i + 1 < raw.Length && raw[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }
                // Find closing }
                var close = raw.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(raw[i]); i++; continue; }

                var name = raw[(i + 1)..close];
                var resolved = name switch
                {
                    "date"      => now.ToString("yyyy-MM-dd"),
                    "time"      => now.ToString("HH:mm"),
                    "clipboard" => clipboardSnapshot ?? string.Empty,
                    _           => values.TryGetValue(name, out var v) ? v : $"{{{name}}}",
                };
                sb.Append(resolved);
                i = close + 1;
            }
            else if (raw[i] == '}' && i + 1 < raw.Length && raw[i + 1] == '}')
            {
                // Escaped literal }}  → }
                sb.Append('}');
                i += 2;
            }
            else
            {
                sb.Append(raw[i]);
                i++;
            }
        }

        _ = pos; // suppress unused warning
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the body contains any placeholder tokens.
    /// </summary>
    public bool HasPlaceholders(string body) => TokenRegex.IsMatch(body);

    /// <summary>
    /// Merges declared placeholders with names actually used in the body.
    /// Declared placeholders carry label + default; undeclared ones get generated labels.
    /// </summary>
    public IReadOnlyList<Placeholder> MergeWithDeclared(string body, IReadOnlyList<Placeholder> declared)
    {
        var custom = ExtractCustomNames(body);
        if (custom.Count == 0) return [];

        var index = declared.ToDictionary(p => p.Name, StringComparer.Ordinal);
        return custom
            .Select(n => index.TryGetValue(n, out var p) ? p : new Placeholder(n, n, string.Empty))
            .ToList();
    }
}
