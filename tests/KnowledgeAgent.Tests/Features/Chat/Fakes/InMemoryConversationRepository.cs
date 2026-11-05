using System.Collections.Concurrent;
using KnowledgeAgent.Features.Chat;

namespace KnowledgeAgent.Tests.Features.Chat.Fakes;

// Returns copies, like a database would, so tests catch code that relies on in-memory mutation.
public sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly ConcurrentDictionary<Guid, Conversation> _conversations = new();
    private readonly Lock _gate = new();

    public Exception? FailAddTurnWith { get; set; }

    public int Count => _conversations.Count;

    public IReadOnlyList<Conversation> All => _conversations.Values.ToList();

    public Task<Conversation> CreateAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _conversations[conversation.Id] = Copy(conversation);
        }

        return Task.FromResult(Copy(conversation));
    }

    public Task<Conversation?> GetAsync(Guid id, bool includeTurns = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_conversations.TryGetValue(id, out var stored))
            {
                return Task.FromResult<Conversation?>(null);
            }

            var copy = Copy(stored);
            if (!includeTurns)
            {
                copy.Turns.Clear();
                copy.Messages.Clear();
            }

            return Task.FromResult<Conversation?>(copy);
        }
    }

    public Conversation Get(Guid id)
    {
        lock (_gate)
        {
            return Copy(_conversations[id]);
        }
    }

    public Task<Guid?> FindConversationOfMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_conversations.Values
                .Where(conversation => conversation.Messages.Any(message => message.Id == messageId))
                .Select(conversation => (Guid?)conversation.Id)
                .FirstOrDefault());
        }
    }

    public Task<Guid?> FindLatestForVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_conversations.Values
                .Where(conversation => conversation.VisitorId == visitorId && conversation.Status != ConversationStatus.Closed)
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .Select(conversation => (Guid?)conversation.Id)
                .FirstOrDefault());
        }
    }

    public Task<Guid?> FindLatestForContactAsync(Guid contactId, Guid widgetId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_conversations.Values
                .Where(conversation => conversation.ContactId == contactId && conversation.WidgetId == widgetId && conversation.Status != ConversationStatus.Closed)
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .Select(conversation => (Guid?)conversation.Id)
                .FirstOrDefault());
        }
    }

    public Task<int> DeleteWidgetConversationsAsync(Guid widgetId, DateTimeOffset updatedBefore, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var expired = _conversations.Values.Where(conversation => conversation.WidgetId == widgetId && conversation.UpdatedAt < updatedBefore).ToList();
            foreach (var conversation in expired)
            {
                _conversations.TryRemove(conversation.Id, out _);
            }

            return Task.FromResult(expired.Count);
        }
    }

    public Task<IReadOnlyList<Guid>> ListIdleAsync(
        Guid widgetId,
        ConversationStatus status,
        DateTimeOffset updatedBefore,
        IReadOnlyCollection<MessageAuthor>? lastAuthors,
        int take,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(_conversations.Values
                .Where(conversation => conversation.WidgetId == widgetId && conversation.Status == status && conversation.UpdatedAt < updatedBefore)
                .Where(conversation => lastAuthors is null
                                       || conversation.Messages.OrderBy(message => message.Sequence).LastOrDefault() is { } last && lastAuthors.Contains(last.Author))
                .OrderBy(conversation => conversation.UpdatedAt)
                .ThenBy(conversation => conversation.Id)
                .Select(conversation => conversation.Id)
                .Take(take)
                .ToList());
        }
    }

    public Task AddTurnAsync(ConversationTurn turn, CancellationToken cancellationToken = default)
    {
        if (FailAddTurnWith is { } failure)
        {
            return Task.FromException(failure);
        }

        lock (_gate)
        {
            var stored = _conversations[turn.ConversationId];
            if (turn.Sequence <= 0)
            {
                turn.Sequence = stored.Turns.Count == 0 ? 1 : stored.Turns.Max(existing => existing.Sequence) + 1;
            }

            stored.Turns.Add(Copy(turn));
            stored.Turns.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            stored.UpdatedAt = turn.CreatedAt;
        }

        return Task.CompletedTask;
    }

    public Task<ConversationMessage> AddMessageAsync(ConversationMessage message, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_conversations.TryGetValue(message.ConversationId, out var stored))
            {
                throw new KeyNotFoundException($"Conversation '{message.ConversationId}' does not exist.");
            }

            message.Sequence = stored.Messages.Count == 0 ? 1 : stored.Messages.Max(existing => existing.Sequence) + 1;
            stored.Messages.Add(Copy(message));
            stored.UpdatedAt = message.CreatedAt;
        }

        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid conversationId, int afterSequence = 0, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ConversationMessage>>(_conversations.TryGetValue(conversationId, out var stored)
                ? stored.Messages.Where(message => message.Sequence > afterSequence).OrderBy(message => message.Sequence).Select(Copy).ToList()
                : []);
        }
    }

    public Task UpdateStatusAsync(Guid id, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _conversations[id].Status = status;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateStatusAsync(
        Guid id,
        ConversationStatus expected,
        ConversationStatus status,
        DateTimeOffset? updatedBefore = null,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_conversations.TryGetValue(id, out var stored) || stored.Status != expected || stored.UpdatedAt >= updatedBefore)
            {
                return Task.FromResult(false);
            }

            stored.Status = status;
            return Task.FromResult(true);
        }
    }

    public Task UpdateTitleAsync(Guid id, string title, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _conversations[id].Title = title;
        }

        return Task.CompletedTask;
    }

    public Task UpdateTurnToolCallsAsync(Guid turnId, string toolCallsJson, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _conversations.Values.SelectMany(conversation => conversation.Turns).Single(turn => turn.Id == turnId).ToolCallsJson = toolCallsJson;
        }

        return Task.CompletedTask;
    }

    public Task SetActiveAgentAsync(Guid id, Guid? agentId, string? agentName, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _conversations[id].ActiveAgentId = agentId;
            _conversations[id].ActiveAgentName = agentId is null ? null : agentName;
        }

        return Task.CompletedTask;
    }

    public Task<bool> SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_conversations.TryGetValue(id, out var conversation))
            {
                return Task.FromResult(false);
            }

            conversation.Pinned = pinned;
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_conversations.TryRemove(id, out _));
        }
    }

    private static Conversation Copy(Conversation source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        AgentName = source.AgentName,
        AgentId = source.AgentId,
        ActiveAgentId = source.ActiveAgentId,
        ActiveAgentName = source.ActiveAgentName,
        Channel = source.Channel,
        Status = source.Status,
        SystemPrompt = source.SystemPrompt,
        PromptName = source.PromptName,
        PromptVersion = source.PromptVersion,
        PromptOrigin = source.PromptOrigin,
        Profile = source.Profile,
        SourceIds = source.SourceIds.ToList(),
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        Pinned = source.Pinned,
        AutoLabeledAt = source.AutoLabeledAt,
        Turns = source.Turns.Select(Copy).ToList(),
        Messages = source.Messages.Select(Copy).ToList(),
    };

    private static ConversationMessage Copy(ConversationMessage source) => new()
    {
        Id = source.Id,
        ConversationId = source.ConversationId,
        Sequence = source.Sequence,
        Author = source.Author,
        AuthorName = source.AuthorName,
        Text = source.Text,
        CitationsJson = source.CitationsJson,
        TurnId = source.TurnId,
        StaffOnly = source.StaffOnly,
        CreatedAt = source.CreatedAt,
    };

    private static ConversationTurn Copy(ConversationTurn source) => new()
    {
        Id = source.Id,
        ConversationId = source.ConversationId,
        Sequence = source.Sequence,
        UserMessage = source.UserMessage,
        AssistantMessage = source.AssistantMessage,
        ModelMessagesJson = source.ModelMessagesJson,
        ToolCallsJson = source.ToolCallsJson,
        CitationsJson = source.CitationsJson,
        Provider = source.Provider,
        Model = source.Model,
        InputTokens = source.InputTokens,
        OutputTokens = source.OutputTokens,
        FinishReason = source.FinishReason,
        Error = source.Error,
        DurationMs = source.DurationMs,
        TraceId = source.TraceId,
        CreatedAt = source.CreatedAt,
    };
}
