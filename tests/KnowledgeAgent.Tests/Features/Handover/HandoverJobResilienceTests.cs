using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Live;
using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using HandoverRequest = KnowledgeAgent.Features.Handover.Handover;

namespace KnowledgeAgent.Tests.Features.Handover;

public sealed class HandoverJobResilienceTests : IAsyncLifetime
{
    private static readonly EmailSettings WorkingEmail = new()
    {
        Enabled = true,
        SmtpHost = "smtp.shop.test",
        FromAddress = "help@shop.test",
        SupportAddress = "team@shop.test",
    };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingJobQueue _jobs = new();
    private readonly FakeIntegrations _integrations = new();
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = PersistenceHost.Build(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Persistence:InMemoryDatabaseName"] = $"handover-resilience-{Guid.NewGuid():N}",
            },
            _time,
            builder =>
            {
                var services = builder.Services;
                services.AddSingleton<IJobQueue>(_jobs);
                services.AddSingleton<IIntegrationSettings>(_integrations);
                services.AddSingleton<IEmailTransport, FailingEmail>();
                services.AddSingleton<ISlackGateway, UnusedSlack>();
                services.AddSingleton<ConversationEventHub>();
                services.AddSingleton<ILiveBus, InProcessLiveBus>();
                services.AddScoped<ConversationTimeline>();
                services.AddScoped<IWidgetRepository, WidgetRepository>();
                services.AddScoped<HandoverContextLoader>();
                services.AddScoped<IPublicAddress, PublicAddressStore>();
                services.AddScoped<HandoverDispatchJob>();
                services.AddScoped<HandoverExpireJob>();
            });
        await PersistenceHost.RunStartupInitializersAsync(_host.Services, Ct);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_dispatch_that_gives_up_keeps_the_error_and_tells_the_conversation_nobody_was_reached()
    {
        _integrations.Email = WorkingEmail;
        var conversationId = await StartConversationAsync();
        var handover = await AddPendingAsync(conversationId, WidgetChoices.HandoverEmail, _time.GetUtcNow());

        await DispatchFailedAsync(handover.Id, "The mail server is down.");

        var failed = await LoadAsync(handover.Id);
        failed.Status.ShouldBe(HandoverStatus.Pending);
        failed.LastError.ShouldBe("The mail server is down.");
        var notice = (await MessagesAsync(conversationId))[^1];
        notice.Author.ShouldBe(MessageAuthor.System);
        notice.Text.ShouldBe(HandoverDispatchJob.NotDeliveredNotice);
    }

    [Fact]
    public async Task A_failed_slack_dispatch_stays_quiet_while_an_escalation_email_is_still_to_come()
    {
        _integrations.Email = WorkingEmail;
        var conversationId = await StartConversationAsync();
        var handover = await AddPendingAsync(conversationId, WidgetChoices.HandoverSlack, _time.GetUtcNow());
        var before = (await MessagesAsync(conversationId)).Count;

        await DispatchFailedAsync(handover.Id, "channel_not_found");

        (await LoadAsync(handover.Id)).LastError.ShouldBe("channel_not_found");
        (await MessagesAsync(conversationId)).Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_failed_slack_dispatch_without_email_tells_the_conversation()
    {
        var conversationId = await StartConversationAsync();
        var handover = await AddPendingAsync(conversationId, WidgetChoices.HandoverSlack, _time.GetUtcNow());

        await DispatchFailedAsync(handover.Id, "channel_not_found");

        (await MessagesAsync(conversationId))[^1].Text.ShouldBe(HandoverDispatchJob.NotDeliveredNotice);
    }

    [Fact]
    public async Task Startup_queues_the_lost_timers_of_pending_handovers_again_and_overdue_ones_expire_at_once()
    {
        _integrations.Email = WorkingEmail;
        var now = _time.GetUtcNow();
        var overdue = await AddPendingAsync(await StartConversationAsync(), WidgetChoices.HandoverSlack, now.AddMinutes(-40));
        var waiting = await AddPendingAsync(await StartConversationAsync(), WidgetChoices.HandoverSlack, now.AddMinutes(-5));
        var joined = await AddPendingAsync(await StartConversationAsync(), WidgetChoices.HandoverSlack, now.AddMinutes(-40), HandoverStatus.Active);

        await RecoverAsync(new JobEngineInfo("inprocess", DashboardUrl: null));

        // The widget's defaults: escalate after 10 minutes, expire after 15.
        _jobs.Enqueued.ShouldBe(
        [
            (HandoverJobs.Expire, overdue.Id.ToString(), overdue.RequestedAt.AddMinutes(15)),
            (HandoverJobs.Escalate, waiting.Id.ToString(), waiting.RequestedAt.AddMinutes(10)),
            (HandoverJobs.Expire, waiting.Id.ToString(), waiting.RequestedAt.AddMinutes(15)),
        ]);
        _jobs.Enqueued.ShouldNotContain(job => job.Key == joined.Id.ToString());

        await InScopeAsync((HandoverExpireJob job) => job.HandleAsync(new JobContext("expire-1", HandoverJobs.Expire, overdue.Id.ToString(), 1, 3), Ct));
        (await LoadAsync(overdue.Id)).Status.ShouldBe(HandoverStatus.Expired);
        (await LoadAsync(joined.Id)).Status.ShouldBe(HandoverStatus.Active);
    }

    [Fact]
    public async Task Startup_leaves_the_timers_to_inngest_which_stores_them()
    {
        await AddPendingAsync(await StartConversationAsync(), WidgetChoices.HandoverEmail, _time.GetUtcNow().AddMinutes(-40));

        await RecoverAsync(new JobEngineInfo("inngest", "http://localhost:8288"));

        _jobs.Enqueued.ShouldBeEmpty();
    }

    private Task RecoverAsync(JobEngineInfo engine) =>
        new HandoverTimerRecovery(_host.Services.GetRequiredService<IServiceScopeFactory>(), _jobs, engine, _time, NullLogger<HandoverTimerRecovery>.Instance)
            .InitializeAsync(Ct);

    private Task DispatchFailedAsync(Guid handoverId, string error) =>
        InScopeAsync((HandoverDispatchJob job) => job.OnFailureAsync(new JobFailure("run-1", HandoverJobs.Dispatch, handoverId.ToString(), 6, error), Ct));

    private async Task<Guid> StartConversationAsync()
    {
        var now = _time.GetUtcNow();
        var widget = await InScopeAsync((IWidgetRepository widgets) => widgets.AddAsync(
            new Widget
            {
                Id = Guid.CreateVersion7(),
                PublicKey = Widget.KeyPrefix + Guid.NewGuid().ToString("N")[..24],
                Name = "Shop help",
                AgentId = Guid.CreateVersion7(),
                Settings = new WidgetSettings(),
                CreatedAt = now,
                UpdatedAt = now,
            },
            Ct));
        var conversation = new Conversation
        {
            Channel = ConversationChannel.Widget,
            WidgetId = widget.Id,
            Title = "Parcel",
            Status = ConversationStatus.HandoverPending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await InScopeAsync((IConversationRepository conversations) => conversations.CreateAsync(conversation, Ct));
        await InScopeAsync((IConversationRepository conversations) => conversations.AddMessageAsync(
            new ConversationMessage { Id = Guid.CreateVersion7(), ConversationId = conversation.Id, Author = MessageAuthor.Visitor, Text = "Can a person help?", CreatedAt = now },
            Ct));
        return conversation.Id;
    }

    private async Task<HandoverRequest> AddPendingAsync(Guid conversationId, string channel, DateTimeOffset requestedAt, HandoverStatus status = HandoverStatus.Pending)
    {
        var conversation = (await InScopeAsync((IConversationRepository conversations) => conversations.GetAsync(conversationId, includeTurns: false, Ct))).ShouldNotBeNull();
        return await InScopeAsync((IHandoverRepository handovers) => handovers.AddAsync(
            new HandoverRequest
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                WidgetId = conversation.WidgetId,
                Channel = channel,
                Status = status,
                Source = HandoverSource.Visitor,
                Token = Guid.NewGuid().ToString("N")[..HandoverRequest.TokenLength],
                RequestedAt = requestedAt,
                UpdatedAt = requestedAt,
            },
            Ct));
    }

    private async Task<HandoverRequest> LoadAsync(Guid handoverId) =>
        (await InScopeAsync((IHandoverRepository handovers) => handovers.GetAsync(handoverId, Ct))).ShouldNotBeNull();

    private Task<IReadOnlyList<ConversationMessage>> MessagesAsync(Guid conversationId) =>
        InScopeAsync((IConversationRepository conversations) => conversations.ListMessagesAsync(conversationId, cancellationToken: Ct));

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

    // The expiry emails the transcript when nobody got it; here the mail server is down, as in the dispatch tests.
    private sealed class FailingEmail : IEmailTransport
    {
        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("The mail server is down."));

        public Task<int> ProcessInboxAsync(
            Func<EmailEnvelope, bool> wants,
            Func<InboundEmail, CancellationToken, Task<bool>> handle,
            int maxMessages,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> TestInboxAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSlack : ISlackGateway
    {
        public Task<string> PostMessageAsync(string channel, string text, JsonArray? blocks = null, string? threadTs = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateMessageAsync(string channel, string ts, string text, JsonArray? blocks = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> OpenSocketAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
