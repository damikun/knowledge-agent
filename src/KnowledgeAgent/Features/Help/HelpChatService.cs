using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using KnowledgeAgent.Features.Agents;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Help;

internal sealed partial class HelpChatService(
    HelpLibrary library,
    HelpSetupReader setup,
    HelpPrompt prompts,
    IChatClientFactory chatClients,
    AgentRunner runner,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<HelpChatService> logger)
{
    public const string Channel = "help";
    public const string AgentName = "help";
    public const string TraceName = "help-turn";
    public const int MaxMessageLength = 2_000;
    public const int MaxHistoryMessages = 12;
    public const int MaxHistoryChars = 6_000;
    private const int MaxToolRounds = 5;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        HelpChatRequest request,
        bool isAdmin,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = System.Threading.Channels.Channel.CreateUnbounded<ChatStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
        var run = RunAsync(request, isAdmin, events.Writer, cancellationToken);
        await foreach (var streamEvent in events.Reader.ReadAllAsync(CancellationToken.None))
        {
            yield return streamEvent;
        }

        await run;
    }

    private async Task RunAsync(HelpChatRequest request, bool isAdmin, ChannelWriter<ChatStreamEvent> writer, CancellationToken cancellationToken)
    {
        // Yield first, so the caller reads events while the turn runs.
        await Task.Yield();
        var previous = Activity.Current;
        Activity? activity = null;
        var sessionId = request.SessionId is { } id && id != Guid.Empty ? id : Guid.CreateVersion7();
        var turnId = Guid.CreateVersion7();
        try
        {
            Activity.Current = null;
            activity = AgentTelemetry.StartChatTurn(previous, AgentName, request.Message);
            activity?.SetTag(AgentTelemetry.Tags.TraceName, TraceName);
            activity?.SetTag(AgentTelemetry.Tags.SessionId, $"{Channel}-{sessionId:D}");
            activity?.SetTag("knowledge_agent.channel", Channel);
            if (activity is null)
            {
                Activity.Current = previous;
            }

            writer.TryWrite(new ConversationEvent(sessionId, "Help", IsNew: request.History is not { Count: > 0 }));
            var outcome = await AnswerAsync(request, isAdmin, sessionId, turnId, writer, activity, cancellationToken);
            writer.TryWrite(new DoneEvent(sessionId, turnId, outcome));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag(AgentTelemetry.Tags.FinishReason, "cancelled");
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
            AgentTelemetry.RecordError(activity, ChatErrorCodes.InternalError, ChatService.InternalErrorMessage, ex);
            writer.TryWrite(new ErrorEvent(ChatService.InternalErrorMessage, ChatErrorCodes.InternalError));
            writer.TryWrite(new DoneEvent(sessionId, turnId, ChatFinishReasons.Error));
        }
        finally
        {
            activity?.Stop();
            Activity.Current = previous;
            writer.TryComplete();
        }
    }

    private async Task<string> AnswerAsync(
        HelpChatRequest request,
        bool isAdmin,
        Guid sessionId,
        Guid turnId,
        ChannelWriter<ChatStreamEvent> writer,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        ChatModelHandle model;
        try
        {
            model = await chatClients.CreateAsync(AgentDefaults.ProfileName, cancellationToken: cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            writer.TryWrite(new ErrorEvent(ex.Message, ChatErrorCodes.ModelNotConfigured));
            return ChatFinishReasons.Error;
        }

        var index = await library.GetIndexAsync(cancellationToken);
        var facts = await setup.ReadAsync(cancellationToken);
        var prompt = prompts.Render(Variables(request.Page, isAdmin, facts, index.Contents));
        AgentTelemetry.SetConversation(activity, sessionId, turnId, AgentDefaults.ProfileName, prompt.Name, promptVersion: null);
        activity?.SetTag(AgentTelemetry.Tags.SessionId, $"{Channel}-{sessionId:D}");
        AgentTelemetry.SetModel(activity, model.Profile);
        writer.TryWrite(new AgentStartedEvent(model.Profile.ProviderKey, model.Profile.Model));

        var messages = new List<ChatMessage> { new(ChatRole.System, PromptMessages.ToSystemText(prompt)) };
        messages.AddRange(History(request.History));
        messages.Add(new ChatMessage(ChatRole.User, request.Message.Trim()));

        // Empty scope keeps every other tool blind; search_internal reads only the Internal folder.
        var turn = new AgentTurnContext(sessionId, turnId, scopeSourceIds: [], new CitationRegistry(), writer);
        var agent = new AgentDefinition(AgentName, HelpPrompt.Name, AgentDefaults.ProfileName, ToolNames: [], MaxToolRounds)
        {
            ExtraTools = [new ReadHelpSectionTool(index), new SearchHelpTool(index, request.Page), new SearchInternalTool()],
        };

        var result = await runner.RunAsync(
            new AgentRunRequest(agent, model, messages, new PromptReference(prompt.Name, Version: null), turn),
            cancellationToken);

        var usage = turn.Usage.Snapshot();
        writer.TryWrite(new UsageEvent(model.Profile.ProviderKey, model.Profile.Model, usage.InputTokens, usage.OutputTokens, usage.TotalTokens));
        writer.TryWrite(new AgentCompletedEvent(result.FinishReason, result.Iterations));
        AgentTelemetry.CompleteChatTurn(activity, result.FullText, result.FinishReason, result.Iterations, usage, result.Error);
        if (result.Error is { } error)
        {
            if (error.Exception is not null)
            {
                LogModelFailed(logger, error.Exception, error.Code);
            }

            writer.TryWrite(new ErrorEvent(error.Message, error.Code));
        }

        return result.FinishReason;
    }

    private Dictionary<string, object?> Variables(string? page, bool isAdmin, HelpSetupDto facts, string contents)
    {
        var missing = new List<object?>();
        if (!facts.ChatModel || !facts.EmbeddingModel)
        {
            missing.Add(!facts.ChatModel ? "a chat model (AI models page)" : "an embedding model (AI models page)");
        }

        if (!facts.Sources)
        {
            missing.Add("knowledge (Sources page)");
        }

        if (!facts.Agent)
        {
            missing.Add("an agent of their own (Agents page)");
        }

        if (!facts.Widget)
        {
            missing.Add("a widget on their website (Widgets page)");
        }

        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["appName"] = options.Value.AppName,
            ["today"] = timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["isAdmin"] = isAdmin,
            ["missingSetup"] = missing,
            ["contents"] = contents,
        };
        if (HelpPages.Describe(page) is { } pageName)
        {
            variables["pageName"] = pageName;
            variables["pagePath"] = page!.Split('?', '#')[0];
        }

        return variables;
    }

    // Tags of earlier answers are dropped because this turn numbers its sections afresh.
    private static IEnumerable<ChatMessage> History(IReadOnlyList<HelpHistoryMessage>? history)
    {
        if (history is not { Count: > 0 })
        {
            return [];
        }

        var kept = new List<ChatMessage>();
        var budget = MaxHistoryChars;
        foreach (var message in history.TakeLast(MaxHistoryMessages).Reverse())
        {
            var text = CitationTag().Replace(message.Text ?? string.Empty, string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > budget)
            {
                break;
            }

            budget -= text.Length;
            kept.Add(new ChatMessage(string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User, text));
        }

        kept.Reverse();
        return kept;
    }

    [GeneratedRegex(@"\s*\[\s*[Ss]\d+(?:\s*[,;]\s*[Ss]\d+)*\s*\]")]
    private static partial Regex CitationTag();

    [LoggerMessage(Level = LogLevel.Error, Message = "The help answer failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The help answer's model call failed ({Code})")]
    private static partial void LogModelFailed(ILogger logger, Exception exception, string code);
}
