using System.Text;
using System.Text.RegularExpressions;

namespace SnippetLauncher.Core.Storage;

public static class SlugHelper
{
    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Converts a title to a slug: lowercase, hyphens instead of non-alphanumeric, max 60 chars.
    /// </summary>
    public static string Slugify(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "snippet";

        var normalized = title.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        // Strip combining characters (diacritics)
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var slug = NonAlphanumeric.Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug.Length == 0 ? "snippet" : slug;
    }

    /// <summary>
    /// Returns a slug that does not already exist in <paramref name="existingIds"/>.
    /// Appends -2, -3, … until a free slot is found.
    /// </summary>
    public static string UniqueSlug(string title, IEnumerable<string> existingIds)
    {
        var existing = new HashSet<string>(existingIds, StringComparer.Ordinal);
        var base_ = Slugify(title);
        if (!existing.Contains(base_)) return base_;

        for (var n = 2; ; n++)
        {
            var candidate = $"{base_}-{n}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }
}
