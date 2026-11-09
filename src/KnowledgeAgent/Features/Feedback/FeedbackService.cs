using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Feedback;

public sealed record SubmitFeedbackRequest(int Rating, Guid? MessageId = null, string? Comment = null, IReadOnlyList<string>? Reasons = null);

public sealed record FeedbackDto(
    Guid Id,
    Guid ConversationId,
    Guid? MessageId,
    string Kind,
    int Rating,
    string? Comment,
    IReadOnlyList<string> Reasons,
    string Author,
    Guid? WidgetId,
    Guid? AgentId,
    string? TraceId,
    DateTimeOffset CreatedAt,
    string? ConversationTitle = null,
    string? MessageText = null);

public sealed record FeedbackSummaryDto(
    Guid? WidgetId,
    Guid? AgentId,
    int Answers,
    int Positive,
    int Negative,
    double? PositiveRatio,
    int Ratings,
    double? AverageRating,
    int AgentRecorded);

public sealed record FeedbackAuthorRef(FeedbackAuthor Author, string Key);

public interface IFeedbackRecorder
{
    // Null when the conversation already has as many notes from the assistant as allowed.
    Task<Feedback?> RecordFromAgentAsync(Guid conversationId, int sentiment, string comment, int maxPerConversation, CancellationToken cancellationToken = default);
}

public interface IFeedbackService : IFeedbackRecorder
{
    Task<FeedbackDto> SubmitAsync(Conversation conversation, SubmitFeedbackRequest request, FeedbackAuthorRef author, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackDto>> ListAsync(FeedbackFilter filter, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeedbackSummaryDto>> SummarizeAsync(DateTimeOffset from, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> RatingsByAsync(Guid conversationId, string authorKey, CancellationToken cancellationToken = default);
}

internal sealed partial class FeedbackService(
    IFeedbackRepository feedback,
    IConversationRepository conversations,
    IJobQueue jobs,
    TimeProvider timeProvider,
    ILogger<FeedbackService> logger) : IFeedbackService
{
    public const int MaxReasons = 5;
    public const int SummaryLimit = 5000;

    public async Task<FeedbackDto> SubmitAsync(Conversation conversation, SubmitFeedbackRequest request, FeedbackAuthorRef author, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(author);

        var errors = new ValidationErrors();
        var kind = request.MessageId is null ? FeedbackKind.Conversation : FeedbackKind.Answer;
        if (kind == FeedbackKind.Answer && request.Rating is not (1 or -1))
        {
            errors.Add("rating", "Rate an answer with 1 (helpful) or -1 (not helpful).");
        }
        else if (kind == FeedbackKind.Conversation && request.Rating is < 1 or > 5)
        {
            errors.Add("rating", "Rate the conversation from 1 to 5.");
        }

        var comment = request.Comment?.Trim();
        if (comment is { Length: > Feedback.MaxCommentLength })
        {
            errors.Add("comment", $"The comment must be at most {Feedback.MaxCommentLength} characters long.");
        }

        var reasons = (request.Reasons ?? []).Select(reason => reason.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (reasons.Count > MaxReasons || reasons.Any(reason => !FeedbackReasons.All.Contains(reason, StringComparer.Ordinal)))
        {
            errors.Add("reasons", $"Choose up to {MaxReasons} of: {string.Join(", ", FeedbackReasons.All)}.");
        }

        string? traceId = null;
        if (request.MessageId is { } messageId)
        {
            var messages = await conversations.ListMessagesAsync(conversation.Id, cancellationToken: cancellationToken);
            var message = messages.FirstOrDefault(item => item.Id == messageId);
            if (message is null || message.Author != MessageAuthor.Assistant)
            {
                errors.Add("messageId", "Only an answer of this conversation can be rated.");
            }
            else if (message.TurnId is { } turnId)
            {
                traceId = (await conversations.GetAsync(conversation.Id, includeTurns: true, cancellationToken))?
                    .Turns.FirstOrDefault(turn => turn.Id == turnId)?.TraceId;
            }
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var now = timeProvider.GetUtcNow();
        var saved = await feedback.FindAsync(conversation.Id, request.MessageId, kind, author.Key, cancellationToken)
            ?? new Feedback { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, MessageId = request.MessageId, Kind = kind, CreatedAt = now };
        saved.Rating = request.Rating;
        saved.Comment = string.IsNullOrEmpty(comment) ? null : comment;
        saved.Reasons = reasons;
        saved.Author = author.Author;
        saved.AuthorKey = author.Key;
        saved.WidgetId = conversation.WidgetId;
        saved.AgentId = conversation.AgentId;
        saved.TraceId = traceId ?? saved.TraceId;
        saved.UpdatedAt = now;
        saved.ScoreSentAt = null;
        await feedback.SaveAsync(saved, cancellationToken);
        await QueueScoreAsync(saved, cancellationToken);
        LogSubmitted(logger, saved.Kind, saved.Rating, conversation.Id);
        return saved.ToDto();
    }

    public const string AgentAuthorKey = "agent";

    public async Task<Feedback?> RecordFromAgentAsync(Guid conversationId, int sentiment, string comment, int maxPerConversation, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns: false, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation {conversationId} does not exist.");
        var recorded = await feedback.ListByAuthorAsync(conversationId, AgentAuthorKey, cancellationToken);
        if (recorded.Count(item => item.Kind == FeedbackKind.Agent) >= maxPerConversation)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var saved = await feedback.SaveAsync(
            new Feedback
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                Kind = FeedbackKind.Agent,
                Rating = Math.Clamp(sentiment, -1, 1),
                Comment = comment.Length > Feedback.MaxCommentLength ? comment[..Feedback.MaxCommentLength] : comment,
                Author = FeedbackAuthor.Agent,
                AuthorKey = AgentAuthorKey,
                WidgetId = conversation.WidgetId,
                AgentId = conversation.AgentId,
                CreatedAt = now,
                UpdatedAt = now,
            },
            cancellationToken);
        await QueueScoreAsync(saved, cancellationToken);
        LogSubmitted(logger, saved.Kind, saved.Rating, conversationId);
        return saved;
    }

    public async Task<IReadOnlyList<FeedbackDto>> ListAsync(FeedbackFilter filter, int take, CancellationToken cancellationToken = default)
    {
        var items = await feedback.ListAsync(filter, Math.Clamp(take, 1, 500), cancellationToken);
        var result = new List<FeedbackDto>(items.Count);
        foreach (var group in items.GroupBy(item => item.ConversationId))
        {
            var conversation = await conversations.GetAsync(group.Key, includeTurns: true, cancellationToken);
            var messages = conversation?.Messages.ToDictionary(message => message.Id) ?? [];
            foreach (var item in group)
            {
                var text = item.MessageId is { } messageId && messages.TryGetValue(messageId, out var message) ? message.Text : null;
                result.Add(item.ToDto() with { ConversationTitle = conversation?.Title, MessageText = text is { Length: > 300 } ? text[..300] : text });
            }
        }

        return result.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id).ToList();
    }

    public async Task<IReadOnlyList<FeedbackSummaryDto>> SummarizeAsync(DateTimeOffset from, CancellationToken cancellationToken = default)
    {
        var items = await feedback.ListAsync(new FeedbackFilter(From: from), SummaryLimit, cancellationToken);
        return items
            .GroupBy(item => (item.WidgetId, item.AgentId))
            .Select(group =>
            {
                var answers = group.Where(item => item.Kind == FeedbackKind.Answer).ToList();
                var positive = answers.Count(item => item.Rating > 0);
                var ratings = group.Where(item => item.Kind == FeedbackKind.Conversation).ToList();
                return new FeedbackSummaryDto(
                    group.Key.WidgetId,
                    group.Key.AgentId,
                    answers.Count,
                    positive,
                    answers.Count - positive,
                    answers.Count == 0 ? null : Math.Round((double)positive / answers.Count, 3),
                    ratings.Count,
                    ratings.Count == 0 ? null : Math.Round(ratings.Average(item => item.Rating), 2),
                    group.Count(item => item.Kind == FeedbackKind.Agent));
            })
            .OrderByDescending(summary => summary.Answers + summary.Ratings + summary.AgentRecorded)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> RatingsByAsync(Guid conversationId, string authorKey, CancellationToken cancellationToken = default) =>
        (await feedback.ListByAuthorAsync(conversationId, authorKey, cancellationToken))
            .Where(item => item.Kind != FeedbackKind.Agent)
            .GroupBy(item => item.MessageId ?? Guid.Empty)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAt).First().Rating);

    private Task QueueScoreAsync(Feedback saved, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(FeedbackScoreJob.Kind, saved.Id.ToString(), cancellationToken: cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feedback {Kind} {Rating} on conversation {ConversationId}")]
    private static partial void LogSubmitted(ILogger logger, FeedbackKind kind, int rating, Guid conversationId);
}

internal static class FeedbackMappings
{
    public static FeedbackDto ToDto(this Feedback feedback) => new(
        feedback.Id,
        feedback.ConversationId,
        feedback.MessageId,
        feedback.Kind.ToString().ToLowerInvariant(),
        feedback.Rating,
        feedback.Comment,
        feedback.Reasons,
        feedback.Author.ToString().ToLowerInvariant(),
        feedback.WidgetId,
        feedback.AgentId,
        feedback.TraceId,
        feedback.CreatedAt);
}
