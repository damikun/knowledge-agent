using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Sources.Crawling;

// Which URLs belong to a website: its scope, its exclude patterns and robots.txt.
internal sealed class CrawlRules
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    private readonly string _site;
    private readonly string _prefix;
    private readonly CrawlScope _scope;
    private readonly Regex[] _excludes;
    private readonly RobotsRules _robots;

    public CrawlRules(Uri start, WebsiteCrawlSettings settings, RobotsRules robots)
    {
        Start = start;
        _site = SourceDefaults.SiteOf(start);
        _scope = settings.Scope;
        _prefix = SectionPrefix(start);
        _excludes = [.. settings.Exclude.Select(ToRegex)];
        _robots = robots;
    }

    public Uri Start { get; }

    public bool Allows(Uri uri) => InScope(uri) && !IsExcluded(uri) && _robots.IsAllowed(uri);

    // http and https, and the host with or without "www.", count as the same site.
    public bool InScope(Uri uri)
    {
        if (!SourceDefaults.SiteOf(uri).Equals(_site, StringComparison.Ordinal))
        {
            return false;
        }

        return _scope switch
        {
            CrawlScope.Site => true,
            CrawlScope.Section => uri.AbsolutePath.Equals(_prefix.TrimEnd('/'), StringComparison.Ordinal)
                || uri.AbsolutePath.StartsWith(_prefix, StringComparison.Ordinal)
                || _prefix == "/",
            _ => CrawlUrl.Key(uri) == CrawlUrl.Key(Start),
        };
    }

    public bool IsExcluded(Uri uri)
    {
        var target = uri.PathAndQuery;
        foreach (var exclude in _excludes)
        {
            try
            {
                if (exclude.IsMatch(target))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    // "/docs/guide" covers "/docs/guide/…"; "/docs/index.html" covers "/docs/…".
    internal static string SectionPrefix(Uri start)
    {
        var path = start.AbsolutePath;
        var lastSegment = path[(path.LastIndexOf('/') + 1)..];
        if (lastSegment.Contains('.', StringComparison.Ordinal))
        {
            return path[..(path.LastIndexOf('/') + 1)];
        }

        return path.EndsWith('/') ? path : path + "/";
    }

    // '*' matches anything. A pattern starting with '/' matches from the start of the path, any other anywhere in it,
    // and both match everything that follows.
    internal static Regex ToRegex(string pattern)
    {
        var trimmed = pattern.Trim();
        var body = Regex.Escape(trimmed).Replace("\\*", ".*", StringComparison.Ordinal);
        var expression = trimmed.StartsWith('/') ? "^" + body : body;
        return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
    }
}
