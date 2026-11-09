using System.Diagnostics;
using System.Text;
using KnowledgeAgent.Features.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Evaluations.Running;

internal static class AgentTurnCapture
{
    public static async Task<AgentTurnResult> RunAsync(
        IServiceProvider services,
        string question,
        string? profile,
        CancellationToken cancellationToken,
        Guid? conversationId = null)
    {
        var started = Stopwatch.GetTimestamp();
        var answer = new StringBuilder();
        var lastInterim = string.Empty;
        var available = new Dictionary<string, CitationDto>(StringComparer.OrdinalIgnoreCase);
        string? finishReason = null, error = null, errorCode = null, provider = null, model = null;
        long? inputTokens = null, outputTokens = null;
        var toolCalls = 0;

        await using var scope = services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        try
        {
            await foreach (var streamEvent in chat.StreamAsync(new ChatRequestDto(conversationId, question, Profile: profile), cancellationToken))
            {
                var isOrchestrator = streamEvent.ParentCallId is null;
                switch (streamEvent)
                {
                    case ConversationEvent conversation:
                        conversationId = conversation.ConversationId;
                        break;
                    case AgentStartedEvent agentStarted when isOrchestrator:
                        provider = agentStarted.Provider;
                        model = agentStarted.Model;
                        break;
                    case TextDeltaEvent delta when isOrchestrator:
                        answer.Append(delta.Text);
                        break;

                    // Scored like the stored answer: text written before tool calls is not part of it.
                    case StepFinishedEvent { Kind: StepFinishedEvent.Interim } when isOrchestrator:
                        if (!string.IsNullOrWhiteSpace(answer.ToString()))
                        {
                            lastInterim = answer.ToString();
                        }

                        answer.Clear();
                        break;
                    case ToolCallEvent:
                        toolCalls++;
                        break;
                    case ToolResultEvent result:
                        foreach (var citation in result.Citations)
                        {
                            available[citation.Ref] = citation;
                        }

                        break;
                    case UsageEvent usage when isOrchestrator:
                        inputTokens = usage.InputTokens;
                        outputTokens = usage.OutputTokens;
                        break;
                    case AgentCompletedEvent completed when isOrchestrator:
                        finishReason = completed.FinishReason;
                        break;
                    case ErrorEvent failure:
                        error = failure.Message;
                        errorCode = failure.Code;
                        break;
                    case DoneEvent done:
                        conversationId = done.ConversationId;
                        finishReason = done.FinishReason;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            error ??= ex.Message;
            errorCode ??= ChatErrorCodes.InternalError;
        }

        var text = answer.ToString().Trim();
        if (text.Length == 0 && error is null && finishReason != ChatFinishReasons.Stopped)
        {
            text = lastInterim.Trim();
        }

        return new AgentTurnResult(
            text,
            available,
            finishReason,
            error,
            errorCode,
            provider,
            model,
            inputTokens,
            outputTokens,
            toolCalls,
            conversationId,
            TraceId: null,
            RootSpanId: null,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
