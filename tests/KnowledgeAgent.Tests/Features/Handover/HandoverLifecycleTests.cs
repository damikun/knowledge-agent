using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Labels;
using KnowledgeAgent.Features.Staff;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Handover;

public sealed class HandoverLifecycleTests : IAsyncLifetime
{
    private static readonly EmailSettings WorkingEmail = new()
    {
        Enabled = true,
        SmtpHost = "smtp.shop.test",
        FromAddress = "help@shop.test",
        SupportAddress = "team@shop.test",
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingJobQueue _jobs = new();
    private readonly FakeIntegrations _integrations = new();
    private readonly RecordingEmail _email = new();
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"handover-lifecycle-{Guid.NewGuid():N}",
            },
            _time,
            builder =>
            {
                var services = builder.Services;
                services.AddSingleton<IJobQueue>(_jobs);
                services.AddSingleton<IIntegrationSettings>(_integrations);
                services.AddSingleton<IEmailTransport>(_email);
                services.AddSingleton<ConversationEventHub>();
                services.AddSingleton<IConversationNotifier, NullConversationNotifier>();
                services.AddSingleton<ChatTurnHost>();
                services.AddScoped<ConversationTimeline>();
                services.AddScoped<IWidgetRepository, WidgetRepository>();
                services.AddScoped<HandoverContextLoader>();
                services.AddScoped<IPublicAddress, PublicAddressStore>();
                services.AddScoped<IHandoverService, HandoverService>();
                services.AddScoped<HandoverExpireJob>();
                services.AddScoped<CloseIdleConversationsJob>();
                services.AddOptions<HandoverOptions>();
            });
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_visitor_cancels_a_request_only_while_nobody_has_joined()
    {
        var widget = await AddWidgetAsync(handover: new WidgetHandover { WaitMinutes = 20 });
        var conversationId = await StartConversationAsync(widget);

        var requestId = await RequestAsync(conversationId);
        _jobs.Enqueued.ShouldContain((HandoverJobs.Expire, requestId.ToString(), _time.GetUtcNow().AddMinutes(20)));
        await InScopeAsync((IHandoverService handover) => handover.CancelAsync(conversationId, Ct));

        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.Ai);
        (await RequestStatusAsync(requestId)).ShouldBe(HandoverStatus.Cancelled);
        (await LastMessageAsync(conversationId)).Text.ShouldBe(HandoverService.BackToAgentNotice);
        _jobs.Enqueued.ShouldContain((HandoverJobs.UpdateSlack, requestId.ToString(), null), "Slack shows the visitor went back");

        var messages = (await MessagesAsync(conversationId)).Count;
        await InScopeAsync((IHandoverService handover) => handover.CancelAsync(conversationId, Ct));
        (await MessagesAsync(conversationId)).Count.ShouldBe(messages);

        var takenId = await RequestAsync(conversationId);
        await InScopeAsync((IHandoverService handover) => handover.JoinAsync(conversationId, "Dana", Ct));
        await Should.ThrowAsync<PersonJoinedException>(() => InScopeAsync((IHandoverService handover) => handover.CancelAsync(conversationId, Ct)));
        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.Human);
        (await RequestStatusAsync(takenId)).ShouldBe(HandoverStatus.Active);

        await InScopeAsync((IHandoverService handover) => handover.CloseAsync(conversationId, "Dana", Ct));
        _jobs.Enqueued.ShouldContain((AutoLabelJob.Kind, conversationId.ToString(), null));
        await Should.ThrowAsync<ConversationClosedException>(() => InScopeAsync((IHandoverService handover) => handover.CancelAsync(conversationId, Ct)));
    }

    [Fact]
    public async Task A_playground_test_of_a_widget_asks_nobody_and_says_what_a_live_chat_would_do()
    {
        _integrations.Email = WorkingEmail;
        var widget = await AddWidgetAsync(handover: new WidgetHandover { Channel = WidgetChoices.HandoverEmail });
        var now = _time.GetUtcNow();
        var conversation = new Conversation { Channel = ConversationChannel.Portal, WidgetId = widget.Id, Title = "Test", CreatedAt = now, UpdatedAt = now };
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));

        var outcome = await InScopeAsync((IHandoverService handover) =>
            handover.RequestFromAgentAsync(conversation.Id, "asked for a person", "Ana wants to cancel.", Ct));

        (outcome.Started, outcome.Simulated).ShouldBe((true, true));
        outcome.Instruction.ShouldContain("nobody was asked");
        (await InScopeAsync((IHandoverRepository handovers) => handovers.FindLatestAsync(conversation.Id, Ct))).ShouldBeNull("no request is stored");
        _jobs.Enqueued.ShouldBeEmpty("nothing goes to Slack or email");
        _email.Sent.ShouldBeEmpty();
        (await StatusAsync(conversation.Id)).ShouldBe(ConversationStatus.Ai);
        var notice = (await MessagesAsync(conversation.Id)).ShouldHaveSingleItem();
        (notice.Author, notice.Text).ShouldBe((MessageAuthor.System, "In a live chat, this would ask your team to join (Inbox and email)"));
    }

    [Fact]
    public async Task A_request_expires_only_while_nobody_has_joined()
    {
        var widget = await AddWidgetAsync();
        var conversationId = await StartConversationAsync(widget, visitorEmail: "ana@visitor.test");

        var expiringId = await RequestAsync(conversationId);
        _time.Advance(TimeSpan.FromMinutes(15));
        await ExpireAsync(expiringId);

        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.Ai);
        var expired = await LoadRequestAsync(expiringId);
        expired.Status.ShouldBe(HandoverStatus.Expired);
        expired.ResolvedAt.ShouldBe(_time.GetUtcNow());
        expired.EmailedAt.ShouldBeNull();
        (await LastMessageAsync(conversationId)).Text.ShouldBe(HandoverExpireJob.Notice(emailed: false, "ana@visitor.test"));
        _email.Sent.ShouldBeEmpty("email is not set up");
        _jobs.Enqueued.ShouldContain((HandoverJobs.UpdateSlack, expiringId.ToString(), null));

        var cancelledId = await RequestAsync(conversationId);
        await InScopeAsync((IHandoverService handover) => handover.CancelAsync(conversationId, Ct));
        var messages = (await MessagesAsync(conversationId)).Count;
        await ExpireAsync(cancelledId);
        (await RequestStatusAsync(cancelledId)).ShouldBe(HandoverStatus.Cancelled);
        (await MessagesAsync(conversationId)).Count.ShouldBe(messages);

        var takenId = await RequestAsync(conversationId);
        await InScopeAsync((IHandoverService handover) => handover.AddStaffMessageAsync(conversationId, "Dana", "Let me check.", HandoverService.ViaPortal, Ct));
        await ExpireAsync(takenId);
        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.Human);
        (await RequestStatusAsync(takenId)).ShouldBe(HandoverStatus.Active);
        (await LastMessageAsync(conversationId)).Text.ShouldBe("Let me check.");
    }

    [Fact]
    public void The_visitor_hears_whether_and_where_the_team_will_reply()
    {
        HandoverExpireJob.Notice(emailed: true, "ana@visitor.test")
            .ShouldBe("Nobody from the team is free right now. They'll reply to ana@visitor.test. Meanwhile, the assistant can keep helping.");
        HandoverExpireJob.Notice(emailed: true, visitorEmail: null)
            .ShouldBe("Nobody from the team is free right now. Leave your email address so they can reach you. Meanwhile, the assistant can keep helping.");
        HandoverExpireJob.Notice(emailed: false, "ana@visitor.test")
            .ShouldBe("Nobody from the team is free right now. Please try again later. Meanwhile, the assistant can keep helping.");
    }

    [Fact]
    public async Task An_expired_request_emails_the_transcript_unless_it_went_by_email_already()
    {
        _integrations.Email = WorkingEmail;
        var widget = await AddWidgetAsync();
        var anonymous = await StartConversationAsync(widget);
        var requestId = await RequestAsync(anonymous);

        await ExpireAsync(requestId);

        var transcript = _email.Sent.ShouldHaveSingleItem();
        transcript.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("team@shop.test");
        transcript.TextBody.ShouldNotBeNull().ShouldContain("Where is my parcel?");
        (await LoadRequestAsync(requestId)).EmailedAt.ShouldBe(_time.GetUtcNow());
        (await LastMessageAsync(anonymous)).Text.ShouldBe(HandoverExpireJob.Notice(emailed: true, visitorEmail: null));

        var known = await StartConversationAsync(widget, visitorEmail: "ana@visitor.test");
        var emailedId = await RequestAsync(known);
        await InScopeAsync(async (IHandoverRepository handovers) =>
        {
            var emailed = (await handovers.GetAsync(emailedId, Ct)).ShouldNotBeNull();
            emailed.EmailedAt = _time.GetUtcNow();
            await handovers.SaveDeliveryAsync(emailed, Ct);
        });

        await ExpireAsync(emailedId);

        _email.Sent.Count.ShouldBe(1, "the team has the transcript already");
        (await LastMessageAsync(known)).Text.ShouldBe(HandoverExpireJob.Notice(emailed: true, "ana@visitor.test"));
    }

    [Fact]
    public async Task A_request_still_expires_when_its_email_fails()
    {
        _integrations.Email = WorkingEmail;
        _email.Failure = new IOException("The mail server is down.");
        var widget = await AddWidgetAsync();
        var conversationId = await StartConversationAsync(widget, visitorEmail: "ana@visitor.test");
        var requestId = await RequestAsync(conversationId);

        await ExpireAsync(requestId);

        var expired = await LoadRequestAsync(requestId);
        expired.Status.ShouldBe(HandoverStatus.Expired);
        expired.EmailedAt.ShouldBeNull();
        expired.LastError.ShouldBe("The mail server is down.");
        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.Ai);
        (await LastMessageAsync(conversationId)).Text.ShouldBe(HandoverExpireJob.Notice(emailed: false, "ana@visitor.test"));
    }

    [Fact]
    public async Task Quiet_conversations_close_by_mode_but_never_while_the_visitor_waits_for_the_team()
    {
        var widget = await AddWidgetAsync(new WidgetConversations { AiIdleMinutes = 30, HumanIdleHours = 24 });
        var keepsOpen = await AddWidgetAsync(new WidgetConversations { CloseIdle = false, AiIdleMinutes = 5 });

        var answered = await StartConversationAsync(widget);
        var answeredRequest = await RequestAsync(answered);
        await StaffReplyAsync(answered);
        var visitorWaiting = await StartConversationAsync(widget);
        await RequestAsync(visitorWaiting);
        await StaffReplyAsync(visitorWaiting);
        var waiting = (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(visitorWaiting, includeTurns: false, Ct))).ShouldNotBeNull();
        await InScopeAsync((IHandoverService handover) => handover.AddVisitorMessageAsync(waiting, "Ana", "And the invoice?", HandoverService.ViaWidget, Ct));
        var waitingForPerson = await StartConversationAsync(widget);
        await RequestAsync(waitingForPerson);
        var turnedOff = await StartConversationAsync(keepsOpen);

        _time.Advance(TimeSpan.FromHours(2));
        var answeredLately = await StartConversationAsync(widget);
        await RequestAsync(answeredLately);
        await StaffReplyAsync(answeredLately);

        _time.Advance(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(29));
        var quietWithAgent = await StartConversationAsync(widget);
        _time.Advance(TimeSpan.FromMinutes(21));
        var activeWithAgent = await StartConversationAsync(widget);
        _time.Advance(TimeSpan.FromMinutes(10));

        await CloseIdleAsync();

        (await StatusAsync(answered)).ShouldBe(ConversationStatus.Closed);
        (await LastMessageAsync(answered)).Text.ShouldBe(HandoverService.IdleClosedNotice);
        (await RequestStatusAsync(answeredRequest)).ShouldBe(HandoverStatus.Resolved);
        (await StatusAsync(quietWithAgent)).ShouldBe(ConversationStatus.Closed);
        (await LastMessageAsync(quietWithAgent)).Text.ShouldBe(HandoverService.IdleClosedNotice);
        (await StatusAsync(visitorWaiting)).ShouldBe(ConversationStatus.Human, "the visitor wrote last and waits for the team");
        (await StatusAsync(waitingForPerson)).ShouldBe(ConversationStatus.HandoverPending, "a pending request expires instead");
        (await StatusAsync(answeredLately)).ShouldBe(ConversationStatus.Human);
        (await StatusAsync(activeWithAgent)).ShouldBe(ConversationStatus.Ai);
        (await StatusAsync(turnedOff)).ShouldBe(ConversationStatus.Ai);
        _jobs.Enqueued.Where(job => job.Kind == AutoLabelJob.Kind).Select(job => job.Key)
            .ShouldBe([answered.ToString(), quietWithAgent.ToString()], ignoreOrder: true, "closed conversations get their labels");

        var messages = (await MessagesAsync(answered)).Count;
        await CloseIdleAsync();
        (await MessagesAsync(answered)).Count.ShouldBe(messages, "a closed conversation is not closed again");
    }

    [Fact]
    public async Task Taking_a_conversation_over_assigns_it_and_handing_it_back_to_the_agent_unassigns_it()
    {
        var widget = await AddWidgetAsync();
        var conversationId = await StartConversationAsync(widget);
        var requestId = await RequestAsync(conversationId);

        await InScopeAsync((IHandoverService handover) => handover.AssignAsync(conversationId, new StaffRef("kc-dana", "Dana Novak"), Ct));
        (await AssigneeAsync(conversationId)).ShouldBe(("kc-dana", "Dana Novak"));
        (await StatusAsync(conversationId)).ShouldBe(ConversationStatus.HandoverPending, "assigning asks nothing of the visitor");

        await InScopeAsync((IHandoverService handover) => handover.JoinAsync(conversationId, "Petr", new StaffRef("kc-petr", "Petr Svoboda"), Ct));
        (await AssigneeAsync(conversationId)).ShouldBe(("kc-petr", "Petr Svoboda"), "whoever takes it over owns it");
        var request = await LoadRequestAsync(requestId);
        (request.Assignee, request.AssigneeId).ShouldBe(("Petr", "kc-petr"));
        (await LastMessageAsync(conversationId)).Text.ShouldBe("Petr joined the conversation.");

        await InScopeAsync((IHandoverService handover) => handover.AssignAsync(conversationId, null, Ct));
        (await AssigneeAsync(conversationId)).ShouldBe((null, null));

        await InScopeAsync((IHandoverService handover) => handover.AssignAsync(conversationId, new StaffRef("kc-dana", "Dana Novak"), Ct));
        await InScopeAsync((IHandoverService handover) => handover.ReturnToAgentAsync(conversationId, "Dana", Ct));
        (await AssigneeAsync(conversationId)).ShouldBe((null, null), "the agent has it again");

        await Should.ThrowAsync<ConversationNotFoundException>(() =>
            InScopeAsync((IHandoverService handover) => handover.AssignAsync(Guid.NewGuid(), new StaffRef("kc-dana", "Dana Novak"), Ct)));
    }

    [Fact]
    public async Task A_reply_from_Slack_or_email_assigns_the_conversation_to_the_name_without_an_id()
    {
        var widget = await AddWidgetAsync();
        var conversationId = await StartConversationAsync(widget);
        await RequestAsync(conversationId);

        await InScopeAsync((IHandoverService handover) => handover.AddStaffMessageAsync(conversationId, "Sam", "On it.", HandoverService.ViaSlack, Ct));

        (await AssigneeAsync(conversationId)).ShouldBe((null, "Sam"));
    }

    private async Task<(string? Id, string? Name)> AssigneeAsync(Guid conversationId)
    {
        var conversation = (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(conversationId, includeTurns: false, Ct))).ShouldNotBeNull();
        return (conversation.AssigneeId, conversation.AssigneeName);
    }

    private async Task<Widget> AddWidgetAsync(WidgetConversations? conversations = null, WidgetHandover? handover = null)
    {
        var now = _time.GetUtcNow();
        var widget = new Widget
        {
            Id = Guid.CreateVersion7(),
            PublicKey = Widget.KeyPrefix + Guid.NewGuid().ToString("N")[..24],
            Name = "Shop help",
            AgentId = Guid.CreateVersion7(),
            Settings = new WidgetSettings { Conversations = conversations ?? new(), Handover = handover ?? new() },
            CreatedAt = now,
            UpdatedAt = now,
        };
        return await InScopeAsync((IWidgetRepository widgets) => widgets.AddAsync(widget, Ct));
    }

    private async Task<Guid> StartConversationAsync(Widget widget, string? visitorEmail = null)
    {
        var now = _time.GetUtcNow();
        var visitor = await InScopeAsync((IWidgetRepository widgets) => widgets.AddVisitorAsync(
            new WidgetVisitor { Id = Guid.CreateVersion7(), WidgetId = widget.Id, Name = "Ana", Email = visitorEmail, CreatedAt = now, LastSeenAt = now },
            Ct));
        var conversation = new Conversation
        {
            Channel = ConversationChannel.Widget,
            WidgetId = widget.Id,
            VisitorId = visitor.Id,
            Title = "Parcel",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        await AddMessageAsync(conversation.Id, MessageAuthor.Visitor, "Where is my parcel?");
        await AddMessageAsync(conversation.Id, MessageAuthor.Assistant, "It left the warehouse today.");
        return conversation.Id;
    }

    private Task AddMessageAsync(Guid conversationId, MessageAuthor author, string text) =>
        InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(
            new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversationId, Author = author, Text = text, CreatedAt = _time.GetUtcNow() },
            Ct));

    private async Task<Guid> RequestAsync(Guid conversationId)
    {
        var (outcome, handover) = await InScopeAsync((IHandoverService service) =>
            service.RequestAsync(conversationId, HandoverSource.Visitor, "Asked for a person", summary: null, Ct));
        outcome.Started.ShouldBeTrue();
        return handover.ShouldNotBeNull().Id;
    }

    private async Task<HandoverRequest> LoadRequestAsync(Guid handoverId) =>
        (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(handoverId, Ct))).ShouldNotBeNull();

    private async Task<HandoverStatus> RequestStatusAsync(Guid handoverId) => (await LoadRequestAsync(handoverId)).Status;

    private Task StaffReplyAsync(Guid conversationId) =>
        InScopeAsync((IHandoverService handover) => handover.AddStaffMessageAsync(conversationId, "Dana", "It ships today.", HandoverService.ViaPortal, Ct));

    private Task ExpireAsync(Guid handoverId) =>
        InScopeAsync((HandoverExpireJob job) => job.HandleAsync(new JobContext("expire-1", HandoverJobs.Expire, handoverId.ToString(), 1, 3), Ct));

    private Task CloseIdleAsync() =>
        InScopeAsync((CloseIdleConversationsJob job) => job.HandleAsync(new JobContext("close-idle-1", CloseIdleConversationsJob.Kind, "all", 1, 1), Ct));

    private async Task<ConversationStatus> StatusAsync(Guid conversationId) =>
        (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(conversationId, includeTurns: false, Ct))).ShouldNotBeNull().Status;

    private Task<IReadOnlyList<ConversationMessage>> MessagesAsync(Guid conversationId) =>
        InScopeAsync((IConversationRepository conversations) => conversations.ListMessagesAsync(conversationId, cancellationToken: Ct));

    private async Task<ConversationMessage> LastMessageAsync(Guid conversationId) => (await MessagesAsync(conversationId))[^1];

    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<(string Kind, string Key, DateTimeOffset? RunAt)> Enqueued { get; } = [];

        public Task EnqueueAsync(string kind, string key, DateTimeOffset? runAt = null, CancellationToken cancellationToken = default)
        {
            Enqueued.Add((kind, key, runAt));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIntegrations : IIntegrationSettings
    {
        public EmailSettings Email { get; set; } = new();

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult(Email);

        public Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SlackSettings());

        public Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default) => Task.FromResult((false, false));

        public Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingEmail : IEmailTransport
    {
        public List<MimeMessage> Sent { get; } = [];

        public Exception? Failure { get; set; }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
        {
            if (Failure is { } failure)
            {
                return Task.FromException(failure);
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task<int> ProcessInboxAsync(
            Func<EmailEnvelope, bool> wants,
            Func<InboundEmail, CancellationToken, Task<bool>> handle,
            int maxMessages,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> TestInboxAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
