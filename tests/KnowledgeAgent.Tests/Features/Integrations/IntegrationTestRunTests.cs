using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Infrastructure.Web;
using KnowledgeAgent.TestKit;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace KnowledgeAgent.Tests.Features.Integrations;

public sealed class IntegrationTestRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeSettings _settings = new();
    private readonly FakeSlack _slack = new();

    private IntegrationService Service(IEmailTransport? email = null) =>
        new(_settings, email ?? new UnusedEmail(), _slack, _settings, NullLogger<IntegrationService>.Instance);

    [Fact]
    public async Task Email_that_is_not_set_up_fails_and_names_what_is_missing()
    {
        var result = await Service().TestEmailAsync(null, Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldBe("Email is not set up: add the SMTP server and the sender address in the email settings.");
    }

    [Fact]
    public async Task Email_with_only_the_sender_missing_names_the_sender()
    {
        _settings.Email = new EmailSettings { Enabled = true, SmtpHost = "smtp.shop.test" };

        var result = await Service().TestEmailAsync(null, Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldBe("Email is not set up: add the sender address in the email settings.");
    }

    [Fact]
    public async Task Email_that_is_turned_off_fails()
    {
        _settings.Email = new EmailSettings { SmtpHost = "smtp.shop.test", FromAddress = "help@shop.test" };

        var result = await Service().TestEmailAsync(null, Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("turned off");
    }

    [Fact]
    public async Task A_recipient_that_is_not_an_email_address_is_a_validation_error()
    {
        _settings.Email = new EmailSettings { Enabled = true, SmtpHost = "smtp.shop.test", FromAddress = "help@shop.test" };

        var error = await Should.ThrowAsync<ValidationFailedException>(() => Service().TestEmailAsync(new TestEmailRequest("not an address"), Ct));

        error.Errors.ShouldContainKey("to");
    }

    [Fact]
    public async Task A_mail_server_that_refuses_fails_with_its_reason()
    {
        _settings.Email = new EmailSettings { Enabled = true, SmtpHost = "127.0.0.1", SmtpPort = 1, SmtpSecurity = MailSecurity.None, FromAddress = "help@shop.test" };

        var result = await Service(new MailKitEmailTransport(_settings, NullLogger<MailKitEmailTransport>.Instance)).TestEmailAsync(null, Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldStartWith("Could not sign in to the mail server:");
    }

    [Fact]
    public async Task Without_a_recipient_it_signs_in_and_checks_the_reply_mailbox_without_sending()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var mail = await StartMailServerAsync();
        _settings.Email = SettingsFor(mail) with { SupportAddress = null };

        var result = await Service(new MailKitEmailTransport(_settings, NullLogger<MailKitEmailTransport>.Instance)).TestEmailAsync(new TestEmailRequest("  "), Ct);

        result.Ok.ShouldBeTrue();
        result.Message.ShouldBe("Connected to the mail server; enter an address to receive a test email");
        result.Details.ShouldNotBeNull();
        result.Details.ShouldContain(detail => detail.StartsWith("Opened the reply mailbox replies@shop.test", StringComparison.Ordinal));
        (await CountAsync(mail, "team@shop.test")).ShouldBe(0);
    }

    [Fact]
    public async Task With_a_recipient_it_sends_a_test_email_there()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var mail = await StartMailServerAsync();
        _settings.Email = SettingsFor(mail);

        var result = await Service(new MailKitEmailTransport(_settings, NullLogger<MailKitEmailTransport>.Instance)).TestEmailAsync(new TestEmailRequest("ada@shop.test"), Ct);

        result.Ok.ShouldBeTrue();
        result.Message.ShouldBe("A test email went to ada@shop.test.");
        (await CountAsync(mail, "ada@shop.test")).ShouldBe(1);
        (await CountAsync(mail, "team@shop.test")).ShouldBe(0);
    }

    [Fact]
    public async Task Without_an_entered_recipient_it_sends_to_the_support_address()
    {
        Assert.SkipUnless(DockerAvailability.IsAvailable, DockerAvailability.SkipReason);
        await using var mail = await StartMailServerAsync();
        _settings.Email = SettingsFor(mail);

        var result = await Service(new MailKitEmailTransport(_settings, NullLogger<MailKitEmailTransport>.Instance)).TestEmailAsync(null, Ct);

        result.Ok.ShouldBeTrue();
        result.Message.ShouldBe("A test email went to team@shop.test.");
        (await CountAsync(mail, "team@shop.test")).ShouldBe(1);
    }

    [Fact]
    public async Task Slack_that_is_not_set_up_fails_and_names_both_tokens()
    {
        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldBe("Slack is not set up: add the bot token and the app-level token in the Slack settings.");
        _slack.SignIns.ShouldBe(0);
    }

    [Fact]
    public async Task Slack_that_is_turned_off_fails()
    {
        _settings.Slack = new SlackSettings { BotToken = "xoxb-test", AppToken = "xapp-test", ChannelId = "C123" };

        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("turned off");
    }

    [Fact]
    public async Task Slack_that_refuses_the_bot_token_fails()
    {
        _settings.Slack = new SlackSettings { Enabled = true, BotToken = "xoxb-test", AppToken = "xapp-test", ChannelId = "C123" };
        _slack.SignInFailure = new SlackApiException("auth.test", "invalid_auth");

        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("invalid_auth");
        _slack.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Slack_without_a_channel_signs_in_without_posting()
    {
        _settings.Slack = new SlackSettings { Enabled = true, BotToken = "xoxb-test", AppToken = "xapp-test" };

        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeTrue();
        result.Message.ShouldBe("Signed in; choose a channel to receive the test message");
        _slack.SignIns.ShouldBe(1);
        _slack.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Slack_with_a_channel_posts_the_test_message_there()
    {
        _settings.Slack = new SlackSettings { Enabled = true, BotToken = "xoxb-test", AppToken = "xapp-test", ChannelId = "C123" };

        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeTrue();
        result.Message.ShouldBe("A test message was posted to C123.");
        _slack.Posted.ShouldHaveSingleItem().Channel.ShouldBe("C123");
    }

    [Fact]
    public async Task Slack_that_cannot_post_to_the_channel_fails_with_a_hint()
    {
        _settings.Slack = new SlackSettings { Enabled = true, BotToken = "xoxb-test", AppToken = "xapp-test", ChannelId = "C123" };
        _slack.PostFailure = new SlackApiException("chat.postMessage", "not_in_channel");

        var result = await Service().TestSlackAsync(Ct);

        result.Ok.ShouldBeFalse();
        result.Message.ShouldContain("Invite the bot to the channel");
    }

    // GreenMail with SMTP on 3025 and IMAP on 3143, accepting any address and password.
    private static async Task<IContainer> StartMailServerAsync()
    {
        var mail = new ContainerBuilder(ContainerImages.GreenMail)
            .WithPortBinding(3025, assignRandomHostPort: true)
            .WithPortBinding(3143, assignRandomHostPort: true)
            .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3143))
            .Build();
        await mail.StartAsync(Ct);
        return mail;
    }

    private static EmailSettings SettingsFor(IContainer mail) => new()
    {
        Enabled = true,
        SmtpHost = mail.Hostname,
        SmtpPort = mail.GetMappedPublicPort(3025),
        SmtpSecurity = MailSecurity.None,
        FromAddress = "help@shop.test",
        FromName = "Shop help",
        SupportAddress = "team@shop.test",
        ReplyAddress = "replies@shop.test",
        ImapHost = mail.Hostname,
        ImapPort = mail.GetMappedPublicPort(3143),
        ImapSecurity = MailSecurity.None,
        ImapUser = "replies@shop.test",
        ImapPassword = "secret",
    };

    // GreenMail delivers at once, so the mailbox can be counted right after the test.
    private static async Task<int> CountAsync(IContainer mail, string mailbox)
    {
        using var imap = new ImapClient();
        await imap.ConnectAsync(mail.Hostname, mail.GetMappedPublicPort(3143), SecureSocketOptions.None, Ct);
        await imap.AuthenticateAsync(mailbox, "secret", Ct);
        await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, Ct);
        var count = imap.Inbox.Count;
        await imap.DisconnectAsync(true, Ct);
        return count;
    }

    private sealed class FakeSettings : IIntegrationSettings, ISlackConnectionState
    {
        public EmailSettings Email { get; set; } = new();

        public SlackSettings Slack { get; set; } = new();

        public bool IsConnected => true;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<EmailSettings> GetEmailAsync(CancellationToken cancellationToken = default) => Task.FromResult(Email);

        public Task<SlackSettings> GetSlackAsync(CancellationToken cancellationToken = default) => Task.FromResult(Slack);

        public Task<(bool Email, bool Slack)> GetSavedAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, true));

        public Task SaveEmailAsync(EmailSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveSlackAsync(SlackSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSlack : ISlackGateway
    {
        public List<(string Channel, string Text)> Posted { get; } = [];

        public int SignIns { get; private set; }

        public Exception? SignInFailure { get; set; }

        public Exception? PostFailure { get; set; }

        public Task<string> PostMessageAsync(string channel, string text, JsonArray? blocks = null, string? threadTs = null, CancellationToken cancellationToken = default)
        {
            if (PostFailure is { } failure)
            {
                return Task.FromException<string>(failure);
            }

            Posted.Add((channel, text));
            return Task.FromResult("1.0");
        }

        public Task<string> TestAsync(CancellationToken cancellationToken = default)
        {
            SignIns++;
            return SignInFailure is { } failure ? Task.FromException<string>(failure) : Task.FromResult("Signed in to Shop as @helper.");
        }

        public Task UpdateMessageAsync(string channel, string ts, string text, JsonArray? blocks = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> OpenSocketAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Unconfigured email must fail before any transport call.
    private sealed class UnusedEmail : IEmailTransport
    {
        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ProcessInboxAsync(
            Func<EmailEnvelope, bool> wants,
            Func<InboundEmail, CancellationToken, Task<bool>> handle,
            int maxMessages,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> TestAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> TestInboxAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
