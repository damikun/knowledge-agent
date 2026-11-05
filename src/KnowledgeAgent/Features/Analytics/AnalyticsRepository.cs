using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.People;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeAgent.Features.Analytics;

public interface IAnalyticsRepository
{
    Task<AnalyticsDto> GetAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnalyticsSegmentDto>> SegmentsAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default);
}

internal sealed class AnalyticsRepository(KnowledgeDbContext db, DbContextGate gate, IPeopleRepository people, ISegmentService segments) : IAnalyticsRepository
{
    internal const int MaxSegments = 20;

    public async Task<IReadOnlyList<AnalyticsSegmentDto>> SegmentsAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default)
    {
        var all = (await segments.AllAsync(cancellationToken)).Take(MaxSegments).ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var firstDay = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1 - days);
        var from = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        List<(Guid ContactId, bool Handed)> conversations;
        List<(Guid ContactId, int Rating)> ratings;
        using (await gate.EnterAsync(cancellationToken))
        {
            conversations = (await db.Conversations.AsNoTracking()
                    .Where(conversation => conversation.UpdatedAt >= from && conversation.CreatedAt >= from
                        && conversation.Channel != ConversationChannel.Portal && conversation.ContactId != null)
                    .Select(conversation => new
                    {
                        ContactId = conversation.ContactId!.Value,
                        Handed = db.Handovers.Any(handover => handover.ConversationId == conversation.Id && handover.Status != HandoverStatus.Cancelled),
                    })
                    .ToListAsync(cancellationToken))
                .Select(row => (row.ContactId, row.Handed))
                .ToList();
            ratings = (await db.Feedback.AsNoTracking()
                    .Where(item => item.CreatedAt >= from && item.Kind == FeedbackKind.Conversation)
                    .Join(
                        db.Conversations.Where(conversation => conversation.Channel != ConversationChannel.Portal && conversation.ContactId != null),
                        item => item.ConversationId,
                        conversation => conversation.Id,
                        (item, conversation) => new { ContactId = conversation.ContactId!.Value, item.Rating })
                    .ToListAsync(cancellationToken))
                .Select(row => (row.ContactId, row.Rating))
                .ToList();
        }

        var active = conversations.Select(row => row.ContactId).Concat(ratings.Select(row => row.ContactId)).ToHashSet();
        var result = new List<AnalyticsSegmentDto>();
        foreach (var segment in all)
        {
            var members = await people.MatchingAmongAsync(active, await segments.ConditionsAsync(segment, cancellationToken), now, cancellationToken);
            var started = conversations.Where(row => members.Contains(row.ContactId)).ToList();
            var rated = ratings.Where(row => members.Contains(row.ContactId)).Select(row => row.Rating).ToList();
            var handed = started.Count(row => row.Handed);
            result.Add(new AnalyticsSegmentDto(
                segment.Id,
                segment.Name,
                segment.Color,
                members.Count,
                started.Count,
                handed,
                started.Count == 0 ? null : Math.Round((double)handed / started.Count, 3),
                rated.Count,
                Average(rated)));
        }

        return result;
    }

    internal const int MaxSignals = 500;
    internal const int MaxQuestions = 50;
    internal const int MaxLabels = 10;
    private const int MaxQuestionLength = 1000;

    private static readonly string[] KnowledgeTools =
        [ToolNames.SearchKnowledgeBase, ToolNames.GrepSources, ToolNames.FindSources, ToolNames.ReadSource, ToolNames.DelegateResearch];

    // Portal conversations are playground tests and count nowhere here.
    public async Task<AnalyticsDto> GetAsync(DateTimeOffset now, int days, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstDay = today.AddDays(1 - days);
        var from = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        using var _ = await gate.EnterAsync(cancellationToken);

        // Bucketed in memory so both stores agree, without date functions on converted columns.
        var conversations = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.UpdatedAt >= from && conversation.CreatedAt >= from && conversation.Channel != ConversationChannel.Portal)
            .Select(conversation => new
            {
                conversation.CreatedAt,
                conversation.WidgetId,
                Handed = db.Handovers.Any(handover => handover.ConversationId == conversation.Id && handover.Status != HandoverStatus.Cancelled),
            })
            .ToListAsync(cancellationToken);
        var feedback = await db.Feedback.AsNoTracking()
            .Where(item => item.CreatedAt >= from && (item.Kind == FeedbackKind.Answer || item.Kind == FeedbackKind.Conversation)
                && !db.Conversations.Any(conversation => conversation.Id == item.ConversationId && conversation.Channel == ConversationChannel.Portal))
            .Select(item => new { item.CreatedAt, item.Kind, item.Rating, item.WidgetId })
            .ToListAsync(cancellationToken);

        var signals = new List<Signal>();
        signals.AddRange(await HandoverSignalsAsync(from, cancellationToken));
        signals.AddRange(await ThumbsDownSignalsAsync(from, cancellationToken));
        var noSources = await NoSourceSignalsAsync(from, cancellationToken);
        signals.AddRange(noSources);

        var startedByDay = conversations.ToLookup(item => Day(item.CreatedAt));
        var ratedByDay = feedback.ToLookup(item => Day(item.CreatedAt));
        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day =>
            {
                var started = startedByDay[day].ToList();
                var rated = ratedByDay[day].ToList();
                var ratings = rated.Where(item => item.Kind == FeedbackKind.Conversation).Select(item => item.Rating).ToList();
                return new AnalyticsDayDto(
                    day,
                    started.Count,
                    started.Count(item => item.Handed),
                    rated.Count(item => item.Kind == FeedbackKind.Answer && item.Rating > 0),
                    rated.Count(item => item.Kind == FeedbackKind.Answer && item.Rating < 0),
                    ratings.Count,
                    Average(ratings));
            })
            .ToList();

        var startedByWidget = conversations.ToLookup(item => item.WidgetId);
        var ratedByWidget = feedback.ToLookup(item => item.WidgetId);
        var widgets = startedByWidget.Select(group => group.Key)
            .Union(ratedByWidget.Select(group => group.Key))
            .Select(widgetId =>
            {
                var started = startedByWidget[widgetId].ToList();
                var rated = ratedByWidget[widgetId].ToList();
                var ratings = rated.Where(item => item.Kind == FeedbackKind.Conversation).Select(item => item.Rating).ToList();
                return new AnalyticsWidgetDto(
                    widgetId,
                    started.Count,
                    started.Count(item => item.Handed),
                    rated.Count(item => item.Kind == FeedbackKind.Answer && item.Rating > 0),
                    rated.Count(item => item.Kind == FeedbackKind.Answer && item.Rating < 0),
                    ratings.Count,
                    Average(ratings));
            })
            .OrderByDescending(item => item.Conversations)
            .ThenBy(item => item.WidgetId is null ? 0 : 1)
            .ToList();

        var allRatings = feedback.Where(item => item.Kind == FeedbackKind.Conversation).Select(item => item.Rating).ToList();
        var handed = conversations.Count(item => item.Handed);
        var totals = new AnalyticsTotalsDto(
            conversations.Count,
            conversations.Count - handed,
            handed,
            allRatings.Count,
            Average(allRatings),
            feedback.Count(item => item.Kind == FeedbackKind.Answer && item.Rating > 0),
            feedback.Count(item => item.Kind == FeedbackKind.Answer && item.Rating < 0),
            noSources.Count);

        return new AnalyticsDto(days, from, now, totals, daily, widgets, Group(signals))
        {
            Labels = await LabelCountsAsync(from, days, cancellationToken),
        };
    }

    private async Task<List<AnalyticsLabelDto>> LabelCountsAsync(DateTimeOffset from, int days, CancellationToken cancellationToken)
    {
        var previousFrom = from.AddDays(-days);
        var rows = await db.ConversationLabels.AsNoTracking()
            .Where(item => !item.Removed)
            .Join(
                db.Conversations.Where(conversation => conversation.UpdatedAt >= previousFrom && conversation.CreatedAt >= previousFrom),
                item => item.ConversationId,
                conversation => conversation.Id,
                (item, conversation) => new { item.LabelId, conversation.CreatedAt })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var labels = await db.Labels.AsNoTracking().ToDictionaryAsync(label => label.Id, cancellationToken);
        return rows
            .Where(row => labels.ContainsKey(row.LabelId))
            .GroupBy(row => row.LabelId)
            .Select(group => new AnalyticsLabelDto(
                group.Key,
                labels[group.Key].Name,
                labels[group.Key].Color,
                group.Count(row => row.CreatedAt >= from),
                group.Count(row => row.CreatedAt < from)))
            .OrderByDescending(item => item.Conversations)
            .ThenByDescending(item => item.PreviousConversations)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxLabels)
            .ToList();
    }

    private async Task<List<Signal>> HandoverSignalsAsync(DateTimeOffset from, CancellationToken cancellationToken)
    {
        var rows = await db.Handovers.AsNoTracking()
            .Where(handover => handover.RequestedAt >= from && handover.Source != HandoverSource.Staff)
            .OrderByDescending(handover => handover.RequestedAt)
            .Take(MaxSignals)
            .Select(handover => new
            {
                handover.ConversationId,
                handover.WidgetId,
                handover.RequestedAt,
                Question = db.ConversationMessages
                    .Where(message => message.ConversationId == handover.ConversationId
                        && (message.Author == MessageAuthor.Visitor || message.Author == MessageAuthor.User)
                        && message.CreatedAt <= handover.RequestedAt)
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Text)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new Signal(UnansweredReasons.Handover, row.Question, row.RequestedAt, row.ConversationId, row.WidgetId, null, null, null))
            .ToList();
    }

    private async Task<List<Signal>> ThumbsDownSignalsAsync(DateTimeOffset from, CancellationToken cancellationToken)
    {
        var rows = await db.Feedback.AsNoTracking()
            .Where(item => item.Kind == FeedbackKind.Answer && item.Rating < 0 && item.CreatedAt >= from && item.MessageId != null
                && !db.Conversations.Any(conversation => conversation.Id == item.ConversationId && conversation.Channel == ConversationChannel.Portal))
            .OrderByDescending(item => item.CreatedAt)
            .Take(MaxSignals)
            .Select(item => new
            {
                item.ConversationId,
                item.WidgetId,
                item.MessageId,
                item.CreatedAt,
                item.Comment,
                Answer = db.ConversationMessages.Where(message => message.Id == item.MessageId).Select(message => message.Text).FirstOrDefault(),
                Question = db.ConversationMessages
                    .Where(message => message.ConversationId == item.ConversationId
                        && (message.Author == MessageAuthor.Visitor || message.Author == MessageAuthor.User)
                        && message.Sequence < db.ConversationMessages.Where(answer => answer.Id == item.MessageId).Select(answer => answer.Sequence).FirstOrDefault())
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Text)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new Signal(UnansweredReasons.ThumbsDown, row.Question, row.CreatedAt, row.ConversationId, row.WidgetId, row.MessageId, row.Answer, row.Comment))
            .ToList();
    }

    private async Task<List<Signal>> NoSourceSignalsAsync(DateTimeOffset from, CancellationToken cancellationToken)
    {
        var rows = await db.ConversationTurns.AsNoTracking()
            .Where(turn => turn.CreatedAt >= from && turn.CitationsJson == "[]" && turn.Error == null && turn.AssistantMessage != string.Empty
                && !db.Conversations.Any(conversation => conversation.Id == turn.ConversationId && conversation.Channel == ConversationChannel.Portal))
            .OrderByDescending(turn => turn.CreatedAt)
            .Take(MaxSignals)
            .Select(turn => new
            {
                turn.ConversationId,
                turn.UserMessage,
                turn.AssistantMessage,
                turn.ToolCallsJson,
                turn.CreatedAt,
                WidgetId = db.Conversations.Where(conversation => conversation.Id == turn.ConversationId).Select(conversation => conversation.WidgetId).FirstOrDefault(),
                MessageId = db.ConversationMessages
                    .Where(message => message.ConversationId == turn.ConversationId && message.TurnId == turn.Id && message.Author == MessageAuthor.Assistant)
                    .Select(message => (Guid?)message.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows
            .Where(row => KnowledgeTools.Any(tool => row.ToolCallsJson.Contains($"\"{tool}\"", StringComparison.Ordinal)))
            .Select(row => new Signal(UnansweredReasons.NoSources, row.UserMessage, row.CreatedAt, row.ConversationId, row.WidgetId, row.MessageId, row.AssistantMessage, null))
            .ToList();
    }

    internal static List<UnansweredQuestionDto> Group(IEnumerable<Signal> signals) =>
        signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Question))
            .GroupBy(signal => Normalize(signal.Question!), StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group.MaxBy(signal => signal.At)!;
                var answered = group.Where(signal => signal.Answer is not null).MaxBy(signal => signal.At);
                var question = string.Join(' ', latest.Question!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                return new UnansweredQuestionDto(
                    question.Length > MaxQuestionLength ? question[..MaxQuestionLength] : question,
                    group.Count(),
                    group.Select(signal => signal.Reason).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
                    latest.At,
                    latest.ConversationId,
                    latest.WidgetId,
                    answered?.MessageId,
                    answered?.Answer,
                    group.Where(signal => !string.IsNullOrWhiteSpace(signal.Comment)).MaxBy(signal => signal.At)?.Comment);
            })
            .OrderByDescending(item => item.Count)
            .ThenByDescending(item => item.LastAskedAt)
            .Take(MaxQuestions)
            .ToList();

    private static string Normalize(string question) =>
        string.Join(' ', question.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('?', '!', '.', ' ');

    private static DateOnly Day(DateTimeOffset value) => DateOnly.FromDateTime(value.UtcDateTime);

    private static double? Average(List<int> ratings) => ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2);

    internal sealed record Signal(
        string Reason,
        string? Question,
        DateTimeOffset At,
        Guid ConversationId,
        Guid? WidgetId,
        Guid? MessageId,
        string? Answer,
        string? Comment);
}
