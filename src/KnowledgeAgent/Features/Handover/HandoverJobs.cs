using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Handover;

internal static class HandoverJobs
{
    public const string Dispatch = "handover.dispatch";
    public const string Escalate = "handover.escalate";
    public const string Expire = "handover.expire";
    public const string Relay = "handover.relay";
    public const string NotifyVisitor = "handover.notify-visitor";
    public const string UpdateSlack = "handover.update-slack";
    public const string PollMail = "handover.mail-poll";
}

internal sealed class HandoverContextLoader(
    IHandoverRepository handovers,
    IConversationRepository conversations,
    IWidgetRepository widgets,
    IIntegrationSettings integrations,
    IPublicAddress publicAddress)
{
    public async Task<HandoverContext?> LoadAsync(Guid handoverId, CancellationToken cancellationToken)
    {
        if (await handovers.GetAsync(handoverId, cancellationToken) is not { } handover
            || await conversations.GetAsync(handover.ConversationId, includeTurns: true, cancellationToken) is not { } conversation)
        {
            return null;
        }

        var widget = handover.WidgetId is { } widgetId ? await widgets.GetAsync(widgetId, cancellationToken) : null;
        var visitor = conversation.VisitorId is { } visitorId ? await widgets.GetVisitorAsync(visitorId, cancellationToken) : null;
        return new HandoverContext(
            handover,
            conversation,
            widget,
            visitor,
            await integrations.GetEmailAsync(cancellationToken),
            await integrations.GetSlackAsync(cancellationToken),
            (await publicAddress.GetAsync(cancellationToken)).Url);
    }
}

internal sealed record HandoverContext(
    Handover Handover,
    Conversation Conversation,
    Widget? Widget,
    WidgetVisitor? Visitor,
    EmailSettings Email,
    SlackSettings Slack,
    string? PublicUrl)
{
    public string WidgetName => Widget?.Name ?? "Website chat";

    public IReadOnlyList<string> EmailRecipients =>
        Widget?.Settings.Handover.EmailTo is { Count: > 0 } to ? to : Email.SupportAddress is { } support ? [support] : [];

    public string? SlackChannel => Widget?.Settings.Handover.SlackChannelId ?? Slack.ChannelId;

    public string? InboxLink => PublicUrl is { } url ? $"{url.TrimEnd('/')}/inbox/{Conversation.Id}" : null;
}

internal sealed partial class HandoverDispatchJob(
    HandoverContextLoader loader,
    IHandoverRepository handovers,
    IEmailTransport email,
    ISlackGateway slack,
    ConversationTimeline timeline,
    TimeProvider timeProvider,
    ILogger<HandoverDispatchJob> logger) : IJobHandler, IJobFailureHandler
{
    public const string NotDeliveredNotice = "We could not reach the team just now. Your request stays open, and someone will reply here when they see it. Meanwhile, the assistant can keep helping.";

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var id) || await loader.LoadAsync(id, cancellationToken) is not { } handover)
        {
            return;
        }

        try
        {
            switch (handover.Handover.Channel)
            {
                case WidgetChoices.HandoverEmail:
                    await SendEmailAsync(handover, email, handovers, timeProvider, cancellationToken);
                    break;
                case WidgetChoices.HandoverSlack:
                    await PostToSlackAsync(handover, cancellationToken);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = handover.Handover;
            failed.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            failed.UpdatedAt = timeProvider.GetUtcNow();
            await handovers.SaveDeliveryAsync(failed, cancellationToken);
            LogFailed(logger, ex, failed.Id, failed.Channel, context.Attempt, context.MaxAttempts);
            throw;
        }
    }

    public async Task OnFailureAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(failure.Key, out var id) || await loader.LoadAsync(id, cancellationToken) is not { } context)
        {
            return;
        }

        var handover = context.Handover;
        handover.LastError ??= failure.Error.Length > 1000 ? failure.Error[..1000] : failure.Error;
        handover.UpdatedAt = timeProvider.GetUtcNow();
        await handovers.SaveDeliveryAsync(handover, cancellationToken);
        LogGaveUp(logger, handover.Id, handover.Channel, failure.Attempts, failure.Error);

        if (handover.Status != HandoverStatus.Pending || Reached(context) || EscalationPending(context))
        {
            return;
        }

        await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = handover.ConversationId,
                Author = MessageAuthor.System,
                Text = NotDeliveredNotice,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }

    private static bool Reached(HandoverContext context) =>
        context.Handover.SlackThreadTs is not null || context.Handover.EmailedAt is not null || context.Handover.Channel == WidgetChoices.HandoverPortal;

    private static bool EscalationPending(HandoverContext context) =>
        context.Handover is { Channel: WidgetChoices.HandoverSlack, EscalatedAt: null } && context.Email.CanSend && context.EmailRecipients.Count > 0;

    internal static async Task SendEmailAsync(HandoverContext context, IEmailTransport email, IHandoverRepository handovers, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (context.EmailRecipients.Count == 0 || !context.Email.CanSend)
        {
            return;
        }

        var handover = context.Handover;
        var message = HandoverEmails.Transcript(
            context.Email,
            handover,
            context.Conversation,
            context.Conversation.Messages.OrderBy(item => item.Sequence).ToList(),
            context.Visitor,
            context.WidgetName,
            context.EmailRecipients,
            context.InboxLink);
        await email.SendAsync(message, cancellationToken);
        handover.EmailMessageId ??= message.MessageId;
        handover.EmailedAt = timeProvider.GetUtcNow();
        handover.LastError = null;
        handover.UpdatedAt = timeProvider.GetUtcNow();
        await handovers.SaveDeliveryAsync(handover, cancellationToken);
    }

    private async Task PostToSlackAsync(HandoverContext context, CancellationToken cancellationToken)
    {
        var handover = context.Handover;
        if (handover.SlackThreadTs is not null || context.SlackChannel is not { } channel)
        {
            return;
        }

        var (text, blocks) = SlackMessages.Request(context);
        var ts = await slack.PostMessageAsync(channel, text, blocks, cancellationToken: cancellationToken);
        handover.SlackChannelId = channel;
        handover.SlackThreadTs = ts;
        handover.LastError = null;
        handover.UpdatedAt = timeProvider.GetUtcNow();
        await handovers.SaveDeliveryAsync(handover, cancellationToken);
        await slack.PostMessageAsync(channel, SlackMessages.Transcript(context), threadTs: ts, cancellationToken: cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handover {HandoverId} could not be sent by {Channel} (attempt {Attempt} of {MaxAttempts})")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid handoverId, string channel, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handover {HandoverId} was not sent by {Channel} after {Attempts} attempts: {Error}")]
    private static partial void LogGaveUp(ILogger logger, Guid handoverId, string channel, int attempts, string error);
}

internal sealed partial class HandoverEscalateJob(
    HandoverContextLoader loader,
    IHandoverRepository handovers,
    IEmailTransport email,
    ConversationTimeline timeline,
    TimeProvider timeProvider,
    ILogger<HandoverEscalateJob> logger) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var id)
            || await loader.LoadAsync(id, cancellationToken) is not { Handover.Status: HandoverStatus.Pending, Handover.EscalatedAt: null } handover
            || !handover.Email.CanSend
            || handover.EmailRecipients.Count == 0)
        {
            return;
        }

        await HandoverDispatchJob.SendEmailAsync(handover, email, handovers, timeProvider, cancellationToken);
        var escalated = handover.Handover;
        escalated.EscalatedAt = timeProvider.GetUtcNow();
        await handovers.SaveDeliveryAsync(escalated, cancellationToken);
        var note = handover.Visitor?.Email is { } address
            ? $"Everyone is busy right now, so the team was also emailed. They'll reply here and to {address}."
            : "Everyone is busy right now, so the team was also emailed. Leave your email address so they can reach you if you close this page.";
        await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = escalated.ConversationId,
                Author = MessageAuthor.System,
                Text = note,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);
        LogEscalated(logger, escalated.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handover {HandoverId} had no reply on Slack and was emailed")]
    private static partial void LogEscalated(ILogger logger, Guid handoverId);
}

internal sealed partial class HandoverExpireJob(
    HandoverContextLoader loader,
    IHandoverRepository handovers,
    IEmailTransport email,
    ConversationTimeline timeline,
    IJobQueue jobs,
    TimeProvider timeProvider,
    ILogger<HandoverExpireJob> logger) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var id) || await loader.LoadAsync(id, cancellationToken) is not { Handover.Status: HandoverStatus.Pending } expiring)
        {
            return;
        }

        var handover = expiring.Handover;
        var now = timeProvider.GetUtcNow();
        handover.Status = HandoverStatus.Expired;
        handover.ResolvedAt = now;
        handover.UpdatedAt = now;
        if (!await handovers.TryEndPendingAsync(handover, cancellationToken))
        {
            return;
        }

        if (handover.EmailedAt is null)
        {
            try
            {
                await HandoverDispatchJob.SendEmailAsync(expiring, email, handovers, timeProvider, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The request has ended, so a retry would not send it either; the visitor hears that nobody was emailed.
                handover.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                await handovers.SaveDeliveryAsync(handover, cancellationToken);
                LogEmailFailed(logger, ex, handover.Id);
            }
        }

        var emailed = handover.EmailedAt is not null;
        if (await timeline.TrySetStatusAsync(handover.ConversationId, ConversationStatus.HandoverPending, ConversationStatus.Ai, cancellationToken: cancellationToken))
        {
            await timeline.AddAsync(
                new ConversationMessage
                {
                    Id = Guid.CreateVersion7(),
                    ConversationId = handover.ConversationId,
                    Author = MessageAuthor.System,
                    Text = Notice(emailed, expiring.Visitor?.Email),
                    CreatedAt = timeProvider.GetUtcNow(),
                },
                cancellationToken);
        }

        await jobs.EnqueueAsync(HandoverJobs.UpdateSlack, handover.Id.ToString(), cancellationToken: cancellationToken);
        LogExpired(logger, handover.Id, emailed);
    }

    internal static string Notice(bool emailed, string? visitorEmail) => (emailed, visitorEmail) switch
    {
        (true, { } address) => $"Nobody from the team is free right now. They'll reply to {address}. Meanwhile, the assistant can keep helping.",
        (true, null) => "Nobody from the team is free right now. Leave your email address so they can reach you. Meanwhile, the assistant can keep helping.",
        _ => "Nobody from the team is free right now. Please try again later. Meanwhile, the assistant can keep helping.",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Nobody joined handover {HandoverId} in time; emailed to the team: {Emailed}")]
    private static partial void LogExpired(ILogger logger, Guid handoverId, bool emailed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The transcript of expired handover {HandoverId} could not be emailed")]
    private static partial void LogEmailFailed(ILogger logger, Exception exception, Guid handoverId);
}

// Key: "<message id>:<where it was written>".
internal sealed class HandoverRelayJob(
    IConversationRepository conversations,
    IHandoverRepository handovers,
    HandoverContextLoader loader,
    IEmailTransport email,
    ISlackGateway slack) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var separator = context.Key.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || !Guid.TryParse(context.Key[..separator], out var messageId))
        {
            return;
        }

        var via = context.Key[(separator + 1)..];
        var message = await FindMessageAsync(messageId, cancellationToken);
        if (message is null || await handovers.FindLatestAsync(message.ConversationId, cancellationToken) is not { } latest
            || await loader.LoadAsync(latest.Id, cancellationToken) is not { } handover)
        {
            return;
        }

        if (handover.Handover.Channel == WidgetChoices.HandoverSlack && via != HandoverService.ViaSlack
            && handover.Handover.SlackChannelId is { } channel && handover.Handover.SlackThreadTs is { } thread)
        {
            await slack.PostMessageAsync(channel, SlackMessages.Relay(message, via), threadTs: thread, cancellationToken: cancellationToken);
        }
        else if (handover.Handover.Channel == WidgetChoices.HandoverEmail && via != HandoverService.ViaEmail
                 && handover.Email.CanSend && handover.EmailRecipients.Count > 0)
        {
            await email.SendAsync(HandoverEmails.FollowUp(handover.Email, handover.Handover, handover.Conversation, message, handover.EmailRecipients), cancellationToken);
        }

        async Task<ConversationMessage?> FindMessageAsync(Guid id, CancellationToken token)
        {
            var conversationId = await conversations.FindConversationOfMessageAsync(id, token);
            return conversationId is { } found
                ? (await conversations.ListMessagesAsync(found, cancellationToken: token)).FirstOrDefault(item => item.Id == id)
                : null;
        }
    }
}

internal sealed partial class VisitorNotifyJob(
    IConversationRepository conversations,
    IHandoverRepository handovers,
    HandoverContextLoader loader,
    IEmailTransport email,
    VisitorPresence presence,
    ILogger<VisitorNotifyJob> logger) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.Key, out var messageId)
            || await conversations.FindConversationOfMessageAsync(messageId, cancellationToken) is not { } conversationId
            || presence.IsPresent(conversationId))
        {
            return;
        }

        var message = (await conversations.ListMessagesAsync(conversationId, cancellationToken: cancellationToken)).FirstOrDefault(item => item.Id == messageId);
        if (message is not { Author: MessageAuthor.Staff }
            || await handovers.FindLatestAsync(conversationId, cancellationToken) is not { } latest
            || await loader.LoadAsync(latest.Id, cancellationToken) is not { Visitor.Email: { } address } handover
            || !handover.Email.CanSend
            || handover.Visitor!.LastSeenAt > message.CreatedAt)
        {
            return;
        }

        var title = handover.Widget?.Settings.Appearance.Title ?? handover.WidgetName;
        await email.SendAsync(HandoverEmails.ToVisitor(handover.Email, handover.Handover, title, message, address), cancellationToken);
        LogNotified(logger, messageId, conversationId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Emailed staff reply {MessageId} of conversation {ConversationId} to the visitor")]
    private static partial void LogNotified(ILogger logger, Guid messageId, Guid conversationId);
}

internal sealed class SlackUpdateJob(HandoverContextLoader loader, ISlackGateway slack) : IJobHandler
{
    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(context.Key, out var id)
            && await loader.LoadAsync(id, cancellationToken) is { Handover: { SlackChannelId: { } channel, SlackThreadTs: { } ts } } handover
            && handover.Slack.CanPost)
        {
            var (text, blocks) = SlackMessages.Request(handover);
            await slack.UpdateMessageAsync(channel, ts, text, blocks, cancellationToken);
        }
    }
}

internal sealed partial class MailPollJob(
    IIntegrationSettings integrations,
    IEmailTransport email,
    IHandoverRepository handovers,
    IConversationRepository conversations,
    IWidgetRepository widgets,
    IHandoverService handover,
    TimeProvider timeProvider,
    ILogger<MailPollJob> logger) : IJobHandler
{
    public const int MaxMessagesPerRun = 50;

    public async Task HandleAsync(JobContext context, CancellationToken cancellationToken)
    {
        var settings = await integrations.GetEmailAsync(cancellationToken);
        if (!settings.CanReceive)
        {
            return;
        }

        var handled = await email.ProcessInboxAsync(
            envelope => HandoverEmails.FindToken(envelope) is not null
                        && !string.Equals(envelope.FromAddress, settings.FromAddress, StringComparison.OrdinalIgnoreCase),
            (inbound, token) => HandleAsync(settings, inbound, token),
            MaxMessagesPerRun,
            cancellationToken);
        if (handled > 0)
        {
            LogPolled(logger, handled);
        }
    }

    private async Task<bool> HandleAsync(EmailSettings settings, InboundEmail inbound, CancellationToken cancellationToken)
    {
        var envelope = inbound.Envelope;
        var token = HandoverEmails.FindToken(envelope)!;
        if (await handovers.FindByTokenAsync(token, cancellationToken) is not { } found
            || await conversations.GetAsync(found.ConversationId, includeTurns: false, cancellationToken) is not { } conversation)
        {
            LogUnknownToken(logger, token);
            return true;
        }

        var dedupeKey = InboundDedupeKey(inbound);
        if (!await handovers.TryMarkInboundAsync(dedupeKey, timeProvider.GetUtcNow(), cancellationToken))
        {
            return true;
        }

        var text = HandoverEmails.ReplyText(inbound.Text);
        if (text.Length == 0)
        {
            return true;
        }

        var visitor = conversation.VisitorId is { } visitorId ? await widgets.GetVisitorAsync(visitorId, cancellationToken) : null;
        var widget = found.WidgetId is { } widgetId ? await widgets.GetAsync(widgetId, cancellationToken) : null;
        if (visitor?.Email is { } visitorEmail && string.Equals(envelope.FromAddress, visitorEmail, StringComparison.OrdinalIgnoreCase))
        {
            if (conversation.Status != ConversationStatus.Closed)
            {
                await handover.AddVisitorMessageAsync(conversation, visitor.Name, text, HandoverService.ViaEmail, cancellationToken);
            }

            return true;
        }

        if (!IsStaff(envelope.FromAddress, settings, widget))
        {
            LogNotStaff(logger, envelope.FromAddress ?? "unknown", conversation.Id);
            return true;
        }

        await handover.AddStaffMessageAsync(conversation.Id, inbound.FromName ?? envelope.FromAddress!, text, HandoverService.ViaEmail, cancellationToken);
        return true;
    }

    // Message-ID, or a SHA-256 of sender/subject/date/text: string.GetHashCode is randomized per process.
    internal static string InboundDedupeKey(InboundEmail inbound)
    {
        var envelope = inbound.Envelope;
        if (!string.IsNullOrWhiteSpace(envelope.MessageId))
        {
            return "email:" + envelope.MessageId;
        }

        var normalized = string.Join(
            '\u001f',
            (envelope.FromAddress ?? string.Empty).Trim().ToLowerInvariant(),
            (envelope.Subject ?? string.Empty).Trim(),
            envelope.Date?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            inbound.Text.ReplaceLineEndings("\n").Trim());
        return "email:sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static bool IsStaff(string? from, EmailSettings settings, Widget? widget)
    {
        if (string.IsNullOrWhiteSpace(from) || from.LastIndexOf('@') is var at && at <= 0)
        {
            return false;
        }

        var domain = from[(at + 1)..];
        if (settings.StaffAddresses.Count > 0 || settings.StaffDomains.Count > 0)
        {
            return settings.StaffAddresses.Contains(from, StringComparer.OrdinalIgnoreCase)
                   || settings.StaffDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
        }

        var trusted = (widget?.Settings.Handover.EmailTo ?? []).Append(settings.SupportAddress).OfType<string>()
            .Select(address => address[(address.LastIndexOf('@') + 1)..]);
        return trusted.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Count} handover email(s)")]
    private static partial void LogPolled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "An email for the unknown handover token {Token} was set aside")]
    private static partial void LogUnknownToken(ILogger logger, string token);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An email from {From} to conversation {ConversationId} was ignored: the sender is neither the visitor nor staff")]
    private static partial void LogNotStaff(ILogger logger, string from, Guid conversationId);
}
