using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat;

public sealed record ReplySuggestionDto(string Text, IReadOnlyList<CitationDto> Citations);

internal static class ReplySuggestionEndpoints
{
    public static RouteHandlerBuilder MapReplySuggestionEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/conversations/{id:guid}/suggest-reply", SuggestAsync)
            .WithTags("Handover")
            .WithName("SuggestReply")
            .WithSummary("Drafts a reply for the staff member from the conversation so far and the knowledge sources, as the conversation's agent; 503 with code model_not_configured without a chat model.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<Results<Ok<ReplySuggestionDto>, ProblemHttpResult>> SuggestAsync(
        Guid id,
        ClaimsPrincipal user,
        ReplySuggester suggester,
        CancellationToken cancellationToken)
    {
        try
        {
            var suggestion = await suggester.SuggestAsync(id, HandoverEndpoints.StaffName(user), cancellationToken);
            return suggestion is null ? ApiResults.NotFound($"Conversation '{id}' was not found.") : TypedResults.Ok(suggestion);
        }
        catch (ModelNotConfiguredException ex)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Model not configured",
                detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = ChatErrorCodes.ModelNotConfigured });
        }
        catch (ReplySuggestionFailedException ex)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "No draft",
                detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = ex.Code });
        }
    }
}

internal sealed class ReplySuggestionFailedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed partial class ReplySuggester(
    IConversationRepository conversations,
    IAgentResolver agents,
    IPromptService prompts,
    IChatClientFactory chatClients,
    AgentRunner runner,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider)
{
    public const string AgentName = "reply-assistant";
    internal const int MaxMessages = 40;
    internal const int MaxMessageLength = 4000;

    public static readonly IReadOnlyList<string> ReadingTools =
        [ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource];

    private readonly AgentOptions _options = options.Value;

    public async Task<ReplySuggestionDto?> SuggestAsync(Guid conversationId, string staffName, CancellationToken cancellationToken)
    {
        if (await conversations.GetAsync(conversationId, includeTurns: true, cancellationToken) is not { } conversation)
        {
            return null;
        }

        var said = conversation.Messages.Where(message => message.Author != MessageAuthor.System && !string.IsNullOrWhiteSpace(message.Text)).ToList();
        if (said.Count == 0)
        {
            throw new ValidationFailedException("conversation", "The conversation has no messages to reply to yet.");
        }

        var agent = await agents.ResolveAsync(conversation.AgentId, fallbackKey: null, cancellationToken);
        var profile = conversation.Profile ?? agent.ProfileName;
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["agentName"] = agent.Name,
            ["appName"] = _options.AppName,
            ["staffName"] = staffName,
            ["today"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["allowGeneralKnowledge"] = agent.AllowGeneralKnowledge,
        };
        if (!string.IsNullOrWhiteSpace(agent.Language))
        {
            variables["language"] = agent.Language;
        }

        if (!string.IsNullOrWhiteSpace(agent.Instructions))
        {
            variables["instructions"] = agent.Instructions;
        }

        var prompt = await prompts.RenderAsync(PromptNames.ReplySuggestion, variables, cancellationToken: cancellationToken);

        // A model config written for another profile must not override the agent's own.
        var patch = prompt.Profile is null || string.Equals(prompt.Profile, profile, StringComparison.OrdinalIgnoreCase) ? prompt.ModelPatch : null;
        var model = await chatClients.CreateAsync(profile, patch, requestPatch: null, cancellationToken);

        var definition = new AgentDefinition(
            AgentName,
            PromptNames.ReplySuggestion,
            profile,
            ReadingTools.Where(agent.Tools.Contains).ToList(),
            Math.Max(1, _options.MaxIterations));
        var scope = await agents.ResolveScopeAsync(agent, conversation.SourceIds, cancellationToken);
        var citations = new CitationRegistry();

        var events = Channel.CreateUnbounded<ChatStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
        var turn = new AgentTurnContext(conversation.Id, Guid.CreateVersion7(), scope, citations, events.Writer);
        var messages = PromptMessages.ForAgent(prompt, Transcript(said, staffName));
        AgentRunResult result;
        try
        {
            result = await runner.RunAsync(new AgentRunRequest(definition, model, messages, new PromptReference(prompt.Name, prompt.Version), turn), cancellationToken);
        }
        finally
        {
            events.Writer.TryComplete();
        }

        if (result.Error is { } error)
        {
            throw new ReplySuggestionFailedException(error.Code, error.Message);
        }

        var text = (string.IsNullOrWhiteSpace(result.FinalText) ? result.FullText : result.FinalText).Trim();
        if (text.Length == 0)
        {
            throw new ReplySuggestionFailedException(ChatErrorCodes.InternalError, "The model wrote no draft. Try again.");
        }

        var cited = ReferenceTag().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return new ReplySuggestionDto(text, citations.TurnCitations.Where(citation => cited.Contains(citation.Ref)).ToList());
    }

    // The conversation as one message, so the model reads it as material to answer rather than as its own turns.
    internal static string Transcript(IReadOnlyList<ConversationMessage> messages, string staffName)
    {
        var text = new StringBuilder("The conversation so far, oldest first:\n\n<conversation>\n");
        foreach (var message in messages.TakeLast(MaxMessages))
        {
            var who = message.Author switch
            {
                MessageAuthor.Visitor or MessageAuthor.User => message.AuthorName is { Length: > 0 } name ? $"Customer ({name})" : "Customer",
                MessageAuthor.Assistant => "Assistant",
                _ => message.AuthorName is { Length: > 0 } staff ? $"Team ({staff})" : "Team",
            };
            var body = message.Text.Trim();
            text.Append('[').Append(who).Append("]\n").Append(body.Length > MaxMessageLength ? body[..MaxMessageLength] + " ..." : body).Append("\n\n");
        }

        return text.Append("</conversation>\n\nWrite the next reply ").Append(staffName).Append(" sends to the customer.").ToString();
    }

    [GeneratedRegex(@"\[(S\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceTag();
}
