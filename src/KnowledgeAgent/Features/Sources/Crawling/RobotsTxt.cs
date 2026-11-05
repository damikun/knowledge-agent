using System.Globalization;

namespace KnowledgeAgent.Features.Sources.Crawling;

internal sealed record RobotsRule(bool Allow, string Pattern);

// The rules of robots.txt that apply to one user agent (RFC 9309), as plain data so a crawl plan can carry them.
internal sealed record RobotsRules(IReadOnlyList<RobotsRule> Rules, double? CrawlDelaySeconds)
{
    public static RobotsRules AllowAll { get; } = new([], null);

    public static RobotsRules DisallowAll { get; } = new([new RobotsRule(false, "/")], null);

    public bool IsAllowed(Uri uri)
    {
        var path = uri.PathAndQuery;
        if (path.Equals("/robots.txt", StringComparison.Ordinal))
        {
            return true;
        }

        // The longest matching pattern wins, and Allow wins a tie.
        RobotsRule? best = null;
        foreach (var rule in Rules)
        {
            if (!Matches(rule.Pattern, path))
            {
                continue;
            }

            if (best is null || rule.Pattern.Length > best.Pattern.Length || (rule.Pattern.Length == best.Pattern.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    // '*' matches any run of characters and a trailing '$' anchors the end; otherwise a pattern matches a prefix.
    internal static bool Matches(string pattern, string path)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        int p = 0, s = 0, star = -1, resume = 0;
        while (s < path.Length)
        {
            if (p == body.Length && !anchored)
            {
                return true;
            }

            if (p < body.Length && body[p] == '*')
            {
                star = p++;
                resume = s;
            }
            else if (p < body.Length && body[p] == path[s])
            {
                p++;
                s++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                s = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < body.Length && body[p] == '*')
        {
            p++;
        }

        return p == body.Length;
    }
}

internal sealed record RobotsTxt(RobotsRules Rules, IReadOnlyList<string> Sitemaps)
{
    public static RobotsTxt Empty { get; } = new(RobotsRules.AllowAll, []);

    // Groups naming the agent's product token apply; without one, the '*' groups do.
    public static RobotsTxt Parse(string? text, string userAgentToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var sitemaps = new List<string>();
        var groups = new List<(List<string> Agents, List<RobotsRule> Rules, List<double> Delays)>();
        (List<string> Agents, List<RobotsRule> Rules, List<double> Delays)? current = null;
        var lastWasAgent = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOf('#');
            if (comment >= 0)
            {
                line = line[..comment];
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "user-agent":
                    if (current is null || !lastWasAgent)
                    {
                        current = ([], [], []);
                        groups.Add(current.Value);
                    }

                    current.Value.Agents.Add(value);
                    lastWasAgent = true;
                    continue;
                case "allow" or "disallow" when current is not null && value.Length > 0:
                    current.Value.Rules.Add(new RobotsRule(key == "allow", value));
                    break;
                case "crawl-delay" when current is not null:
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay) && delay >= 0)
                    {
                        current.Value.Delays.Add(delay);
                    }

                    break;
                case "sitemap" when value.Length > 0:
                    sitemaps.Add(value);
                    break;
            }

            lastWasAgent = false;
        }

        var token = userAgentToken.Trim();
        var matching = groups.Where(group => group.Agents.Any(agent => ProductToken(agent).Equals(token, StringComparison.OrdinalIgnoreCase))).ToList();
        if (matching.Count == 0)
        {
            matching = [.. groups.Where(group => group.Agents.Any(agent => agent == "*"))];
        }

        var rules = matching.SelectMany(group => group.Rules).ToList();
        var delays = matching.SelectMany(group => group.Delays).ToList();
        return new RobotsTxt(new RobotsRules(rules, delays.Count > 0 ? delays.Max() : null), sitemaps);
    }

    // "KnowledgeAgent/1.0 (+https://…)" is matched by "KnowledgeAgent".
    public static string ProductToken(string userAgent)
    {
        var trimmed = userAgent.Trim();
        var end = trimmed.IndexOfAny(['/', ' ']);
        return end > 0 ? trimmed[..end] : trimmed;
    }
}
