using System.Globalization;
using System.Text;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Sources;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class FindSourcesTool : IAgentTool
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const string AnyStatus = "any";

    public string Name => ToolNames.FindSources;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.FindSources,
        "Looks up sources in the knowledge base by words in their title, URL or file name, page by page. Use it to answer "
        + "questions about what the knowledge base contains, to find the id of a document the user mentions (then pass "
        + "it in source_ids to search_knowledge_base or to read_source), or to check whether a source finished "
        + "processing. Returns ready sources unless you ask for another status.",
        ToolSchemas.FindSources(DefaultLimit, MaxLimit));

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var text = arguments.GetString("query")?.Trim();
        var typeText = arguments.GetString("type");
        var statusText = arguments.GetString("status");
        var limit = arguments.GetInt32("limit") ?? DefaultLimit;
        var offset = arguments.GetInt32("offset") ?? 0;
        arguments.ThrowIfInvalid(Name);

        SourceType? type = null;
        if (typeText is not null)
        {
            if (!ToolSchemas.SourceTypes.Contains(typeText, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse<SourceType>(typeText, ignoreCase: true, out var parsedType))
            {
                throw new ToolArgumentException(
                    $"Invalid arguments for {Name}: 'type' must be one of {string.Join(", ", ToolSchemas.SourceTypes)}.");
            }

            type = parsedType;
        }

        SourceStatus? status = SourceStatus.Ready;
        if (statusText is not null)
        {
            if (string.Equals(statusText, AnyStatus, StringComparison.OrdinalIgnoreCase))
            {
                status = null;
            }
            else if (!ToolSchemas.SourceStatuses.Contains(statusText, StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse<SourceStatus>(statusText, ignoreCase: true, out var parsedStatus))
            {
                throw new ToolArgumentException(
                    $"Invalid arguments for {Name}: 'status' must be one of {string.Join(", ", ToolSchemas.SourceStatuses)} or {AnyStatus}.");
            }
            else
            {
                status = parsedStatus;
            }
        }

        if (offset < 0)
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'offset' must be zero or positive.");
        }

        if (invocation.Turn.HasNoSources)
        {
            return new ToolResult(SearchKnowledgeBaseTool.NoSourcesMessage, SearchKnowledgeBaseTool.NoSourcesMessage);
        }

        var query = new SourceQuery(
            Text: string.IsNullOrEmpty(text) ? null : text,
            Type: type,
            Statuses: status is { } wanted ? [wanted] : null,
            Ids: invocation.Turn.ScopeSourceIds,
            Offset: offset,
            Limit: Math.Clamp(limit, 1, MaxLimit));

        var repository = invocation.Services.GetRequiredService<ISourceRepository>();
        var page = await repository.FindAsync(query, cancellationToken);
        return new ToolResult(Format(page, query), Summarize(page, query));
    }

    public static string Format(SourcePage page, SourceQuery query)
    {
        var filters = Describe(query);
        if (page.Total == 0)
        {
            return query.Text is null
                ? $"No sources{filters}."
                : $"No sources match \"{query.Text}\"{filters}.";
        }

        var builder = new StringBuilder();
        builder.Append(Count(page.Total))
            .Append(query.Text is null ? string.Empty : $" match \"{query.Text}\"")
            .Append(filters);

        if (page.Items.Count == 0)
        {
            return builder.Append($". Offset {page.Offset} is past the last result; use a smaller offset.").ToString();
        }

        builder.Append(string.Create(CultureInfo.InvariantCulture, $"; showing {page.Offset + 1}-{page.Offset + page.Items.Count}:"));
        foreach (var source in page.Items)
        {
            builder.Append("\n- ").Append(TextUtil.CollapseWhitespace(source.Title))
                .Append(" (id: ").Append(source.Id.ToString("D"))
                .Append(", type: ").Append(source.Type.ToString().ToLowerInvariant())
                .Append(", status: ").Append(source.Status.ToString().ToLowerInvariant())
                .Append(", chunks: ").Append(source.ChunkCount.ToString(CultureInfo.InvariantCulture))
                .Append(", added: ").Append(source.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(source.Url))
            {
                builder.Append(", url: ").Append(source.Url);
            }

            builder.Append(')');
        }

        if (page.NextOffset is { } next)
        {
            builder.Append(string.Create(CultureInfo.InvariantCulture, $"\nMore results: call {ToolNames.FindSources} again with offset={next}."));
        }

        return builder.ToString();
    }

    private static string Summarize(SourcePage page, SourceQuery query)
    {
        var scope = query.Text is null ? string.Empty : $" for \"{TextUtil.CollapseWhitespace(query.Text)}\"";
        return page.Items.Count == page.Total
            ? $"{Count(page.Total)}{scope}"
            : string.Create(CultureInfo.InvariantCulture, $"{page.Items.Count} of {Count(page.Total)}{scope}");
    }

    private static string Describe(SourceQuery query)
    {
        var parts = new List<string>();
        if (query.Type is { } type)
        {
            parts.Add($"type {type.ToString().ToLowerInvariant()}");
        }

        parts.Add(query.Statuses is { Count: > 0 } statuses
            ? $"status {string.Join(" or ", statuses.Select(s => s.ToString().ToLowerInvariant()))}"
            : "any status");
        return $" ({string.Join(", ", parts)})";
    }

    private static string Count(int total) =>
        string.Create(CultureInfo.InvariantCulture, $"{total:N0} {(total == 1 ? "source" : "sources")}");
}
