using System.Globalization;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Sources;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Tools;

internal sealed class ReadSourceTool(IOptions<AgentOptions> options) : IAgentTool
{
    public const int DefaultMaxChars = 4000;
    public const int MaxCharsCap = 12000;

    private const int HeaderReserve = 400;

    private readonly int _maxChars = MaxChars(options.Value);

    public string Name => ToolNames.ReadSource;

    public AIFunctionDeclaration Declaration { get; } = AIFunctionFactory.CreateDeclaration(
        ToolNames.ReadSource,
        "Reads the extracted text of one ready source sequentially. Use it when search passages are not enough, "
        + "e.g. to read a whole section or a short document. The result header tells you the total length and the "
        + "offset to continue from.",
        ToolSchemas.ReadSource(Math.Min(DefaultMaxChars, MaxChars(options.Value)), MaxChars(options.Value)));

    public async Task<ToolResult> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var arguments = invocation.Arguments;
        var idText = arguments.GetString("source_id", required: true);
        var offset = arguments.GetInt32("offset") ?? 0;
        var maxChars = arguments.GetInt32("max_chars") ?? DefaultMaxChars;
        arguments.ThrowIfInvalid(Name);

        if (!Guid.TryParse(idText, out var sourceId))
        {
            throw new ToolArgumentException(
                $"Invalid arguments for {Name}: 'source_id' must be a source id (GUID) as returned by find_sources or search results.");
        }

        if (offset < 0)
        {
            throw new ToolArgumentException($"Invalid arguments for {Name}: 'offset' must be zero or positive.");
        }

        maxChars = Math.Clamp(maxChars, 1, _maxChars);

        if (!invocation.Turn.IsInScope(sourceId))
        {
            return ToolResult.Failure($"Source {sourceId} is not among the sources selected for this conversation.");
        }

        var repository = invocation.Services.GetRequiredService<ISourceRepository>();
        var source = await repository.GetAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return ToolResult.Failure($"Source {sourceId} was not found.");
        }

        if (source.Status != SourceStatus.Ready)
        {
            var status = source.Status.ToString().ToLowerInvariant();
            var reason = source.Status is SourceStatus.Failed or SourceStatus.Waiting && !string.IsNullOrWhiteSpace(source.Error) ? $": {source.Error}" : ".";
            return ToolResult.Failure($"Source \"{source.Title}\" is not ready (status: {status}){reason}");
        }

        var content = await repository.GetContentAsync(sourceId, cancellationToken);
        var text = content?.ExtractedText;
        if (string.IsNullOrEmpty(text))
        {
            return ToolResult.Failure($"Source \"{source.Title}\" has no extracted text.");
        }

        if (offset >= text.Length)
        {
            return ToolResult.Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"offset {offset} is past the end of \"{source.Title}\" ({text.Length} characters)."));
        }

        var end = Math.Min(text.Length, offset + maxChars);
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        var slice = text[offset..end].Replace('\f', '\n');
        var paging = end < text.Length
            ? string.Create(CultureInfo.InvariantCulture, $"Next offset: {end}.")
            : "End of text.";
        var output = string.Create(
            CultureInfo.InvariantCulture,
            $"Source: {TextUtil.CollapseWhitespace(source.Title)} (id: {source.Id:D}, type: {source.Type.ToString().ToLowerInvariant()})\n"
            + $"Characters {offset}-{end} of {text.Length}. {paging}\n\n{slice}");
        var summary = string.Create(CultureInfo.InvariantCulture, $"Read characters {offset}-{end} of {text.Length} from \"{source.Title}\"");
        return new ToolResult(output, summary);
    }

    private static int MaxChars(AgentOptions options) =>
        Math.Clamp(options.MaxToolResultChars - HeaderReserve, 500, MaxCharsCap);
}
