using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Widgets;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace KnowledgeAgent.Features.Integrations;

public interface ISlackConnectionState
{
    bool IsConnected { get; }
}

internal sealed partial class IntegrationService(
    IIntegrationSettings integrations,
    IEmailTransport email,
    ISlackGateway slack,
    ISlackConnectionState slackConnection,
    ILogger<IntegrationService> logger)
{
    public async Task<IntegrationsDto> GetAsync(CancellationToken cancellationToken)
    {
        var mail = await integrations.GetEmailAsync(cancellationToken);
        var chat = await integrations.GetSlackAsync(cancellationToken);
        var saved = await integrations.GetSavedAsync(cancellationToken);
        return new IntegrationsDto(ToDto(mail, saved.Email), ToDto(chat, saved.Slack, slackConnection.IsConnected));
    }

    public async Task<EmailSettingsDto> SaveEmailAsync(SaveEmailSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await integrations.GetEmailAsync(cancellationToken);
        var errors = new ValidationErrors();
        var settings = new EmailSettings
        {
            Enabled = request.Enabled,
            SmtpHost = Host(request.SmtpHost, "smtpHost", errors),
            SmtpPort = Port(request.SmtpPort, "smtpPort", errors),
            SmtpSecurity = Choice(request.SmtpSecurity, "smtpSecurity", errors),
            SmtpUser = Trimmed(request.SmtpUser),
            SmtpPassword = Secret(request.SmtpPassword, current.SmtpPassword),
            FromAddress = Address(request.FromAddress, "fromAddress", errors),
            FromName = Trimmed(request.FromName),
            SupportAddress = Address(request.SupportAddress, "supportAddress", errors),
            ReplyAddress = Address(request.ReplyAddress, "replyAddress", errors),
            PlusAddressing = request.PlusAddressing,
            ImapHost = Host(request.ImapHost, "imapHost", errors),
            ImapPort = Port(request.ImapPort, "imapPort", errors),
            ImapSecurity = Choice(request.ImapSecurity, "imapSecurity", errors),
            ImapUser = Trimmed(request.ImapUser),
            ImapPassword = Secret(request.ImapPassword, current.ImapPassword),
            ProcessedFolder = Trimmed(request.ProcessedFolder) ?? "Processed",
            StaffAddresses = (request.StaffAddresses ?? []).Select(item => Address(item, "staffAddresses", errors)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StaffDomains = (request.StaffDomains ?? []).Select(item => Domain(item, errors)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            PortalUrl = Url(request.PortalUrl, errors),
        };

        if (settings.Enabled && (settings.SmtpHost is null || settings.FromAddress is null))
        {
            errors.Add(settings.SmtpHost is null ? "smtpHost" : "fromAddress", "Email needs an SMTP server and a sender address.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        await integrations.SaveEmailAsync(settings, cancellationToken);
        return ToDto(settings, saved: true);
    }

    public async Task<SlackSettingsDto> SaveSlackAsync(SaveSlackSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await integrations.GetSlackAsync(cancellationToken);
        var errors = new ValidationErrors();
        var botToken = Secret(request.BotToken, current.BotToken);
        var appToken = Secret(request.AppToken, current.AppToken);
        if (botToken is not null && !botToken.StartsWith("xoxb-", StringComparison.Ordinal))
        {
            errors.Add("botToken", "The bot token starts with xoxb-.");
        }

        if (appToken is not null && !appToken.StartsWith("xapp-", StringComparison.Ordinal))
        {
            errors.Add("appToken", "The app-level token starts with xapp-.");
        }

        var channel = Trimmed(request.ChannelId);
        if (channel is not null && !SlackChannel().IsMatch(channel))
        {
            errors.Add("channelId", "Use the channel id, such as C0123456789 (in Slack: channel details, at the bottom).");
        }

        if (request.Enabled && (botToken is null || channel is null))
        {
            errors.Add(botToken is null ? "botToken" : "channelId", "Slack needs the bot token and a channel.");
        }

        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var settings = new SlackSettings { Enabled = request.Enabled, BotToken = botToken, AppToken = appToken, ChannelId = channel };
        await integrations.SaveSlackAsync(settings, cancellationToken);
        return ToDto(settings, saved: true, slackConnection.IsConnected);
    }

    public async Task<IntegrationTestResultDto> TestEmailAsync(TestEmailRequest? request, CancellationToken cancellationToken)
    {
        var settings = await integrations.GetEmailAsync(cancellationToken);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            missing.Add("the SMTP server");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            missing.Add("the sender address");
        }

        if (missing.Count > 0)
        {
            return Failed($"Email is not set up: add {string.Join(" and ", missing)} in the email settings.");
        }

        if (!settings.CanSend)
        {
            return Failed("Email is turned off: turn it on in the email settings and save.");
        }

        var errors = new ValidationErrors();
        var to = Address(request?.To, "to", errors) ?? settings.SupportAddress;
        if (errors.HasErrors)
        {
            throw new ValidationFailedException(errors.ToDictionary());
        }

        var details = new List<string>();
        string message;
        try
        {
            if (to is null)
            {
                details.Add(await email.TestAsync(cancellationToken));
                message = "Connected to the mail server; enter an address to receive a test email";
            }
            else
            {
                await email.SendAsync(TestEmail(settings.FromName, settings.FromAddress, to), cancellationToken);
                details.Add($"Sent through the mail server {settings.SmtpHost}.");
                message = $"A test email went to {to}.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(logger, ex, "email");
            return Failed(
                to is null
                    ? $"Could not sign in to the mail server: {ex.Message}"
                    : $"The test email to {to} was not sent: {ex.Message}",
                details);
        }

        try
        {
            details.Add(await email.TestInboxAsync(cancellationToken) ?? "Replies are not read until the IMAP mailbox and the reply address are set.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(logger, ex, "imap");
            details.Add($"The reply mailbox could not be opened: {ex.Message}");
            return Failed(
                to is null
                    ? "Connected to the mail server, but the reply mailbox could not be opened."
                    : $"A test email went to {to}, but the reply mailbox could not be opened.",
                details);
        }

        return new IntegrationTestResultDto(true, message, details);
    }

    public async Task<IntegrationTestResultDto> TestSlackAsync(CancellationToken cancellationToken)
    {
        var settings = await integrations.GetSlackAsync(cancellationToken);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            missing.Add("the bot token");
        }

        if (string.IsNullOrWhiteSpace(settings.AppToken))
        {
            missing.Add("the app-level token");
        }

        if (missing.Count > 0)
        {
            return Failed($"Slack is not set up: add {string.Join(" and ", missing)} in the Slack settings.");
        }

        if (!settings.Enabled)
        {
            return Failed("Slack is turned off: turn it on in the Slack settings and save.");
        }

        var details = new List<string>();
        try
        {
            details.Add(await slack.TestAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(logger, ex, "slack");
            return Failed($"Slack did not accept the bot token: {ex.Message}");
        }

        details.Add(slackConnection.IsConnected
            ? "Connected for replies, so answers in the thread reach the visitor."
            : "Not connected for replies right now; check the app-level token if this stays.");
        if (settings.ChannelId is not { } channel)
        {
            return new IntegrationTestResultDto(true, "Signed in; choose a channel to receive the test message", details);
        }

        try
        {
            await slack.PostMessageAsync(channel, "Knowledge Agent is connected. Requests for a person will appear in this channel.", cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTestFailed(logger, ex, "slack");
            var hint = ex is SlackApiException { Error: "not_in_channel" or "channel_not_found" }
                ? " Invite the bot to the channel, or check the channel id."
                : string.Empty;
            return Failed($"Signed in, but the test message to {channel} was not posted: {ex.Message}.{hint}", details);
        }

        return new IntegrationTestResultDto(true, $"A test message was posted to {channel}.", details);
    }

    private static IntegrationTestResultDto Failed(string message, IReadOnlyList<string>? details = null) =>
        new(false, message, details is { Count: > 0 } ? details : null);

    private static MimeMessage TestEmail(string? fromName, string fromAddress, string to)
    {
        var test = new MimeMessage { Subject = "Knowledge Agent: test email" };
        test.From.Add(new MailboxAddress(fromName ?? "Knowledge Agent", fromAddress));
        test.To.Add(MailboxAddress.Parse(to));
        test.Body = new TextPart("plain") { Text = "Email from Knowledge Agent works. Handover transcripts will arrive like this." };
        return test;
    }

    private static EmailSettingsDto ToDto(EmailSettings settings, bool saved) => new(
        settings.Enabled,
        settings.SmtpHost,
        settings.SmtpPort,
        settings.SmtpSecurity,
        settings.SmtpUser,
        !string.IsNullOrEmpty(settings.SmtpPassword),
        settings.FromAddress,
        settings.FromName,
        settings.SupportAddress,
        settings.ReplyAddress,
        settings.PlusAddressing,
        settings.ImapHost,
        settings.ImapPort,
        settings.ImapSecurity,
        settings.ImapUser,
        !string.IsNullOrEmpty(settings.ImapPassword),
        settings.ProcessedFolder,
        settings.StaffAddresses,
        settings.StaffDomains,
        settings.PortalUrl,
        settings.CanSend,
        settings.CanReceive,
        saved);

    private static SlackSettingsDto ToDto(SlackSettings settings, bool saved, bool connected) => new(
        settings.Enabled,
        !string.IsNullOrEmpty(settings.BotToken),
        !string.IsNullOrEmpty(settings.AppToken),
        settings.ChannelId,
        settings.CanPost,
        settings.CanReceive,
        saved,
        connected);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Null keeps the saved secret and an empty string removes it.
    private static string? Secret(string? value, string? current) => value is null ? current : Trimmed(value);

    private static string? Host(string? value, string field, ValidationErrors errors)
    {
        var host = Trimmed(value);
        if (host is not null && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            errors.Add(field, "Enter a host name such as smtp.example.com.");
        }

        return host;
    }

    private static int Port(int value, string field, ValidationErrors errors)
    {
        if (value is < 1 or > 65535)
        {
            errors.Add(field, "Enter a port from 1 to 65535.");
        }

        return value;
    }

    private static string Choice(string? value, string field, ValidationErrors errors)
    {
        var choice = Trimmed(value)?.ToLowerInvariant() ?? MailSecurity.Auto;
        if (!MailSecurity.All.Contains(choice, StringComparer.Ordinal))
        {
            errors.Add(field, $"Choose one of: {string.Join(", ", MailSecurity.All)}.");
        }

        return choice;
    }

    private static string? Address(string? value, string field, ValidationErrors errors)
    {
        var address = Trimmed(value);
        if (address is not null && !WidgetEmails.IsValid(address))
        {
            errors.Add(field, $"'{address}' is not an email address.");
        }

        return address;
    }

    private static string? Domain(string? value, ValidationErrors errors)
    {
        var domain = Trimmed(value)?.TrimStart('@').ToLowerInvariant();
        if (domain is not null && (Uri.CheckHostName(domain) != UriHostNameType.Dns || !domain.Contains('.', StringComparison.Ordinal)))
        {
            errors.Add("staffDomains", $"'{domain}' is not a domain such as example.com.");
        }

        return domain;
    }

    private static string? Url(string? value, ValidationErrors errors)
    {
        var url = Trimmed(value)?.TrimEnd('/');
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            errors.Add("portalUrl", "Enter the portal address, such as https://support.example.com.");
        }

        return url;
    }

    [GeneratedRegex("^[CG][A-Z0-9]{6,}$")]
    private static partial Regex SlackChannel();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Integration} test failed")]
    private static partial void LogTestFailed(ILogger logger, Exception exception, string integration);
}
