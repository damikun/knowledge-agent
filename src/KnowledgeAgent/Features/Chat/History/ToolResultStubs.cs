using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Connections.Mcp;

namespace KnowledgeAgent.Features.Chat.History;

// The stub text is part of the cached prompt prefix: any change to it re-sends every open conversation once.
internal sealed partial class ToolResultStubs(ConversationTurn turn, HistoryOptions options)
{
    // Short results, errors and confirmation requests included, are cheaper kept than described.
    public const int KeepWholeChars = 500;

    private const int LineChars = 300;

    private Dictionary<string, CitationDto>? _citations;

    public string For(string? toolName, string text)
    {
        if (text.Length <= KeepWholeChars || toolName is ToolNames.TransferToAgent or ToolNames.TransferBack
            or ToolNames.RequestHumanHandover or ToolNames.RecordFeedback)
        {
            return text;
        }

        var wrapped = TryUnwrap(text, out var inner, out var key);
        var (header, body) = inner.StartsWith("Error: ", StringComparison.Ordinal) ? Excerpt(inner, options.OtherExcerptChars)
            : toolName is ToolNames.SearchKnowledgeBase or ToolNames.GrepSources && PassageReferences(inner) is { Count: > 0 } references
                ? Passages(inner, references)
            : toolName == ToolNames.ReadSource ? SourceRead(inner)
            : toolName == ToolNames.DelegateResearch ? Report(inner)
            : toolName is not null && McpToolNames.IsMcp(toolName) ? Mcp(toolName, inner)
            : Excerpt(inner, options.OtherExcerptChars);

        // Content from sources or other systems stays inside the block it came in.
        var stub = header + "\n" + (wrapped ? UntrustedText.Wrap(body, key) : body);
        return stub.Length < text.Length ? stub : text;
    }

    public static string TextOf(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => result.ToString() ?? string.Empty,
    };

    private (string Header, string Body) Passages(string text, IReadOnlyList<string> references)
    {
        var body = new StringBuilder(TextUtil.Clip(FirstLine(text), LineChars));
        foreach (var reference in references)
        {
            body.Append('\n').Append(CitationLine(reference) ?? OriginalLine(text, reference));
        }

        return (Shortened(text.Length) + " Passages are not repeated; use read_source with a source_id, or search again with source_ids, if you need the text.)", body.ToString());
    }

    private static (string Header, string Body) SourceRead(string text)
    {
        var lines = text.Split('\n', 3).Take(2).Select(line => TextUtil.Clip(line.TrimEnd(), LineChars));
        return (Shortened(text.Length) + " The text is not repeated; call read_source again with this source_id and offset if you need it.)", string.Join('\n', lines));
    }

    private (string Header, string Body) Report(string text)
    {
        var body = new StringBuilder(TextUtil.Clip(text, Math.Max(0, options.ReportExcerptChars)));
        var lines = InlineReferences(text).Select(CitationLine).OfType<string>().ToList();
        if (lines.Count > 0)
        {
            body.Append("\nReferences:");
            foreach (var line in lines)
            {
                body.Append('\n').Append(line);
            }
        }

        return (Shortened(text.Length) + ")", body.ToString());
    }

    private (string Header, string Body) Mcp(string toolName, string text) =>
        (string.Create(
            CultureInfo.InvariantCulture,
            $"(Earlier result of {toolName} from turn {turn.Sequence} at {turn.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC, shortened from {text.Length:N0} characters. This was the state at that time; call the tool again for the current state.)"),
        TextUtil.Clip(text, Math.Max(0, options.McpExcerptChars)));

    private static (string Header, string Body) Excerpt(string text, int chars) =>
        (Shortened(text.Length) + ")", TextUtil.Clip(text, Math.Max(0, chars)));

    private static string Shortened(int length) =>
        string.Create(CultureInfo.InvariantCulture, $"(Earlier result, shortened from {length:N0} characters.");

    // The runner wraps untrusted content in a tag keyed per turn; the key is reused so the stub stays a pure function of the stored text.
    internal static bool TryUnwrap(string text, out string inner, out string key)
    {
        inner = text;
        key = string.Empty;
        if (!UntrustedText.IsWrapped(text))
        {
            return false;
        }

        var open = text.IndexOf("<" + UntrustedText.TagPrefix, UntrustedText.Notice.Length, StringComparison.Ordinal);
        var close = open < 0 ? -1 : text.IndexOf('>', open);
        if (close < 0)
        {
            return false;
        }

        var candidate = text[(open + 1 + UntrustedText.TagPrefix.Length)..close];
        var start = Math.Min(text.Length, close + 2);
        var end = text.LastIndexOf("\n</" + UntrustedText.TagPrefix + candidate + ">", StringComparison.Ordinal);
        if (candidate.Length == 0 || end < start)
        {
            return false;
        }

        inner = text[start..end];
        key = candidate;
        return true;
    }

    private string? CitationLine(string reference)
    {
        _citations ??= ChatJson.DeserializeDtos<CitationDto>(turn.CitationsJson)
            .GroupBy(citation => citation.Ref, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (!_citations.TryGetValue(reference, out var citation))
        {
            return null;
        }

        var builder = new StringBuilder("[").Append(reference).Append("] ").Append(TextUtil.Clip(TextUtil.CollapseWhitespace(citation.SourceTitle), LineChars));
        if (citation.Page is int page)
        {
            builder.Append(" (page ").Append(page.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        return builder.Append(" | source_id=").Append(citation.SourceId.ToString("D")).ToString();
    }

    private static string OriginalLine(string text, string reference)
    {
        var start = text.IndexOf("\n[" + reference + "] ", StringComparison.Ordinal);
        var line = start < 0 ? "[" + reference + "]" : FirstLine(text[(start + 1)..]);
        return TextUtil.Clip(ScoreColumn().Replace(line, string.Empty), LineChars);
    }

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? text : text[..end]).TrimEnd();
    }

    private static List<string> PassageReferences(string text) =>
        PassageHeader().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    private static List<string> InlineReferences(string text) =>
        InlineReference().Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"^\[(S\d+)\] ", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PassageHeader();

    [GeneratedRegex(@"\[(S\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex InlineReference();

    [GeneratedRegex(@" \| score [0-9.]+", RegexOptions.CultureInvariant)]
    private static partial Regex ScoreColumn();
}
