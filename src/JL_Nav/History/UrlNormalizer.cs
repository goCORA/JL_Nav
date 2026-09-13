namespace JL_Nav.History;

/// <summary>
/// Explorer's LocationURL returns different string forms for the same folder
/// depending on when it's queried — a plain path ("C:\Users\x") right after a
/// real navigation, a "file:///C:/Users/x" URI (percent-encoded) at other times.
/// Everywhere we compare two URLs for "is this the same place", we need to
/// normalize first or these look like different locations.
/// </summary>
internal static class UrlNormalizer
{
    public static string Normalize(string url)
    {
        if (url.Length == 0)
            return url;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath.TrimEnd('\\').ToLowerInvariant();

        return url.TrimEnd('\\').ToLowerInvariant();
    }
}
