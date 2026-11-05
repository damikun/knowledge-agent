using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Feedback;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Features.Alerts;

public sealed record AlertCount(int Count, DateTimeOffset? Since)
{
    public static readonly AlertCount None = new(0, null);
}

public sealed record SourceAlertFacts(AlertCount Failed, AlertCount Waiting);

public sealed record DeliveryFailures(AlertCount Slack, AlertCount Email, string? LastSlackError, string? LastEmailError);

public sealed record FeedbackTally(int Ratings, double? AverageRating, int ThumbsUp, int ThumbsDown)
{
    public int Thumbs => ThumbsUp + ThumbsDown;

    public double? ThumbsUpShare => Thumbs == 0 ? null : (double)ThumbsUp / Thumbs;
}

public interface IAlertRepository
{
    Task<SourceAlertFacts> GetSourceFactsAsync(CancellationToken cancellationToken = default);

    Task<AlertCount> CountWaitingForPersonAsync(DateTimeOffset requestedBefore, CancellationToken cancellationToken = default);

    Task<AlertCount> CountMissedRequestsAsync(DateTimeOffset endedAfter, CancellationToken cancellationToken = default);

    Task<DeliveryFailures> CountDeliveryFailuresAsync(DateTimeOffset after, CancellationToken cancellationToken = default);

    Task<FeedbackTally> TallyFeedbackAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlertSnooze>> ListSnoozesAsync(CancellationToken cancellationToken = default);

    Task SaveSnoozeAsync(AlertSnooze snooze, CancellationToken cancellationToken = default);

    Task DeleteSnoozesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlertState>> ListStatesAsync(CancellationToken cancellationToken = default);

    Task SaveStatesAsync(IReadOnlyCollection<AlertState> states, CancellationToken cancellationToken = default);
}

internal sealed class AlertRepository(KnowledgeDbContext db, DbContextGate gate) : IAlertRepository
{
    public async Task<SourceAlertFacts> GetSourceFactsAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        // Counted in memory so both stores agree; the rows are few.
        var rows = await db.Sources.AsNoTracking()
            .Where(source => source.Status == SourceStatus.Failed || source.Status == SourceStatus.Waiting)
            .Select(source => new { source.Status, source.UpdatedAt })
            .ToListAsync(cancellationToken);
        AlertCount Of(SourceStatus status)
        {
            var times = rows.Where(row => row.Status == status).Select(row => row.UpdatedAt).ToList();
            return times.Count == 0 ? AlertCount.None : new AlertCount(times.Count, times.Min());
        }

        return new SourceAlertFacts(Of(SourceStatus.Failed), Of(SourceStatus.Waiting));
    }

    public async Task<AlertCount> CountWaitingForPersonAsync(DateTimeOffset requestedBefore, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var times = await db.Handovers.AsNoTracking()
            .Where(handover => handover.Status == HandoverStatus.Pending && handover.RequestedAt <= requestedBefore)
            .Select(handover => handover.RequestedAt)
            .ToListAsync(cancellationToken);
        return times.Count == 0 ? AlertCount.None : new AlertCount(times.Count, times.Min());
    }

    public async Task<AlertCount> CountMissedRequestsAsync(DateTimeOffset endedAfter, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var times = await db.Handovers.AsNoTracking()
            .Where(handover => handover.Status == HandoverStatus.Expired && handover.ResolvedAt >= endedAfter)
            .Where(handover => !db.ConversationMessages.Any(message => message.ConversationId == handover.ConversationId
                && message.Author == MessageAuthor.Staff
                && message.CreatedAt >= handover.RequestedAt))
            .Select(handover => handover.RequestedAt)
            .ToListAsync(cancellationToken);
        return times.Count == 0 ? AlertCount.None : new AlertCount(times.Count, times.Min());
    }

    public async Task<DeliveryFailures> CountDeliveryFailuresAsync(DateTimeOffset after, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var rows = await db.Handovers.AsNoTracking()
            .Where(handover => handover.LastError != null && handover.UpdatedAt >= after)
            .Select(handover => new
            {
                handover.Channel,
                handover.Status,
                handover.SlackThreadTs,
                handover.EmailedAt,
                handover.EscalatedAt,
                handover.LastError,
                handover.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        // A Slack request without a thread failed in Slack. Slack requests nobody took in time also go by email,
        // so a missing EmailedAt there means email failed.
        var slack = rows.Where(row => row.Channel == WidgetChoices.HandoverSlack && row.SlackThreadTs == null).ToList();
        var email = rows.Where(row => row.EmailedAt == null
                && (row.Channel == WidgetChoices.HandoverEmail || row.EscalatedAt != null || row.Status == HandoverStatus.Expired))
            .Except(slack)
            .ToList();
        AlertCount Count<T>(List<T> items, Func<T, DateTimeOffset> at) => items.Count == 0 ? AlertCount.None : new AlertCount(items.Count, items.Min(at));
        return new DeliveryFailures(
            Count(slack, row => row.UpdatedAt),
            Count(email, row => row.UpdatedAt),
            slack.MaxBy(row => row.UpdatedAt)?.LastError,
            email.MaxBy(row => row.UpdatedAt)?.LastError);
    }

    public async Task<FeedbackTally> TallyFeedbackAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        var rows = await db.Feedback.AsNoTracking()
            .Where(item => item.CreatedAt >= from && item.CreatedAt < to
                && (item.Kind == FeedbackKind.Conversation || item.Kind == FeedbackKind.Answer))
            .Select(item => new { item.Kind, item.Rating })
            .ToListAsync(cancellationToken);
        var ratings = rows.Where(row => row.Kind == FeedbackKind.Conversation).Select(row => row.Rating).ToList();
        return new FeedbackTally(
            ratings.Count,
            ratings.Count == 0 ? null : ratings.Average(),
            rows.Count(row => row.Kind == FeedbackKind.Answer && row.Rating > 0),
            rows.Count(row => row.Kind == FeedbackKind.Answer && row.Rating < 0));
    }

    public async Task<IReadOnlyList<AlertSnooze>> ListSnoozesAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.AlertSnoozes.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SaveSnoozeAsync(AlertSnooze snooze, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snooze);
        using var _ = await gate.EnterAsync(cancellationToken);
        await UpsertAsync(db.AlertSnoozes, [snooze], item => item.Key, cancellationToken);
    }

    public async Task DeleteSnoozesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return;
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        try
        {
            db.AlertSnoozes.RemoveRange(await db.AlertSnoozes.Where(item => keys.Contains(item.Key)).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed meanwhile by another run, which is what was wanted.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<IReadOnlyList<AlertState>> ListStatesAsync(CancellationToken cancellationToken = default)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        return await db.AlertStates.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task SaveStatesAsync(IReadOnlyCollection<AlertState> states, CancellationToken cancellationToken = default)
    {
        if (states.Count == 0)
        {
            return;
        }

        using var _ = await gate.EnterAsync(cancellationToken);
        await UpsertAsync(db.AlertStates, states, item => item.Key, cancellationToken);
    }

    // A concurrent insert of the same key fails the first try; the retry finds the row and updates it.
    private async Task UpsertAsync<T>(DbSet<T> set, IReadOnlyCollection<T> items, Func<T, string> key, CancellationToken cancellationToken)
        where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var keys = items.Select(key).ToList();
                var stored = (await set.ToListAsync(cancellationToken)).Where(item => keys.Contains(key(item))).ToDictionary(key, StringComparer.Ordinal);
                foreach (var item in items)
                {
                    if (stored.TryGetValue(key(item), out var existing))
                    {
                        db.Entry(existing).CurrentValues.SetValues(item);
                    }
                    else
                    {
                        set.Add(item);
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
