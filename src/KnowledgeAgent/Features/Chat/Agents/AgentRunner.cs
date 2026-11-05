using System.Globalization;
using System.Text.Json;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Chat.Agents;

internal sealed class AgentRunner(
    ToolRegistry toolRegistry,
    IServiceScopeFactory scopeFactory,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<AgentRunner> logger)
{
    public const string PromptNameProperty = KnowledgeAgentTelemetry.LangfusePromptNameKey;

    public const string PromptVersionProperty = KnowledgeAgentTelemetry.LangfusePromptVersionKey;

    public const string RefusalMessage = "The model declined to respond to this request.";

    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(30);

    internal const string ToolsDisabledResult =
        "Error: no more tool calls are allowed because the tool-call limit was reached. Answer with the information you already have.";

    private readonly AgentOptions _options = options.Value;

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        var agent = request.Agent;
        var tools = toolRegistry.Resolve(agent);
        var declarations = tools.Select(tool => (AITool)tool.Declaration).ToArray();
        var toolsByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var sink = new AgentEventSink(request.Turn.Events, agent.Name, request.ParentCallId);
        var maxToolRounds = Math.Max(1, agent.MaxIterations);

        var texts = new List<string>();
        var citations = new List<CitationDto>();
        var lastText = string.Empty;
        var iterations = 0;
        var toolRounds = 0;

        AgentRunResult Result(string finishReason, AgentRunError? error = null) => new(
            finishReason,
            iterations,
            lastText,
            string.Join("\n\n", texts),
            citations.DistinctBy(citation => citation.Ref).ToList(),
            error);

        void Append(ChatMessage message) => request.Messages.Add(message);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forceFinal = toolRounds >= maxToolRounds;
            if (forceFinal)
            {
                AgentLog.MaxIterationsReached(logger, agent.Name, maxToolRounds);
            }

            iterations++;
            ChatResponse response;
            try
            {
                response = await StreamResponseAsync(request, CreateOptions(request, declarations, forceFinal), sink, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                AgentLog.ModelCallFailed(logger, ex, agent.Name, iterations);
                return Result(ChatFinishReasons.Error, ToRunError(ex));
            }

            request.Turn.Usage.Add(response.Usage);
            foreach (var message in response.Messages.Where(message => message.Contents.Count > 0))
            {
                Append(message);
            }

            lastText = string.Concat(response.Messages.Select(message => message.Text));
            if (lastText.Length > 0)
            {
                texts.Add(lastText);
            }

            if (response.FinishReason == ChatFinishReason.ContentFilter)
            {
                AgentLog.Refused(logger, agent.Name, iterations);
                return Result(ChatFinishReasons.Refusal, new AgentRunError(ChatErrorCodes.Refusal, RefusalMessage));
            }

            var calls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .Where(call => !call.InformationalOnly)
                .ToList();

            if (calls.Count == 0)
            {
                return Result(
                    forceFinal ? ChatFinishReasons.MaxIterations
                    : response.FinishReason == ChatFinishReason.Length ? ChatFinishReasons.Length
                    : ChatFinishReasons.Stop);
            }

            if (forceFinal)
            {
                // The model ignored ToolMode.None. Answer every call so the history stays valid for later turns.
                foreach (var call in calls)
                {
                    AgentLog.ToolCallRejected(logger, call.CallId, agent.Name, "tool-call limit reached");
                }

                Append(new ChatMessage(
                    ChatRole.Tool,
                    calls.Select(call => (AIContent)new FunctionResultContent(call.CallId, ToolsDisabledResult)).ToList()));
                return Result(ChatFinishReasons.MaxIterations);
            }

            toolRounds++;
            var outcomes = await Task.WhenAll(calls.Select(call => ExecuteToolCallAsync(call, toolsByName, request.Turn, sink, confirmed: false, cancellationToken)));
            Append(new ChatMessage(ChatRole.Tool, outcomes.Select(outcome => (AIContent)outcome.Result).ToList()));
            citations.AddRange(outcomes.SelectMany(outcome => outcome.Citations));
            if (outcomes.Any(outcome => outcome.Tool.EndsRun))
            {
                return Result(ChatFinishReasons.Stop);
            }
        }
    }

    private static ChatOptions CreateOptions(AgentRunRequest request, IReadOnlyList<AITool> tools, bool final)
    {
        var chatOptions = request.Model.CreateOptions();
        if (tools.Count > 0)
        {
            // The final call still sends the tools, with ToolMode.None, so the cached prompt prefix stays valid.
            chatOptions.Tools = [.. tools];
            chatOptions.ToolMode = final ? ChatToolMode.None : ChatToolMode.Auto;
            chatOptions.AllowMultipleToolCalls = true;
        }
        else
        {
            chatOptions.Tools = null;
            chatOptions.ToolMode = null;
        }

        chatOptions.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        chatOptions.AdditionalProperties[PromptNameProperty] = request.Prompt.Name;
        if (request.Prompt.Version is int version)
        {
            chatOptions.AdditionalProperties[PromptVersionProperty] = version;
        }

        return chatOptions;
    }

    private static async Task<ChatResponse> StreamResponseAsync(
        AgentRunRequest request,
        ChatOptions chatOptions,
        AgentEventSink sink,
        CancellationToken cancellationToken)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in request.Model.Client
            .GetStreamingResponseAsync(request.Messages, chatOptions, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            updates.Add(update);
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text: { Length: > 0 } text }:
                        sink.Emit(new TextDeltaEvent(text));
                        break;
                    case TextReasoningContent { Text: { Length: > 0 } reasoning }:
                        sink.Emit(new ReasoningDeltaEvent(reasoning));
                        break;
                }
            }
        }

        return updates.ToChatResponse();
    }

    internal async Task<ToolResult> RunConfirmedAsync(
        IAgentTool tool,
        string callId,
        JsonElement? arguments,
        AgentTurnContext turn,
        string agent,
        CancellationToken cancellationToken)
    {
        var values = arguments is { ValueKind: JsonValueKind.Object } json
            ? json.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        var call = new FunctionCallContent(callId, tool.Name, values);
        var sink = new AgentEventSink(turn.Events, agent, parentCallId: null);
        var outcome = await ExecuteToolCallAsync(call, new Dictionary<string, IAgentTool>(StringComparer.Ordinal) { [tool.Name] = tool }, turn, sink, confirmed: true, cancellationToken);
        return outcome.Tool;
    }

    private async Task<ToolCallOutcome> ExecuteToolCallAsync(
        FunctionCallContent call,
        IReadOnlyDictionary<string, IAgentTool> toolsByName,
        AgentTurnContext turn,
        AgentEventSink sink,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        // Runs synchronously up to the first await, so tool-call events are emitted in call order.
        var startedAt = timeProvider.GetTimestamp();
        var arguments = ToolArguments.ToJson(call.Arguments);
        sink.Emit(new ToolCallEvent(call.CallId, call.Name, arguments));
        var logEntry = turn.ToolCalls.Start(call.CallId, call.Name, sink.Agent, sink.ParentCallId, arguments);
        using var activity = AgentTelemetry.StartTool(call.Name, call.CallId, sink.Agent, arguments);

        var result = await InvokeToolAsync(call, toolsByName, turn, sink, confirmed, cancellationToken);

        var text = TextUtil.TruncateToolOutput(result.Text, Math.Max(1_000, _options.MaxToolResultChars));
        var durationMs = (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
        var citations = result.Citations ?? [];
        sink.Emit(new ToolResultEvent(call.CallId, call.Name, result.IsError, result.Summary, durationMs, citations));
        ToolCallConfirmationDto? confirmation = null;
        if (result.Confirmation is { } prompt)
        {
            var expiresAt = timeProvider.GetUtcNow() + ConfirmationLifetime;
            confirmation = new ToolCallConfirmationDto(ToolCallConfirmationDto.Pending, prompt.Title, prompt.Connection, expiresAt);
            sink.Emit(new ConfirmationRequiredEvent(call.CallId, call.Name, prompt.Title, prompt.Connection, arguments, expiresAt));
        }
        else if (confirmed)
        {
            confirmation = new ToolCallConfirmationDto(ToolCallConfirmationDto.Confirmed, call.Name, null, timeProvider.GetUtcNow());
        }

        logEntry.Complete(result.Summary, result.IsError, durationMs, result.LogText, confirmation);
        AgentTelemetry.CompleteTool(activity, text, result.IsError, durationMs);
        return new ToolCallOutcome(new FunctionResultContent(call.CallId, text), citations, result with { Text = text });
    }

    private async Task<ToolResult> InvokeToolAsync(
        FunctionCallContent call,
        IReadOnlyDictionary<string, IAgentTool> toolsByName,
        AgentTurnContext turn,
        AgentEventSink sink,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!toolsByName.TryGetValue(call.Name, out var tool))
        {
            AgentLog.ToolCallRejected(logger, call.CallId, sink.Agent, "unknown tool");
            return ToolResult.Failure($"Unknown tool '{call.Name}'. Available tools: {string.Join(", ", toolsByName.Keys)}.");
        }

        if (call.Exception is not null)
        {
            AgentLog.ToolCallRejected(logger, call.CallId, sink.Agent, "unparseable arguments");
            return ToolResult.Failure(
                $"The arguments for {call.Name} could not be parsed ({call.Exception.Message}). Call the tool again with a valid JSON object.");
        }

        var timeout = tool.Timeout ?? TimeSpan.FromSeconds(Math.Clamp(_options.ToolTimeoutSeconds, 1, 86_400));
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        await using var scope = scopeFactory.CreateAsyncScope();
        var invocation = new ToolInvocation(call.CallId, new ToolArguments(call.Arguments), turn, sink.Agent, this, scope.ServiceProvider) { Confirmed = confirmed };

        try
        {
            return await tool.InvokeAsync(invocation, linkedSource.Token);
        }
        catch (ToolArgumentException ex)
        {
            return ToolResult.Failure(ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            AgentLog.ToolTimedOut(logger, call.Name, call.CallId, timeout.TotalSeconds);
            return ToolResult.Failure(string.Create(
                CultureInfo.InvariantCulture,
                $"{call.Name} timed out after {timeout.TotalSeconds:0.#} seconds. Try a narrower request."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            AgentLog.ToolFailed(logger, ex, call.Name, call.CallId);
            return ToolResult.Failure($"{call.Name} failed: {ex.Message}");
        }
    }

    private static AgentRunError ToRunError(Exception exception) => exception switch
    {
        ModelNotConfiguredException => new AgentRunError(ChatErrorCodes.ModelNotConfigured, exception.Message, exception),
        HttpRequestException { StatusCode: { } status } => new AgentRunError(
            ChatErrorCodes.ProviderError,
            string.Create(CultureInfo.InvariantCulture, $"The model provider returned an error (HTTP {(int)status}). Please try again."),
            exception),
        OperationCanceledException or TimeoutException => new AgentRunError(
            ChatErrorCodes.ProviderError,
            "The model provider did not respond in time. Please try again.",
            exception),
        _ => new AgentRunError(ChatErrorCodes.ProviderError, "The model provider returned an error. Please try again.", exception),
    };

    private sealed record ToolCallOutcome(FunctionResultContent Result, IReadOnlyList<CitationDto> Citations, ToolResult Tool);
}
