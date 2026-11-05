using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Search;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Agents.Access;

public sealed record AgentAskRequest(string Question, Guid? ConversationId = null);

public sealed record AgentAnswerCitationDto(string Ref, string SourceTitle, string? SourceUrl, int? Page, string Snippet);

public sealed record AgentAnswerDto(Guid ConversationId, string Answer, IReadOnlyList<AgentAnswerCitationDto> Citations);

public sealed record AgentSearchRequest(string Query, int? TopK = null);

public sealed record AgentPassageDto(string SourceTitle, string? SourceUrl, int? Page, string Content, double Score);

public sealed record AgentSearchResultDto(IReadOnlyList<AgentPassageDto> Passages);

internal sealed class AgentAskException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed class AgentAskService(
    IChatService chat,
    IConversationRepository conversations,
    IAgentResolver resolver,
    IKnowledgeSearch search,
    IOptions<AgentOptions> options)
{
    public const int MaxQuestionLength = 8_000;

    public async Task<AgentAnswerDto> AskAsync(AgentConfig agent, AgentApiKey key, ConversationChannel channel, AgentAskRequest request, CancellationToken cancellationToken)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            throw new AgentAskException("invalid_question", "Ask a question.");
        }

        if (question.Length > MaxQuestionLength)
        {
            throw new AgentAskException("invalid_question", $"Questions can be at most {MaxQuestionLength} characters long.");
        }

        var caller = new ChatCaller(channel, key.Name, StaffId: "apikey:" + key.Id.ToString("N"));
        if (request.ConversationId is { } conversationId)
        {
            // A key continues only conversations it started, on the same agent and channel.
            var existing = await conversations.GetAsync(conversationId, includeTurns: false, cancellationToken);
            if (existing is null || existing.AgentId != agent.Id || existing.Channel != channel || existing.CreatedById != caller.StaffId)
            {
                throw new AgentAskException("not_found", "That conversation was not found. Leave out the conversation id to start a new one.");
            }

            if (existing.Status != ConversationStatus.Ai)
            {
                throw new AgentAskException("conversation_closed", "That conversation has ended. Start a new one.");
            }
        }

        DoneEvent? done = null;
        ErrorEvent? error = null;
        try
        {
            await foreach (var streamEvent in chat.StreamAsync(new ChatRequestDto(request.ConversationId, question, Agent: agent.Key), caller, cancellationToken))
            {
                switch (streamEvent)
                {
                    case DoneEvent finished when streamEvent.ParentCallId is null:
                        done = finished;
                        break;
                    case ErrorEvent failed when streamEvent.ParentCallId is null:
                        error ??= failed;
                        break;
                }
            }
        }
        catch (ConversationBusyException)
        {
            throw new AgentAskException(ChatErrorCodes.ConversationBusy, "The previous question of this conversation is still being answered. Wait for it.");
        }

        if (error is not null || done is null)
        {
            throw new AgentAskException(error?.Code ?? ChatErrorCodes.InternalError, VisitorErrorTexts.For(error?.Code));
        }

        var conversation = await conversations.GetAsync(done.ConversationId, includeTurns: true, cancellationToken);
        var turn = conversation?.Turns.FirstOrDefault(item => item.Id == done.TurnId);
        var citations = ChatJson.DeserializeDtos<CitationDto>(turn?.CitationsJson)
            .Select(citation => new AgentAnswerCitationDto(citation.Ref, citation.SourceTitle, WebUrl(citation.SourceUrl), citation.Page, citation.Snippet))
            .ToList();
        return new AgentAnswerDto(done.ConversationId, turn?.AssistantMessage ?? string.Empty, citations);
    }

    public async Task<AgentSearchResultDto> SearchAsync(AgentConfig agent, AgentSearchRequest request, CancellationToken cancellationToken)
    {
        var query = request.Query?.Trim() ?? string.Empty;
        if (query.Length is 0 or > 1_000)
        {
            throw new AgentAskException("invalid_query", "Search for 1 to 1,000 characters.");
        }

        var maxTopK = Math.Max(1, options.Value.MaxTopK);
        var topK = Math.Clamp(request.TopK ?? options.Value.DefaultTopK, 1, maxTopK);
        var scope = await resolver.ResolveScopeAsync(agent, [], cancellationToken);
        if (scope is { Count: 0 })
        {
            return new AgentSearchResultDto([]);
        }

        var hits = await search.SearchAsync(new KnowledgeSearchRequest(query, topK, scope), cancellationToken);
        return new AgentSearchResultDto(hits
            .Select(hit => new AgentPassageDto(hit.SourceTitle, WebUrl(hit.SourceUrl), hit.Page, hit.Content, Math.Round(hit.Score, 4)))
            .ToList());
    }

    private static string? WebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? url : null;
}
