namespace SnippetLauncher.Core.Sync;

public static class RemoteUrlValidator
{
    public static string Validate(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return "";
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("Ongeldige repository-URL.");
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "ssh" or "file"))
            throw new ArgumentException("Gebruik een geldige HTTPS-, SSH- of lokale repository-URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("De repository-URL mag geen accountgegevens, query of fragment bevatten.");
        return uri.AbsoluteUri;
    }
}
