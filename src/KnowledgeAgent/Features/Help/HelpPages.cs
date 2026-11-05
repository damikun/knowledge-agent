namespace KnowledgeAgent.Features.Help;

internal static class HelpPages
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "Home",
        ["inbox"] = "Inbox",
        ["conversations"] = "Inbox",
        ["contacts"] = "Contacts",
        ["people"] = "Contacts",
        ["feedback"] = "Feedback",
        ["analytics"] = "Analytics",
        ["playground"] = "Playground",
        ["chat"] = "Playground",
        ["agents"] = "Agents",
        ["widgets"] = "Widgets",
        ["sources"] = "Sources",
        ["prompts"] = "Prompts",
        ["evals"] = "Evaluations",
        ["tools"] = "Tools",
        ["notifications"] = "Notifications",
        ["connections"] = "Notifications",
        ["integrations"] = "Notifications",
        ["team"] = "Team",
        ["models"] = "AI models",
        ["system"] = "System",
        ["jobs"] = "System",
    };

    public static string? Segment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
        {
            return null;
        }

        var end = path.IndexOfAny(['/', '?', '#'], 1);
        var segment = (end < 0 ? path[1..] : path[1..end]).ToLowerInvariant();
        return segment.Length == 0 ? null : segment;
    }

    public static string? Name(string? path) => Segment(path) is { } segment && Names.TryGetValue(segment, out var name) ? name : null;

    public static string? Describe(string? path)
    {
        if (Name(path) is not { } name)
        {
            return null;
        }

        var bare = path!.Split('?', '#')[0].TrimEnd('/').ToLowerInvariant();
        return bare switch
        {
            "/agents/new" => "Agents, creating a new agent",
            "/widgets/new" => "Widgets, creating a new widget",
            _ when bare.Count(character => character == '/') > 1 && name is "Agents" or "Widgets" or "Contacts" => $"{name}, one {name[..^1].ToLowerInvariant()}",
            _ => name,
        };
    }
}
