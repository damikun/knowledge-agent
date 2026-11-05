using System.Text;

namespace KnowledgeAgent.Features.Sources.Crawling;

internal static class CrawlUrl
{
    private static readonly string[] TrackingParameters = ["gclid", "fbclid", "msclkid", "dclid", "gbraid", "wbraid", "mc_cid", "mc_eid"];

    // One spelling per page: http(s) only, no credentials or fragment, no tracking parameters, and no trailing slash
    // except on the root. Returns null for anything that cannot be crawled.
    public static Uri? Canonicalize(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Host = uri.IdnHost.ToLowerInvariant().TrimEnd('.'),
            Query = CleanQuery(uri.Query),
        };

        var path = builder.Path;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            builder.Path = path.TrimEnd('/');
        }

        if (builder.Path.Length == 0)
        {
            builder.Path = "/";
        }

        var result = builder.Uri;
        return result.AbsoluteUri.Length <= SourceDefaults.MaxUrlLength ? result : null;
    }

    public static Uri? Canonicalize(string? url, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (baseUri is not null)
        {
            return Uri.TryCreate(baseUri, trimmed, out var resolved) ? Canonicalize(resolved) : null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) ? Canonicalize(absolute) : null;
    }

    public static string Key(Uri uri) => uri.AbsoluteUri;

    private static string CleanQuery(string query)
    {
        if (query.Length <= 1)
        {
            return string.Empty;
        }

        var kept = new StringBuilder();
        foreach (var pair in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = pair.Split('=', 2)[0];
            if (name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kept.Length > 0)
            {
                kept.Append('&');
            }

            kept.Append(pair);
        }

        return kept.ToString();
    }
}
