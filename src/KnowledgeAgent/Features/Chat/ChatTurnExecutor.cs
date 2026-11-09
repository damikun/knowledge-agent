using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.History;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat;

// StaffId is the signed-in subject, or "apikey:<id>" on the API and MCP channels.
public sealed record ChatCaller(
    ConversationChannel Channel,
    string? Name = null,
    Guid? WidgetId = null,
    Guid? VisitorId = null,
    bool HandoverAvailable = false,
    string? StaffId = null,
    Guid? ContactId = null,
    bool Verified = false,
    bool FeedbackAvailable = true)
{
    public static readonly ChatCaller Portal = new(ConversationChannel.Portal);

    public bool AsVisitor => Channel != ConversationChannel.Portal || WidgetId is not null;

    public bool IsApi => Channel is ConversationChannel.Api or ConversationChannel.Mcp;

    // Staff are signed in to the portal, so a playground test counts as a verified visitor.
    public bool IsVerified => Channel == ConversationChannel.Portal || Verified;
}

internal sealed class ChatTurnExecutor(
    IConversationRepository conversations,
    ConversationTimeline timeline,
    ISourceRepository sources,
    IPromptService prompts,
    IChatClientFactory chatClients,
    IAgentResolver agents,
    AgentCatalog catalog,
    AgentRunner runner,
    TitleGenerator titleGenerator,
    ConversationHistoryBuilder historyBuilder,
    IEnumerable<IFirstAnswerHook> firstAnswerHooks,
    IEnumerable<IVisitorProfile> visitorProfiles,
    IEnumerable<IAgentToolSource> toolSources,
    IEnumerable<IFeedbackRecorder> feedbackRecorders,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<ChatTurnExecutor> logger)
{
    private readonly AgentOptions _options = options.Value;

    public async Task RunTurnAsync(ChatRequestDto request, string message, ChatCaller caller, TurnBroadcast broadcast, CancellationToken cancellationToken)
    {
        var writer = broadcast.Writer;
        var previousActivity = Activity.Current;
        Activity? activity = null;
        try
        {
            // Each turn starts a new trace, linked to the request span instead of parented to it.
            Activity.Current = null;
            activity = AgentTelemetry.StartChatTurn(previousActivity, catalog.Orchestrator.Name, message);
            if (activity is null)
            {
                Activity.Current = previousActivity;
            }

            await ExecuteTurnAsync(request, message, caller, broadcast, activity, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag(AgentTelemetry.Tags.FinishReason, "cancelled");
            AgentLog.TurnCancelled(logger, request.ConversationId);
        }
        catch (Exception ex)
        {
            AgentLog.TurnSetupFailed(logger, ex, request.ConversationId);
            AgentTelemetry.RecordError(activity, ChatErrorCodes.InternalError, ChatService.InternalErrorMessage, ex);
            writer.TryWrite(new ErrorEvent(ChatService.InternalErrorMessage, ChatErrorCodes.InternalError));
        }
        finally
        {
            activity?.Stop();
            Activity.Current = previousActivity;
            writer.TryComplete();
        }
    }

    private async Task ExecuteTurnAsync(
        ChatRequestDto request,
        string message,
        ChatCaller caller,
        TurnBroadcast broadcast,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var writer = broadcast.Writer;
        var startedAt = timeProvider.GetTimestamp();

        Conversation conversation;
        AgentConfig agent;

        AgentConfig current;
        AgentDefinition orchestrator;
        var isNew = request.ConversationId is not { } requestedId || requestedId == Guid.Empty;
        if (isNew)
        {
            if (request.Confirmation is not null)
            {
                writer.TryWrite(new ErrorEvent(ConfirmationExpiredMessage, ChatErrorCodes.ConfirmationExpired));
                return;
            }

            try
            {
                agent = await agents.ResolveAsync(request.Agent, cancellationToken);
            }
            catch (AgentNotFoundException ex)
            {
                writer.TryWrite(new ErrorEvent(ex.Message, ChatErrorCodes.NotFound));
                return;
            }

            current = agent;
            orchestrator = await AgentForTurnAsync(agent, current, caller, allowHandoff: true, cancellationToken);
            var created = await CreateConversationAsync(request, message, caller, agent, orchestrator, writer, cancellationToken);
            if (created is null)
            {
                return;
            }

            conversation = created;
            broadcast.TryClaim(conversation.Id);
        }
        else
        {
            var existing = await conversations.GetAsync(request.ConversationId!.Value, includeTurns: true, cancellationToken);
            if (existing is null)
            {
                AgentLog.ConversationNotFound(logger, request.ConversationId.Value);
                AgentTelemetry.RecordError(activity, ChatErrorCodes.NotFound, "Conversation not found.");
                writer.TryWrite(new ErrorEvent($"Conversation {request.ConversationId.Value} was not found.", ChatErrorCodes.NotFound));
                return;
            }

            conversation = existing;
            agent = await agents.ResolveAsync(conversation.AgentId, fallbackKey: null, cancellationToken);
            if ((conversation.Channel != ConversationChannel.Portal || conversation.WidgetId is not null) && agent.Id != conversation.AgentId)
            {
                // A visitor, or a test of what one gets, must never end up with the built-in agent, which may search every source.
                writer.TryWrite(new ErrorEvent("The agent of this conversation no longer exists.", ChatErrorCodes.NotFound));
                return;
            }

            current = await ActiveAgentAsync(conversation, agent, cancellationToken);
            orchestrator = await AgentForTurnAsync(agent, current, caller, allowHandoff: true, cancellationToken);
        }

        PendingConfirmation? pending = null;
        if (request.Confirmation is { } answer)
        {
            pending = FindPendingConfirmation(conversation, answer.CallId);
            if (pending is null)
            {
                writer.TryWrite(new ErrorEvent(ConfirmationExpiredMessage, ChatErrorCodes.ConfirmationExpired));
                return;
            }
        }

        var turnId = Guid.CreateVersion7();
        var profile = ProfileFor(request, conversation, agent, current);
        AgentTelemetry.SetConversation(activity, conversation.Id, turnId, profile, conversation.PromptName, conversation.PromptVersion);
        AgentLog.TurnStarted(logger, turnId, conversation.Id, isNew, profile);
        writer.TryWrite(new ConversationEvent(conversation.Id, conversation.Title, isNew));

        // On the timeline at once, so other views see the question before the answer is written.
        await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Author = conversation.Channel == ConversationChannel.Portal ? MessageAuthor.User : MessageAuthor.Visitor,
                AuthorName = caller.Name,
                Text = message,
                TurnId = turnId,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            CancellationToken.None);

        var titleTask = isNew && _options.GenerateTitles
            ? Task.Run(() => titleGenerator.GenerateAsync(message, cancellationToken), CancellationToken.None)
            : null;

        // Built only from stored turns, so every turn starts with the same prefix and the prompt cache keeps working.
        var replayed = historyBuilder.Build(conversation);
        var history = replayed.Messages;
        AgentTelemetry.SetHistory(activity, replayed.Mode.ToString().ToLowerInvariant(), replayed.Turns, replayed.StubbedResults, replayed.EstimatedTokens);
        if (current != agent)
        {
            await UseSystemPromptAsync(history, conversation, request, agent, current, orchestrator, caller, cancellationToken);
        }

        var firstAppended = history.Count;

        var citations = new CitationRegistry(conversation.Turns
            .OrderBy(turn => turn.Sequence)
            .SelectMany(turn => ChatJson.DeserializeDtos<CitationDto>(turn.CitationsJson)));
        var scope = await agents.ResolveScopeAsync(current, conversation.SourceIds, cancellationToken);
        var turn = new AgentTurnContext(conversation.Id, turnId, scope, citations, writer) { Caller = caller };

        var outcome = new TurnOutcome();
        try
        {
            var prompt = pending is null
                ? message
                : await AnswerConfirmationAsync(pending, request.Confirmation!.Approved, message, orchestrator, turn, cancellationToken);
            history.Add(new ChatMessage(ChatRole.User, prompt));
            await RunTeamAsync(new TeamTurn(conversation, request, caller, agent, current, orchestrator, profile, history, turn, activity, outcome), cancellationToken);

            // The visitor saw that text, so an empty last step must not leave them without an answer.
            if (outcome.Error is null && string.IsNullOrWhiteSpace(outcome.Answer) && outcome.LastInterimText is { } interim)
            {
                AgentLog.AnswerFromInterim(logger, turnId, conversation.Id);
                outcome.Answer = interim;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && broadcast.StopRequested)
        {
            outcome.Stop(broadcast.AnswerSoFar());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentLog.TurnFailed(logger, ex, turnId, conversation.Id, ChatErrorCodes.InternalError);
            outcome.Fail(new AgentRunError(ChatErrorCodes.InternalError, ChatService.InternalErrorMessage, ex));
        }

        var usage = turn.Usage.Snapshot();
        var durationMs = (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        AgentRunError? persistError = null;
        try
        {
            // Failed turns keep their text for the UI but add no model messages, so the next turn's context stays valid.
            var committed = outcome.Error is null && !outcome.Stopped ? history.Skip(firstAppended).ToList() : [];
            var record = new ConversationTurn
            {
                Id = turnId,
                ConversationId = conversation.Id,
                UserMessage = message,
                AssistantMessage = outcome.Answer,
                ModelMessagesJson = ChatJson.SerializeMessages(committed, dropped => AgentLog.ContentNotPersisted(logger, dropped, turnId)),
                ToolCallsJson = ChatJson.SerializeDtos(turn.ToolCalls.Snapshot()),
                CitationsJson = ChatJson.SerializeDtos(citations.TurnCitations),
                Provider = outcome.Provider,
                Model = outcome.Model,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                FinishReason = outcome.FinishReason,
                Error = outcome.Error?.Message,
                DurationMs = durationMs,
                TraceId = activity?.TraceId.ToHexString(),
                CreatedAt = timeProvider.GetUtcNow(),
            };

            // Saved even when the client has gone: the turn runs apart from its request.
            await conversations.AddTurnAsync(record, CancellationToken.None);
            await AddAnswerToTimelineAsync(conversation, turnId, outcome, citations);
        }
        catch (Exception ex)
        {
            AgentLog.TurnPersistFailed(logger, ex, turnId, conversation.Id);
            persistError = new AgentRunError(ChatErrorCodes.InternalError, ChatService.PersistErrorMessage, ex);
        }

        if (titleTask is not null)
        {
            var title = await titleTask;
            try
            {
                await conversations.UpdateTitleAsync(conversation.Id, title, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AgentLog.TitleSaveFailed(logger, ex, conversation.Id);
            }

            writer.TryWrite(new ConversationEvent(conversation.Id, title, IsNew: true));
        }

        AgentTelemetry.CompleteChatTurn(activity, outcome.Answer, outcome.FinishReason, outcome.Iterations, usage, outcome.Error ?? persistError);
        if ((outcome.Error ?? persistError) is { } error)
        {
            writer.TryWrite(new ErrorEvent(error.Message, error.Code));
        }

        writer.TryWrite(new DoneEvent(conversation.Id, turnId, outcome.FinishReason));
        AgentLog.TurnCompleted(logger, turnId, conversation.Id, outcome.FinishReason, outcome.Iterations, durationMs);

        // After the done event, so queueing never delays the answer.
        if (isNew && outcome.Error is null && !outcome.Stopped && persistError is null)
        {
            await RunFirstAnswerHooksAsync(conversation);
        }
    }

    private async Task RunFirstAnswerHooksAsync(Conversation conversation)
    {
        foreach (var hook in firstAnswerHooks)
        {
            try
            {
                await hook.OnFirstAnswerAsync(conversation, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AgentLog.FirstAnswerHookFailed(logger, ex, conversation.Id);
            }
        }
    }

    private async Task AddAnswerToTimelineAsync(Conversation conversation, Guid turnId, TurnOutcome outcome, CitationRegistry citations)
    {
        var conversationId = conversation.Id;
        if (!string.IsNullOrWhiteSpace(outcome.Answer))
        {
            await timeline.AddAsync(
                new ConversationMessage
                {
                    Id = Guid.CreateVersion7(),
                    ConversationId = conversationId,
                    Author = MessageAuthor.Assistant,

                    AuthorName = outcome.AnsweredBy,
                    Text = outcome.Answer,
                    CitationsJson = ChatJson.SerializeDtos(citations.TurnCitations),
                    TurnId = turnId,
                    CreatedAt = timeProvider.GetUtcNow(),
                },
                CancellationToken.None);
        }
        else if (outcome.Error is { } error)
        {
            await timeline.AddAsync(
                new ConversationMessage
                {
                    Id = Guid.CreateVersion7(),
                    ConversationId = conversationId,
                    Author = MessageAuthor.System,

                    Text = conversation.Channel == ConversationChannel.Portal ? error.Message : VisitorErrorTexts.For(error.Code),
                    TurnId = turnId,
                    CreatedAt = timeProvider.GetUtcNow(),
                },
                CancellationToken.None);
        }
    }

    private async Task<AgentDefinition> AgentForAsync(AgentConfig agent, ChatCaller caller, CancellationToken cancellationToken)
    {
        var orchestrator = catalog.Orchestrator;
        var extra = new List<IAgentTool>();

        foreach (var source in agent.IsBuiltIn ? [] : toolSources)
        {
            extra.AddRange(await source.ResolveAsync(agent, caller, cancellationToken));
        }

        return orchestrator with
        {
            ToolNames = orchestrator.ToolNames
                .Where(tool => tool switch
                {
                    ToolNames.RequestHumanHandover => !agent.IsBuiltIn && agent.HandoverPolicy != HandoverPolicy.Never && caller.HandoverAvailable,
                    // Hosts without the feedback feature, such as an isolated eval run, would only offer a tool that fails.
                    ToolNames.RecordFeedback => agent.Tools.Contains(tool) && caller.FeedbackAvailable && feedbackRecorders.Any(),
                    _ => agent.Tools.Contains(tool),
                })
                .ToList(),
            ExtraTools = extra,
        };
    }

    // Hand-offs in one turn: to a specialist, back, and to another specialist. Each agent's own limits still apply.
    internal const int MaxHandoffsPerTurn = 3;

    private async Task<AgentDefinition> AgentForTurnAsync(AgentConfig front, AgentConfig current, ChatCaller caller, bool allowHandoff, CancellationToken cancellationToken)
    {
        var definition = await AgentForAsync(current, caller, cancellationToken);
        var isFront = current == front;
        var handoffs = new List<IAgentTool>();
        if (allowHandoff && isFront && !front.IsBuiltIn)
        {
            var choices = new List<SpecialistChoice>();
            foreach (var specialist in front.Specialists)
            {
                if (specialist.AgentId != front.Id && await agents.FindAsync(specialist.AgentId, cancellationToken) is { IsBuiltIn: false } found)
                {
                    choices.Add(new SpecialistChoice(found, specialist.WhenToUse));
                }
            }

            if (choices.Count > 0)
            {
                handoffs.Add(new TransferToAgentTool(choices));
            }
        }
        else if (allowHandoff && !isFront)
        {
            handoffs.Add(new TransferBackTool(front));
        }

        return definition with
        {
            Name = isFront ? definition.Name : current.Key,
            HandoffTools = handoffs,
        };
    }

    private async Task<AgentConfig> ActiveAgentAsync(Conversation conversation, AgentConfig front, CancellationToken cancellationToken)
    {
        if (conversation.ActiveAgentId is not { } activeId || activeId == front.Id)
        {
            return front;
        }

        if (!front.IsBuiltIn && front.Specialists.Any(specialist => specialist.AgentId == activeId)
            && await agents.FindAsync(activeId, cancellationToken) is { IsBuiltIn: false } specialist)
        {
            return specialist;
        }

        await conversations.SetActiveAgentAsync(conversation.Id, null, null, CancellationToken.None);
        conversation.ActiveAgentId = null;
        conversation.ActiveAgentName = null;
        return front;
    }

    private static string ProfileFor(ChatRequestDto request, Conversation conversation, AgentConfig front, AgentConfig current) =>
        !string.IsNullOrWhiteSpace(request.Profile) ? request.Profile.Trim()
        : current == front ? conversation.Profile ?? front.ProfileName
        : current.ProfileName;

    private async Task UseSystemPromptAsync(
        List<ChatMessage> history,
        Conversation conversation,
        ChatRequestDto request,
        AgentConfig front,
        AgentConfig current,
        AgentDefinition definition,
        ChatCaller caller,
        CancellationToken cancellationToken)
    {
        string text;
        if (current == front)
        {
            text = conversation.SystemPrompt;
        }
        else
        {
            var variables = await BuildSystemPromptVariablesAsync(request, current, definition, caller, conversation.SourceIds, cancellationToken);

            // Rendered on every turn, so the date is the conversation's, like the front agent's frozen prompt; today's date would change the cached prefix daily.
            variables["today"] = conversation.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            variables["frontAgentName"] = front.Name;
            variables["handBackEnabled"] = definition.HandoffTools.Any(tool => tool.Name == ToolNames.TransferBack);
            if (!front.ShowSpecialistName)
            {
                variables["agentName"] = front.Name;
            }

            var prompt = await prompts.RenderAsync(conversation.PromptName ?? definition.PromptName, variables, cancellationToken: cancellationToken);
            text = PromptMessages.ToSystemText(prompt);
        }

        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, text);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            history.Insert(0, new ChatMessage(ChatRole.System, text));
        }
    }

    private sealed record TeamTurn(
        Conversation Conversation,
        ChatRequestDto Request,
        ChatCaller Caller,
        AgentConfig Front,
        AgentConfig Current,
        AgentDefinition Definition,
        string Profile,
        List<ChatMessage> History,
        AgentTurnContext Turn,
        Activity? Activity,
        TurnOutcome Outcome);

    private async Task RunTeamAsync(TeamTurn team, CancellationToken cancellationToken)
    {
        var (conversation, request, caller, front, current, definition, profile, history, turn, activity, outcome) = team;
        var handoffs = 0;
        var reason = string.Empty;
        while (true)
        {
            using (var span = handoffs > 0 || current != front ? AgentTelemetry.StartHandoffAgent(definition.Name, current.Name, current.Id, reason) : null)
            {
                await RunOrchestratorAsync(conversation, definition, profile, history, turn, turn.Events, activity, outcome, cancellationToken);
                span?.SetTag(AgentTelemetry.Tags.FinishReason, outcome.FinishReason);
            }

            outcome.AnsweredBy = current == front ? null : current.Name;
            if (outcome.Error is not null || turn.TakeTransfer() is not { } transfer)
            {
                return;
            }

            handoffs++;
            reason = transfer.Reason;
            await HandOffAsync(conversation, front, definition.Name, transfer, turn.Events);
            current = transfer.Back ? front : transfer.Target;
            definition = await AgentForTurnAsync(front, current, caller, allowHandoff: handoffs < MaxHandoffsPerTurn, cancellationToken);
            profile = ProfileFor(request, conversation, front, current);
            ConversationHistoryBuilder.DropReasoningOfCurrentTurn(history);
            await UseSystemPromptAsync(history, conversation, request, front, current, definition, caller, cancellationToken);
            turn.ScopeSourceIds = await agents.ResolveScopeAsync(current, conversation.SourceIds, cancellationToken);
        }
    }

    private async Task HandOffAsync(Conversation conversation, AgentConfig front, string fromAgent, AgentTransfer transfer, ChannelWriter<ChatStreamEvent> writer)
    {
        var target = transfer.Back ? front : transfer.Target;
        var shown = front.ShowSpecialistName;
        await conversations.SetActiveAgentAsync(conversation.Id, transfer.Back ? null : target.Id, target.Name, CancellationToken.None);
        conversation.ActiveAgentId = transfer.Back ? null : target.Id;
        conversation.ActiveAgentName = transfer.Back ? null : target.Name;

        var sink = new AgentEventSink(writer, fromAgent, parentCallId: null);
        sink.Emit(new AgentTransferEvent(transfer.CallId, target.Id == Guid.Empty ? null : target.Id, target.Key, target.Name, transfer.Reason, transfer.Back));
        AgentLog.HandedOff(logger, conversation.Id, fromAgent, target.Key);

        await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Author = MessageAuthor.System,
                AuthorName = target.Name,
                Text = (transfer.Back, shown) switch
                {
                    (false, true) => $"{target.Name} joined",
                    (false, false) => $"Handed to {target.Name}. The visitor does not see this.",
                    (true, true) => $"{target.Name} took over again",
                    (true, false) => $"Handed back to {target.Name}. The visitor does not see this.",
                },

                // Not tied to the turn: a visitor sees a notice of a turn as a failure.
                StaffOnly = !shown,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            CancellationToken.None);
    }

    private static long? Minus(long? total, long? before) => total is null ? null : total - (before ?? 0);

    internal const string ConfirmationExpiredMessage = "This action no longer waits for an answer. Ask again if it is still needed.";

    private sealed record PendingConfirmation(ConversationTurn Turn, IReadOnlyList<ToolCallDto> Calls, ToolCallDto Call);

    private PendingConfirmation? FindPendingConfirmation(Conversation conversation, string callId)
    {
        var latest = conversation.Turns.OrderBy(turn => turn.Sequence).LastOrDefault();
        if (latest is null)
        {
            return null;
        }

        var calls = ChatJson.DeserializeDtos<ToolCallDto>(latest.ToolCallsJson);
        var call = calls.FirstOrDefault(item => item.CallId == callId && item.ParentCallId is null);
        return call is { Confirmation: { Status: ToolCallConfirmationDto.Pending } confirmation } && confirmation.ExpiresAt > timeProvider.GetUtcNow()
            ? new PendingConfirmation(latest, calls, call)
            : null;
    }

    // Marked answered before it runs, so a repeated Confirm can never run it twice.
    private async Task<string> AnswerConfirmationAsync(
        PendingConfirmation pending,
        bool approved,
        string message,
        AgentDefinition orchestrator,
        AgentTurnContext turn,
        CancellationToken cancellationToken)
    {
        var call = pending.Call;
        var status = approved ? ToolCallConfirmationDto.Confirmed : ToolCallConfirmationDto.Declined;
        var answered = pending.Calls
            .Select(item => item.CallId == call.CallId && item.Confirmation is { } confirmation ? item with { Confirmation = confirmation with { Status = status } } : item)
            .ToList();
        await conversations.UpdateTurnToolCallsAsync(pending.Turn.Id, ChatJson.SerializeDtos(answered), CancellationToken.None);

        var title = call.Confirmation!.Title;
        if (!approved)
        {
            return $"{message}\n\n(The person pressed Cancel for \"{title}\" ({call.ToolName}). It did not run. Do not call it again unless they ask for it.)";
        }

        if (orchestrator.ExtraTools.FirstOrDefault(tool => tool.Name == call.ToolName) is not { } tool)
        {
            return $"{message}\n\n(The person pressed Confirm for \"{title}\" ({call.ToolName}), but that tool is no longer available to you, so it did not run. Tell them.)";
        }

        var result = await runner.RunConfirmedAsync(tool, call.CallId + "_confirmed", call.Arguments, turn, orchestrator.Name, cancellationToken);
        var outcome = result.IsError ? "It failed" : "It ran";
        return $"{message}\n\n(The person pressed Confirm for \"{title}\" ({call.ToolName}) with the arguments you gave. {outcome}; the tool returned:\n{result.Text}\nTell them the outcome.)";
    }

    private async Task RunOrchestratorAsync(
        Conversation conversation,
        AgentDefinition orchestrator,
        string profile,
        List<ChatMessage> history,
        AgentTurnContext turn,
        ChannelWriter<ChatStreamEvent> writer,
        Activity? activity,
        TurnOutcome outcome,
        CancellationToken cancellationToken)
    {
        // The prompt text is frozen per conversation, but model settings follow the prompt's current config.
        var promptPatch = await GetPromptPatchAsync(conversation, profile, cancellationToken);

        ChatModelHandle model;
        try
        {
            model = await chatClients.CreateAsync(profile, promptPatch, requestPatch: null, cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            AgentLog.ModelNotConfigured(logger, profile, ex.Message);
            outcome.Fail(new AgentRunError(ChatErrorCodes.ModelNotConfigured, ex.Message, ex));
            return;
        }

        outcome.Provider = model.Profile.ProviderKey;
        outcome.Model = model.Profile.Model;
        AgentTelemetry.SetModel(activity, model.Profile);
        var sink = new AgentEventSink(writer, orchestrator.Name, parentCallId: null);
        sink.Emit(new AgentStartedEvent(model.Profile.ProviderKey, model.Profile.Model));
        var before = turn.Usage.Snapshot();

        var result = await runner.RunAsync(
            new AgentRunRequest(
                orchestrator,
                model,
                history,
                new PromptReference(conversation.PromptName ?? orchestrator.PromptName, conversation.PromptVersion),
                turn),
            cancellationToken);

        var usage = turn.Usage.Snapshot();
        sink.Emit(new UsageEvent(
            model.Profile.ProviderKey,
            model.Profile.Model,
            Minus(usage.InputTokens, before.InputTokens),
            Minus(usage.OutputTokens, before.OutputTokens),
            Minus(usage.TotalTokens, before.TotalTokens)));
        sink.Emit(new AgentCompletedEvent(result.FinishReason, result.Iterations));

        if (!_options.AnswerIsFinalStep)
        {
            outcome.Answer = outcome.Answer.Length > 0 && result.FullText.Length > 0
                ? outcome.Answer + "\n\n" + result.FullText
                : outcome.Answer + result.FullText;
        }
        else
        {
            // The answer is the last step of the agent that answered; after a hand-off the specialist's replaces the front agent's.
            if (result.FinalText.Length > 0 || result.Error is not null)
            {
                outcome.Answer = result.FinalText;
            }

            if (result.Steps.LastOrDefault(step => step.Interim && !string.IsNullOrWhiteSpace(step.Text)) is { } interim)
            {
                outcome.LastInterimText = interim.Text;
            }
        }

        outcome.Iterations += result.Iterations;
        outcome.FinishReason = result.FinishReason;
        outcome.Error = result.Error;
        if (result.Error is { } error)
        {
            AgentLog.TurnFailed(logger, error.Exception, turn.TurnId, conversation.Id, error.Code);
        }
    }

    private async Task<Conversation?> CreateConversationAsync(
        ChatRequestDto request,
        string message,
        ChatCaller caller,
        AgentConfig agent,
        AgentDefinition orchestrator,
        ChannelWriter<ChatStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        var requestedScope = (request.SourceIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        var scope = await ExistingSourceIdsAsync(requestedScope, cancellationToken);
        if (requestedScope.Count > 0 && scope.Count == 0)
        {
            // Searching everything instead would silently widen what the user asked for.
            writer.TryWrite(new ErrorEvent(ChatService.ScopeNotFoundMessage, ChatErrorCodes.NotFound));
            return null;
        }

        var variables = await BuildSystemPromptVariablesAsync(request, agent, orchestrator, caller, scope, cancellationToken);

        RenderedPrompt prompt;
        try
        {
            prompt = await prompts.RenderAsync(orchestrator.PromptName, variables, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is PromptNotFoundException or PromptRenderException)
        {
            AgentLog.TurnSetupFailed(logger, ex, null);
            writer.TryWrite(new ErrorEvent($"The system prompt could not be prepared: {ex.Message}", ChatErrorCodes.InternalError));
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            Title = _options.GenerateTitles ? ChatService.DefaultTitle : TitleGenerator.Fallback(message),
            AgentName = orchestrator.Name,
            AgentId = agent.Id == Guid.Empty ? null : agent.Id,
            Channel = caller.Channel,
            WidgetId = caller.WidgetId,
            VisitorId = caller.VisitorId,
            ContactId = caller.Channel == ConversationChannel.Widget ? caller.ContactId : null,
            CreatedById = caller.Channel == ConversationChannel.Portal || caller.IsApi ? Shortened(caller.StaffId) : null,
            CreatedByName = (caller.Channel == ConversationChannel.Portal || caller.IsApi) && caller.StaffId is not null ? Shortened(caller.Name ?? caller.StaffId) : null,
            SystemPrompt = PromptMessages.ToSystemText(prompt),
            PromptName = prompt.Name,
            PromptVersion = prompt.Version,
            PromptOrigin = prompt.Origin.ToString().ToLowerInvariant(),
            Profile = string.IsNullOrWhiteSpace(request.Profile) ? agent.ProfileName : request.Profile.Trim(),
            SourceIds = scope,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return await conversations.CreateAsync(conversation, cancellationToken);
    }

    // The creator columns hold 200 characters; an identity provider may send longer names.
    private static string? Shortened(string? value) => value is { Length: > 200 } ? value[..200] : value;

    private async Task<List<Guid>> ExistingSourceIdsAsync(IReadOnlyList<Guid> requested, CancellationToken cancellationToken)
    {
        var existing = new HashSet<Guid>();
        foreach (var batch in requested.Chunk(SourceQuery.MaxLimit))
        {
            var page = await sources.FindAsync(new SourceQuery(Ids: batch, Limit: batch.Length), cancellationToken);
            existing.UnionWith(page.Items.Select(source => source.Id));
        }

        return requested.Where(existing.Contains).ToList();
    }

    private async Task<Dictionary<string, object?>> BuildSystemPromptVariablesAsync(
        ChatRequestDto request,
        AgentConfig agent,
        AgentDefinition orchestrator,
        ChatCaller caller,
        IReadOnlyList<Guid> scope,
        CancellationToken cancellationToken)
    {
        // Totals only, so the prompt does not grow with the number of sources. A scoped agent describes only
        // what it may search, so visitors don't learn what else is stored.
        var agentScope = await agents.ResolveScopeAsync(agent, [], cancellationToken);
        var stats = agentScope is null ? await sources.GetStatsAsync(cancellationToken) : await StatsOfAsync(agentScope, cancellationToken);
        var sourceTypes = stats.ReadyCountByType
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = entry.Key.ToString().ToLowerInvariant(),
                ["count"] = entry.Value,
            })
            .ToList();

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["agentName"] = agent.Name,
            ["appName"] = _options.AppName,
            ["today"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["sourceCount"] = stats.Count(SourceStatus.Ready),
            ["chunkCount"] = stats.ReadyChunkCount,
            ["sourceTypes"] = sourceTypes,
            ["pendingSourceCount"] = stats.Count(SourceStatus.Pending) + stats.Count(SourceStatus.Processing) + stats.Count(SourceStatus.Waiting),
            ["researcherEnabled"] = _options.ResearcherEnabled && orchestrator.ToolNames.Contains(ToolNames.DelegateResearch),
            ["allowGeneralKnowledge"] = agent.AllowGeneralKnowledge,
            ["feedbackEnabled"] = orchestrator.ToolNames.Contains(ToolNames.RecordFeedback),
            ["handoverEnabled"] = orchestrator.ToolNames.Contains(ToolNames.RequestHumanHandover),
            ["offerHandoverWhenUnsure"] = orchestrator.ToolNames.Contains(ToolNames.RequestHumanHandover) && agent.HandoverPolicy == HandoverPolicy.WhenUnsure,
            ["isVisitor"] = caller.AsVisitor,
        };

        if (!string.IsNullOrWhiteSpace(agent.Instructions))
        {
            variables["instructions"] = agent.Instructions;
        }

        if (!string.IsNullOrWhiteSpace(agent.Language))
        {
            variables["language"] = agent.Language;
        }

        if (scope.Count > 0)
        {
            variables["scopedSourceCount"] = scope.Count;
        }

        if (orchestrator.ExtraTools.Count > 0)
        {
            variables["connectionToolsEnabled"] = true;
        }

        if (orchestrator.HandoffTools.Any(tool => tool.Name == ToolNames.TransferToAgent))
        {
            variables["specialistsEnabled"] = true;
        }

        // Only for the visitor's own conversations.
        if (caller.Channel == ConversationChannel.Widget && caller.ContactId is { } contactId)
        {
            var facts = new List<object?>();
            foreach (var profile in visitorProfiles)
            {
                facts.AddRange((await profile.FactsAsync(contactId, cancellationToken)).Select(fact => (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["label"] = fact.Label,
                    ["value"] = fact.Value,
                    ["verified"] = fact.Verified,
                }));
            }

            if (facts.Count > 0)
            {
                variables["visitorFacts"] = facts;
            }
        }

        foreach (var (key, value) in request.Variables ?? new Dictionary<string, JsonElement>())
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                variables[key] = value;
            }
        }

        return variables;
    }

    private async Task<SourceStats> StatsOfAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken)
    {
        var byStatus = new Dictionary<SourceStatus, int>();
        var readyByType = new Dictionary<SourceType, int>();
        long readyChunks = 0;
        foreach (var batch in sourceIds.Chunk(SourceQuery.MaxLimit))
        {
            var page = await sources.FindAsync(new SourceQuery(Ids: batch, Limit: batch.Length), cancellationToken);
            foreach (var source in page.Items)
            {
                byStatus[source.Status] = byStatus.GetValueOrDefault(source.Status) + 1;
                if (source.Status == SourceStatus.Ready)
                {
                    readyByType[source.Type] = readyByType.GetValueOrDefault(source.Type) + 1;
                    readyChunks += source.ChunkCount;
                }
            }
        }

        return new SourceStats(byStatus, readyByType, readyChunks, new Dictionary<string, int>());
    }

    private async Task<ModelProfilePatch?> GetPromptPatchAsync(Conversation conversation, string profile, CancellationToken cancellationToken)
    {
        var promptName = conversation.PromptName ?? catalog.Orchestrator.PromptName;
        try
        {
            var template = await prompts.GetTemplateAsync(promptName, selector: null, cancellationToken);

            // A config written for another profile must not override a profile the user picked explicitly.
            var configProfile = PromptConfig.ParseProfile(template.Config);
            return configProfile is not null && !string.Equals(configProfile, profile, StringComparison.OrdinalIgnoreCase)
                ? null
                : PromptConfig.ParseModelPatch(template.Config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AgentLog.PromptConfigUnavailable(logger, ex, promptName);
            return null;
        }
    }

    private sealed class TurnOutcome
    {
        public string Answer { get; set; } = string.Empty;
        public string FinishReason { get; set; } = ChatFinishReasons.Stop;
        public int Iterations { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public AgentRunError? Error { get; set; }

        public string? AnsweredBy { get; set; }

        public string? LastInterimText { get; set; }

        public bool Stopped { get; private set; }

        public void Fail(AgentRunError error)
        {
            Error = error;
            FinishReason = ChatFinishReasons.Error;
        }

        public void Stop(string answerSoFar)
        {
            Stopped = true;
            Answer = answerSoFar;
            FinishReason = ChatFinishReasons.Stopped;
        }
    }
}
