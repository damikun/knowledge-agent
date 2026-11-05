using AngleSharp.Html.Parser;

namespace KnowledgeAgent.Features.Sources.Crawling;

internal sealed record HtmlLinks(Uri? Canonical, IReadOnlyList<Uri> Links, bool NoIndex, bool NoFollow, bool HasScripts)
{
    public static HtmlLinks None { get; } = new(null, [], false, false, false);

    public static HtmlLinks Read(string html, Uri pageUri, string userAgentToken)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        var baseUri = pageUri;
        if (document.QuerySelector("base[href]")?.GetAttribute("href") is { } baseHref
            && Uri.TryCreate(pageUri, baseHref.Trim(), out var resolvedBase)
            && (resolvedBase.Scheme == Uri.UriSchemeHttp || resolvedBase.Scheme == Uri.UriSchemeHttps))
        {
            baseUri = resolvedBase;
        }

        var noIndex = false;
        var noFollow = false;
        foreach (var meta in document.QuerySelectorAll("meta[name][content]"))
        {
            var name = meta.GetAttribute("name")!.Trim();
            if (!name.Equals("robots", StringComparison.OrdinalIgnoreCase) && !name.Equals(userAgentToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directives = meta.GetAttribute("content")!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            noIndex |= directives.Any(d => d.Equals("noindex", StringComparison.OrdinalIgnoreCase) || d.Equals("none", StringComparison.OrdinalIgnoreCase));
            noFollow |= directives.Any(d => d.Equals("nofollow", StringComparison.OrdinalIgnoreCase) || d.Equals("none", StringComparison.OrdinalIgnoreCase));
        }

        Uri? canonical = null;
        foreach (var link in document.QuerySelectorAll("link[rel][href]"))
        {
            if (link.GetAttribute("rel")!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical", StringComparer.OrdinalIgnoreCase))
            {
                canonical = CrawlUrl.Canonicalize(link.GetAttribute("href"), baseUri);
                break;
            }
        }

        var links = new List<Uri>();
        if (!noFollow)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in document.QuerySelectorAll("a[href], area[href]"))
            {
                var rel = anchor.GetAttribute("rel") ?? "";
                if (rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("nofollow", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (CrawlUrl.Canonicalize(anchor.GetAttribute("href"), baseUri) is { } target && seen.Add(target.AbsoluteUri))
                {
                    links.Add(target);
                }
            }
        }

        return new HtmlLinks(canonical, links, noIndex, noFollow, document.Scripts.Length > 0);
    }
}
