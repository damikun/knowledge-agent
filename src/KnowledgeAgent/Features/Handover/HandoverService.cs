using System.Security.Cryptography;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Handover;

public sealed record HandoverDto(
    Guid Id,
    Guid ConversationId,
    string Channel,
    string Status,
    string Source,
    string? Reason,
    string? Summary,
    string? Assignee,
    DateTimeOffset RequestedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? ResolvedAt,
    string? LastError);

public sealed record StaffMessageRequest(string Text);

public sealed record AssignRequest(string? UserId);

public sealed record AssignmentDto(ConversationAssigneeDto? Assignee);

public sealed class HandoverOptions
{
    public const string SectionName = "Handover";

    public int NotifyVisitorDelaySeconds { get; set; } = 60;
}

public sealed class ConversationClosedException(Guid conversationId) : Exception($"Conversation {conversationId} is closed.")
{
    public Guid ConversationId { get; } = conversationId;
}

public sealed class PersonJoinedException(Guid conversationId) : Exception($"Someone from the team already joined conversation {conversationId}.")
{
    public Guid ConversationId { get; } = conversationId;
}

public interface IHandoverService : IHandoverRequester
{
    Task<(HandoverOutcome Outcome, Handover? Handover)> RequestAsync(Guid conversationId, HandoverSource source, string? reason, string? summary, CancellationToken cancellationToken = default);
    Task<Handover> JoinAsync(Guid conversationId, string staffName, StaffRef? assignee, CancellationToken cancellationToken = default);

    Task<Handover> JoinAsync(Guid conversationId, string staffName, CancellationToken cancellationToken = default) =>
        JoinAsync(conversationId, staffName, null, cancellationToken);

    Task<ConversationMessage> AddStaffMessageAsync(Guid conversationId, string staffName, string text, string via, StaffRef? assignee, CancellationToken cancellationToken = default);

    Task<ConversationMessage> AddStaffMessageAsync(Guid conversationId, string staffName, string text, string via, CancellationToken cancellationToken = default) =>
        AddStaffMessageAsync(conversationId, staffName, text, via, null, cancellationToken);

    Task AssignAsync(Guid conversationId, StaffRef? assignee, CancellationToken cancellationToken = default);

    Task<ConversationMessage> AddVisitorMessageAsync(Conversation conversation, string? visitorName, string text, string via, CancellationToken cancellationToken = default);

    Task CancelAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task ReturnToAgentAsync(Guid conversationId, string by, CancellationToken cancellationToken = default);
    Task CloseAsync(Guid conversationId, string by, CancellationToken cancellationToken = default);

    Task<bool> CloseIdleAsync(Guid conversationId, ConversationStatus status, DateTimeOffset idleSince, CancellationToken cancellationToken = default);

    Task<HandoverDto?> GetLatestAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

internal sealed partial class HandoverService(
    IConversationRepository conversations,
    ConversationTimeline timeline,
    IHandoverRepository handovers,
    IWidgetRepository widgets,
    IIntegrationSettings integrations,
    IJobQueue jobs,
    IOptions<HandoverOptions> options,
    TimeProvider timeProvider,
    ILogger<HandoverService> logger) : IHandoverService
{
    public const int MaxStaffMessageLength = 8000;
    public const string ViaPortal = "portal";
    public const string ViaEmail = "email";
    public const string ViaSlack = "slack";
    public const string ViaWidget = "widget";
    public const string BackToAgentNotice = "You're chatting with the assistant again.";
    public const string IdleClosedNotice = "This conversation was closed after a quiet while. Write any time to start a new one.";

    internal const string AlreadyAskedInstruction = "A person was already asked to join. Tell the visitor a colleague will reply in this chat soon.";

    internal const string OfferInstruction =
        "Nobody has been asked yet: the visitor now sees a button to talk to a person from the team. In one short sentence, "
        + "tell them to press it if they want a colleague to take over. Keep helping them meanwhile.";

    // How far back a visitor's request picks up the summary of the agent's offer.
    internal const int OfferLookbackTurns = 3;

    public async Task<HandoverOutcome> OfferAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns: false, cancellationToken);
        if (conversation is { Channel: ConversationChannel.Portal, WidgetId: { } testedWidgetId, VisitorId: null })
        {
            return await SimulateAsync(conversationId, testedWidgetId, cancellationToken);
        }

        var preview = conversation is not null && IsPreview(conversation);
        if (conversation is null || (!preview && conversation is not { Channel: ConversationChannel.Widget, WidgetId: not null }))
        {
            return new HandoverOutcome(false, "A person can only join conversations from a website widget. Say that you cannot connect them here.");
        }

        if (conversation.Status == ConversationStatus.Closed)
        {
            throw new ConversationClosedException(conversationId);
        }

        if (conversation.Status is ConversationStatus.HandoverPending or ConversationStatus.Human
            || (!preview && await handovers.FindOpenAsync(conversationId, cancellationToken) is not null))
        {
            return new HandoverOutcome(true, AlreadyAskedInstruction, Simulated: preview);
        }

        LogOffered(logger, conversationId);
        return new HandoverOutcome(false, OfferInstruction, Simulated: preview, Offered: true);
    }

    public async Task<(HandoverOutcome Outcome, Handover? Handover)> RequestAsync(
        Guid conversationId,
        HandoverSource source,
        string? reason,
        string? summary,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns: false, cancellationToken);
        if (conversation is { Channel: ConversationChannel.Portal, WidgetId: { } previewedWidgetId, VisitorId: not null })
        {
            return (await PreviewAsync(conversation, previewedWidgetId, cancellationToken), null);
        }

        if (conversation is { Channel: ConversationChannel.Portal, WidgetId: { } testedWidgetId })
        {
            return (await SimulateAsync(conversationId, testedWidgetId, cancellationToken), null);
        }

        if (conversation is not { Channel: ConversationChannel.Widget, WidgetId: { } widgetId })
        {
            return (new HandoverOutcome(false, "A person can only join conversations from a website widget. Say that you cannot connect them here."), null);
        }

        if (conversation.Status == ConversationStatus.Closed)
        {
            throw new ConversationClosedException(conversationId);
        }

        if (await handovers.FindOpenAsync(conversationId, cancellationToken) is { } open)
        {
            return (new HandoverOutcome(true, AlreadyAskedInstruction), open);
        }

        if (source == HandoverSource.Visitor && summary is null)
        {
            summary = await OfferedSummaryAsync(conversationId, cancellationToken);
        }

        var widget = await widgets.GetAsync(widgetId, cancellationToken);
        var visitor = conversation.VisitorId is { } visitorId ? await widgets.GetVisitorAsync(visitorId, cancellationToken) : null;
        var policy = widget?.Settings.Handover ?? new WidgetHandover();
        var email = await integrations.GetEmailAsync(cancellationToken);
        var slack = await integrations.GetSlackAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var isOpen = WidgetSchedule.IsOpen(policy.Availability, now);
        var channel = ChooseChannel(policy, email, slack, isOpen);

        var handover = await handovers.AddAsync(
            new Handover
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                WidgetId = widgetId,
                Channel = channel,
                Status = HandoverStatus.Pending,
                Source = source,
                Reason = Clip(reason, 500),
                Summary = Clip(summary, 2000),
                Token = NewToken(),
                RequestedAt = now,
                UpdatedAt = now,
            },
            cancellationToken);
        await timeline.SetStatusAsync(conversationId, ConversationStatus.HandoverPending, cancellationToken);
        var expectation = Expectation(channel, isOpen, visitor?.Email);
        await AddSystemMessageAsync(conversationId, expectation, cancellationToken);

        await jobs.EnqueueAsync(HandoverJobs.Dispatch, handover.Id.ToString(), cancellationToken: cancellationToken);
        if (channel == WidgetChoices.HandoverSlack && email.CanSend)
        {
            await jobs.EnqueueAsync(HandoverJobs.Escalate, handover.Id.ToString(), now.AddMinutes(policy.EscalateAfterMinutes), cancellationToken);
        }

        await jobs.EnqueueAsync(HandoverJobs.Expire, handover.Id.ToString(), now.AddMinutes(policy.WaitMinutes), cancellationToken);
        LogRequested(logger, handover.Id, conversationId, channel, source);
        var instruction = $"A person has been asked to join. The visitor was told: \"{expectation}\" Confirm that in one short sentence and do not answer further questions yourself.";
        return (new HandoverOutcome(true, instruction), handover);
    }

    public async Task<Handover> JoinAsync(Guid conversationId, string staffName, StaffRef? assignee, CancellationToken cancellationToken = default)
    {
        var conversation = await RequireWidgetConversationAsync(conversationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var handover = await handovers.FindOpenAsync(conversationId, cancellationToken);
        if (handover is { Status: HandoverStatus.Active })
        {
            return handover;
        }

        if (handover is null)
        {
            handover = await handovers.AddAsync(
                new Handover
                {
                    Id = Guid.CreateVersion7(),
                    ConversationId = conversationId,
                    WidgetId = conversation.WidgetId,
                    Channel = WidgetChoices.HandoverPortal,
                    Status = HandoverStatus.Pending,
                    Source = HandoverSource.Staff,
                    Token = NewToken(),
                    RequestedAt = now,
                    UpdatedAt = now,
                },
                cancellationToken);
        }

        handover.Status = HandoverStatus.Active;
        handover.Assignee = Clip(staffName, 200);
        handover.AssigneeId = assignee?.Id;
        handover.AcceptedAt = now;
        handover.UpdatedAt = now;
        await handovers.SaveStatusAsync(handover, cancellationToken);

        var owner = assignee ?? new StaffRef(null, staffName);
        await handovers.SetAssigneeAsync(conversationId, owner.Id, Clip(owner.Name, 200), now, cancellationToken);
        await timeline.SetStatusAsync(conversationId, ConversationStatus.Human, cancellationToken);
        await AddSystemMessageAsync(conversationId, $"{staffName} joined the conversation.", cancellationToken);
        await jobs.EnqueueAsync(HandoverJobs.UpdateSlack, handover.Id.ToString(), cancellationToken: cancellationToken);
        LogJoined(logger, handover.Id, conversationId);
        return handover;
    }

    public async Task<ConversationMessage> AddStaffMessageAsync(
        Guid conversationId,
        string staffName,
        string text,
        string via,
        StaffRef? assignee,
        CancellationToken cancellationToken = default)
    {
        var body = text?.Trim() ?? string.Empty;
        if (body.Length is 0 or > MaxStaffMessageLength)
        {
            throw new ValidationFailedException("text", $"Write a reply of 1 to {MaxStaffMessageLength} characters.");
        }

        var conversation = await RequireWidgetConversationAsync(conversationId, cancellationToken);
        if (conversation.Status != ConversationStatus.Human)
        {
            await JoinAsync(conversationId, staffName, assignee, cancellationToken);
        }

        var message = await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                Author = MessageAuthor.Staff,
                AuthorName = Clip(staffName, 200),
                Text = body,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);

        await jobs.EnqueueAsync(HandoverJobs.Relay, $"{message.Id}:{via}", cancellationToken: cancellationToken);
        var notifyAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(0, options.Value.NotifyVisitorDelaySeconds));
        await jobs.EnqueueAsync(HandoverJobs.NotifyVisitor, message.Id.ToString(), notifyAt, cancellationToken);
        return message;
    }

    public async Task<ConversationMessage> AddVisitorMessageAsync(Conversation conversation, string? visitorName, string text, string via, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.Status == ConversationStatus.Closed)
        {
            throw new ConversationClosedException(conversation.Id);
        }

        var message = await timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Author = MessageAuthor.Visitor,
                AuthorName = visitorName,
                Text = text.Trim(),
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);

        if (!IsPreview(conversation))
        {
            await jobs.EnqueueAsync(HandoverJobs.Relay, $"{message.Id}:{via}", cancellationToken: cancellationToken);
        }

        return message;
    }

    public async Task AssignAsync(Guid conversationId, StaffRef? assignee, CancellationToken cancellationToken = default)
    {
        var name = Clip(assignee?.Name, 200);
        if (!await handovers.SetAssigneeAsync(conversationId, name is null ? null : assignee!.Id, name, timeProvider.GetUtcNow(), cancellationToken))
        {
            throw new ConversationNotFoundException(conversationId);
        }

        LogAssigned(logger, conversationId, name is not null);
    }

    public async Task CancelAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await RequireWidgetConversationAsync(conversationId, cancellationToken, allowPreview: true);
        if (conversation.Status == ConversationStatus.Closed)
        {
            throw new ConversationClosedException(conversationId);
        }

        if (await handovers.FindOpenAsync(conversationId, cancellationToken) is { } open)
        {
            var now = timeProvider.GetUtcNow();
            open.Status = HandoverStatus.Cancelled;
            open.ResolvedAt = now;
            open.UpdatedAt = now;
            if (await handovers.TryEndPendingAsync(open, cancellationToken))
            {
                await jobs.EnqueueAsync(HandoverJobs.UpdateSlack, open.Id.ToString(), cancellationToken: cancellationToken);
                LogCancelled(logger, open.Id, conversationId);
            }
            else if (await handovers.FindOpenAsync(conversationId, cancellationToken) is { Status: HandoverStatus.Active })
            {
                throw new PersonJoinedException(conversationId);
            }
        }

        if (await timeline.TrySetStatusAsync(conversationId, ConversationStatus.HandoverPending, ConversationStatus.Ai, cancellationToken: cancellationToken))
        {
            await AddSystemMessageAsync(conversationId, BackToAgentNotice, cancellationToken);
        }
    }

    public async Task ReturnToAgentAsync(Guid conversationId, string by, CancellationToken cancellationToken = default)
    {
        await RequireWidgetConversationAsync(conversationId, cancellationToken);
        await ResolveAsync(conversationId, by, cancellationToken);

        await handovers.SetAssigneeAsync(conversationId, null, null, null, cancellationToken);
        await timeline.SetStatusAsync(conversationId, ConversationStatus.Ai, cancellationToken);
        await AddSystemMessageAsync(conversationId, BackToAgentNotice, cancellationToken);
    }

    public async Task CloseAsync(Guid conversationId, string by, CancellationToken cancellationToken = default)
    {
        await RequireWidgetConversationAsync(conversationId, cancellationToken, allowPreview: true);
        await ResolveAsync(conversationId, by, cancellationToken);
        await timeline.SetStatusAsync(conversationId, ConversationStatus.Closed, cancellationToken);
        await AddSystemMessageAsync(conversationId, "This conversation was closed. Start a new one any time.", cancellationToken);
        await QueueLabelingAsync(conversationId, cancellationToken);
    }

    public async Task<bool> CloseIdleAsync(Guid conversationId, ConversationStatus status, DateTimeOffset idleSince, CancellationToken cancellationToken = default)
    {
        // Checked and closed in one step, so a message that arrives meanwhile keeps the conversation open.
        if (!await timeline.TrySetStatusAsync(conversationId, status, ConversationStatus.Closed, idleSince, cancellationToken))
        {
            return false;
        }

        await ResolveAsync(conversationId, by: null, cancellationToken);
        await AddSystemMessageAsync(conversationId, IdleClosedNotice, cancellationToken);
        await QueueLabelingAsync(conversationId, cancellationToken);
        return true;
    }

    private async Task QueueLabelingAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        try
        {
            await jobs.EnqueueAsync(AutoLabelJob.Kind, conversationId.ToString(), cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLabelingNotQueued(logger, ex, conversationId);
        }
    }

    public async Task<HandoverDto?> GetLatestAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        (await handovers.FindLatestAsync(conversationId, cancellationToken))?.ToDto();

    private async Task<HandoverOutcome> PreviewAsync(Conversation conversation, Guid widgetId, CancellationToken cancellationToken)
    {
        if (conversation.Status == ConversationStatus.Closed)
        {
            throw new ConversationClosedException(conversation.Id);
        }

        if (conversation.Status is ConversationStatus.HandoverPending or ConversationStatus.Human)
        {
            return new HandoverOutcome(true, "A person was already asked to join. Tell the visitor a colleague will reply in this chat soon.", Simulated: true);
        }

        var widget = await widgets.GetAsync(widgetId, cancellationToken);
        var visitor = conversation.VisitorId is { } visitorId ? await widgets.GetVisitorAsync(visitorId, cancellationToken) : null;
        var policy = widget?.Settings.Handover ?? new WidgetHandover();
        var email = await integrations.GetEmailAsync(cancellationToken);
        var slack = await integrations.GetSlackAsync(cancellationToken);
        var isOpen = WidgetSchedule.IsOpen(policy.Availability, timeProvider.GetUtcNow());
        var channel = ChooseChannel(policy, email, slack, isOpen);
        var expectation = Expectation(channel, isOpen, visitor?.Email);
        await timeline.SetStatusAsync(conversation.Id, ConversationStatus.HandoverPending, cancellationToken);
        await AddSystemMessageAsync(conversation.Id, expectation, cancellationToken);
        await AddSystemMessageAsync(conversation.Id, PreviewNotice(channel), cancellationToken);
        var instruction = $"A person has been asked to join. The visitor was told: \"{expectation}\" Confirm that in one short sentence and do not answer further questions yourself.";
        return new HandoverOutcome(true, instruction, Simulated: true);
    }

    // The summary the agent wrote when it offered a person, so the team gets context with the visitor's request.
    private async Task<string?> OfferedSummaryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns: true, cancellationToken);
        foreach (var turn in conversation?.Turns.OrderByDescending(turn => turn.Sequence).Take(OfferLookbackTurns) ?? [])
        {
            var offer = ChatJson.DeserializeDtos<ToolCallDto>(turn.ToolCallsJson)
                .LastOrDefault(call => call is { ToolName: Chat.Tools.ToolNames.RequestHumanHandover, ParentCallId: null, IsError: false });
            if (offer?.Arguments is { ValueKind: System.Text.Json.JsonValueKind.Object } arguments
                && arguments.TryGetProperty("summary", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return text.GetString();
            }
        }

        return null;
    }

    internal static string PreviewNotice(string channel) => $"Preview: nobody is asked. {TestNotice(channel)}.";

    private static bool IsPreview(Conversation conversation) =>
        conversation is { Channel: ConversationChannel.Portal, WidgetId: not null, VisitorId: not null };

    private async Task<HandoverOutcome> SimulateAsync(Guid conversationId, Guid widgetId, CancellationToken cancellationToken)
    {
        var widget = await widgets.GetAsync(widgetId, cancellationToken);
        var policy = widget?.Settings.Handover ?? new WidgetHandover();
        var email = await integrations.GetEmailAsync(cancellationToken);
        var slack = await integrations.GetSlackAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var isOpen = WidgetSchedule.IsOpen(policy.Availability, now);
        var channel = ChooseChannel(policy, email, slack, isOpen);
        await AddSystemMessageAsync(conversationId, TestNotice(channel), cancellationToken);
        var instruction = "This is a test chat in the portal, so nobody was asked. In a live chat the team would be asked to join "
            + $"and the visitor would be told: \"{Expectation(channel, isOpen, visitorEmail: null)}\" Say that in one short sentence.";
        return new HandoverOutcome(true, instruction, Simulated: true);
    }

    internal static string TestNotice(string channel) => channel switch
    {
        WidgetChoices.HandoverSlack => "In a live chat, this would ask your team to join (Inbox and Slack)",
        WidgetChoices.HandoverEmail => "In a live chat, this would ask your team to join (Inbox and email)",
        _ => "In a live chat, this would ask your team to join (Inbox)",
    };

    internal static string ChooseChannel(WidgetHandover policy, EmailSettings email, SlackSettings slack, bool isOpen)
    {
        var channel = policy.Channel;
        var slackChannel = policy.SlackChannelId ?? slack.ChannelId;
        if (channel == WidgetChoices.HandoverSlack && (!slack.CanPost || string.IsNullOrWhiteSpace(slackChannel)))
        {
            channel = WidgetChoices.HandoverEmail;
        }

        if (channel == WidgetChoices.HandoverSlack && !isOpen && email.CanSend)
        {
            channel = WidgetChoices.HandoverEmail;
        }

        if (channel == WidgetChoices.HandoverEmail && (!email.CanSend || (policy.EmailTo.Count == 0 && string.IsNullOrWhiteSpace(email.SupportAddress))))
        {
            channel = WidgetChoices.HandoverPortal;
        }

        return channel;
    }

    internal static string Expectation(string channel, bool isOpen, string? visitorEmail)
    {
        if (channel == WidgetChoices.HandoverEmail)
        {
            return visitorEmail is null
                ? "I've passed your question to the team by email. They'll reply here; leave your email address so they can also reach you if you close this page."
                : $"I've passed your question to the team by email. They'll reply here and to {visitorEmail}.";
        }

        return isOpen
            ? "I've asked a colleague to join. They'll reply here as soon as they can."
            : "Our team is away right now. A colleague will reply here when they're back.";
    }

    private async Task<Conversation> RequireWidgetConversationAsync(Guid conversationId, CancellationToken cancellationToken, bool allowPreview = false)
    {
        var conversation = await conversations.GetAsync(conversationId, includeTurns: false, cancellationToken)
            ?? throw new ConversationNotFoundException(conversationId);
        if (conversation.Channel != ConversationChannel.Widget && !(allowPreview && IsPreview(conversation)))
        {
            throw new ValidationFailedException("conversation", "Only conversations from a website widget can be handed to a person.");
        }

        return conversation;
    }

    private async Task ResolveAsync(Guid conversationId, string? by, CancellationToken cancellationToken)
    {
        if (await handovers.FindOpenAsync(conversationId, cancellationToken) is not { } handover)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        handover.Status = HandoverStatus.Resolved;
        handover.ResolvedAt = now;
        handover.Assignee ??= Clip(by, 200);
        handover.UpdatedAt = now;
        await handovers.SaveStatusAsync(handover, cancellationToken);
        await jobs.EnqueueAsync(HandoverJobs.UpdateSlack, handover.Id.ToString(), cancellationToken: cancellationToken);
    }

    private Task<ConversationMessage> AddSystemMessageAsync(Guid conversationId, string text, CancellationToken cancellationToken) =>
        timeline.AddAsync(
            new ConversationMessage
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                Author = MessageAuthor.System,
                Text = text,
                CreatedAt = timeProvider.GetUtcNow(),
            },
            cancellationToken);

    private static string NewToken() => RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", Handover.TokenLength);

    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handover {HandoverId} requested for conversation {ConversationId} by {Source}, via {Channel}")]
    private static partial void LogRequested(ILogger logger, Guid handoverId, Guid conversationId, string channel, HandoverSource source);

    [LoggerMessage(Level = LogLevel.Information, Message = "The agent offered a person in conversation {ConversationId}")]
    private static partial void LogOffered(ILogger logger, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handover {HandoverId} of conversation {ConversationId} was taken")]
    private static partial void LogJoined(ILogger logger, Guid handoverId, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation {ConversationId} was assigned: {Assigned}")]
    private static partial void LogAssigned(ILogger logger, Guid conversationId, bool assigned);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handover {HandoverId} of conversation {ConversationId} was cancelled by the visitor")]
    private static partial void LogCancelled(ILogger logger, Guid handoverId, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not queue the labeling of closed conversation {ConversationId}")]
    private static partial void LogLabelingNotQueued(ILogger logger, Exception exception, Guid conversationId);
}

public sealed class ConversationNotFoundException(Guid conversationId) : Exception($"Conversation {conversationId} was not found.")
{
    public Guid ConversationId { get; } = conversationId;
}

internal static class HandoverMappings
{
    public static HandoverDto ToDto(this Handover handover) => new(
        handover.Id,
        handover.ConversationId,
        handover.Channel,
        handover.Status.ToString().ToLowerInvariant(),
        handover.Source.ToString().ToLowerInvariant(),
        handover.Reason,
        handover.Summary,
        handover.Assignee,
        handover.RequestedAt,
        handover.AcceptedAt,
        handover.ResolvedAt,
        handover.LastError);
}
