using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Feedback;

// A job, so Langfuse being down only delays the score.
internal sealed partial class FeedbackScoreJob(
    IFeedbackRepository feedback,
    LangfuseEvalClient langfuse,
    TimeProvider timeProvider,
    ILogger<FeedbackScoreJob> logger) : IJobHandler
{
    public const string Kind = "feedback.score";

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!langfuse.IsConfigured || !Guid.TryParse(context.Key, out var id) || await feedback.GetAsync(id, cancellationToken) is not { } item)
        {
            return;
        }

        if (item.Kind == FeedbackKind.Answer && item.TraceId is null)
        {
            LogNoTrace(logger, item.Id);
            return;
        }

        var score = new LangfuseScore(
            item.Id.ToString(),
            item.Kind switch
            {
                FeedbackKind.Answer => "user-feedback",
                FeedbackKind.Conversation => "conversation-rating",
                _ => "agent-recorded-feedback",
            },
            item.Rating,
            TraceId: item.Kind == FeedbackKind.Answer ? item.TraceId : null,
            SessionId: item.Kind == FeedbackKind.Answer ? null : item.ConversationId.ToString(),
            Comment: item.Comment,
            Metadata: new JsonObject
            {
                ["author"] = item.Author.ToString().ToLowerInvariant(),
                ["reasons"] = new JsonArray(item.Reasons.Select(reason => (JsonNode)reason).ToArray()),
                ["widgetId"] = item.WidgetId?.ToString(),
                ["agentId"] = item.AgentId?.ToString(),
            });
        var result = await langfuse.CreateScoresAsync([score], cancellationToken);
        if (result.Rejected > 0)
        {
            LogRejected(logger, item.Id, string.Join("; ", result.Errors));
            return;
        }

        await feedback.MarkScoreSentAsync(item.Id, timeProvider.GetUtcNow(), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Feedback {FeedbackId} has no trace to score")]
    private static partial void LogNoTrace(ILogger logger, Guid feedbackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Langfuse rejected the score of feedback {FeedbackId}: {Errors}")]
    private static partial void LogRejected(ILogger logger, Guid feedbackId, string errors);
}
