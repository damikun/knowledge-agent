using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace KnowledgeAgent.Features.Integrations;

public sealed record EmailEnvelope(
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References,
    string? FromAddress,
    IReadOnlyList<string> To,
    string? Subject)
{
    public DateTimeOffset? Date { get; init; }
}

public sealed record InboundEmail(EmailEnvelope Envelope, string? FromName, string Text);

public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default);

    // A message is marked seen and moved to the processed folder only when handle returns true;
    // unwanted messages stay unseen for whoever reads the mailbox.
    Task<int> ProcessInboxAsync(
        Func<EmailEnvelope, bool> wants,
        Func<InboundEmail, CancellationToken, Task<bool>> handle,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task<string> TestAsync(CancellationToken cancellationToken = default);

    Task<string?> TestInboxAsync(CancellationToken cancellationToken = default);
}

internal sealed partial class MailKitEmailTransport(IIntegrationSettings integrations, ILogger<MailKitEmailTransport> logger) : IEmailTransport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = await integrations.GetEmailAsync(cancellationToken);
        if (!settings.CanSend)
        {
            throw new InvalidOperationException("Email is not set up: add an SMTP server and a sender on the Notifications page.");
        }

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, Security(settings.SmtpSecurity), cancellationToken);
        if (!string.IsNullOrEmpty(settings.SmtpUser))
        {
            await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        LogSent(logger, message.MessageId ?? "(none)", string.Join(", ", message.To.Mailboxes.Select(mailbox => mailbox.Address)));
    }

    public async Task<int> ProcessInboxAsync(
        Func<EmailEnvelope, bool> wants,
        Func<InboundEmail, CancellationToken, Task<bool>> handle,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        var settings = await integrations.GetEmailAsync(cancellationToken);
        if (!settings.CanReceive)
        {
            return 0;
        }

        using var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(settings.ImapHost, settings.ImapPort, Security(settings.ImapSecurity), cancellationToken);
        await client.AuthenticateAsync(settings.ImapUser, settings.ImapPassword ?? string.Empty, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        var unseen = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        var handled = 0;
        IMailFolder? processed = null;
        if (unseen.Count > 0)
        {
            var summaries = await inbox.FetchAsync(
                unseen,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.References,
                cancellationToken);
            foreach (var summary in summaries.Take(maxMessages))
            {
                var envelope = ToEnvelope(summary);
                if (!wants(envelope))
                {
                    continue;
                }

                var message = await inbox.GetMessageAsync(summary.UniqueId, cancellationToken);
                var from = message.From.Mailboxes.FirstOrDefault();
                var done = await handle(new InboundEmail(envelope, from?.Name, BodyText(message)), cancellationToken);
                await inbox.AddFlagsAsync(summary.UniqueId, MessageFlags.Seen, silent: true, cancellationToken);
                if (done)
                {
                    handled++;
                    processed ??= await ProcessedFolderAsync(client, settings.ProcessedFolder, cancellationToken);
                    if (processed is not null)
                    {
                        await inbox.MoveToAsync(summary.UniqueId, processed, cancellationToken);
                    }
                }
            }
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
        return handled;
    }

    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var settings = await integrations.GetEmailAsync(cancellationToken);
        if (!settings.CanSend)
        {
            throw new InvalidOperationException("Email is not set up: add an SMTP server and a sender address.");
        }

        using var smtp = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await smtp.ConnectAsync(settings.SmtpHost, settings.SmtpPort, Security(settings.SmtpSecurity), cancellationToken);
        if (!string.IsNullOrEmpty(settings.SmtpUser))
        {
            await smtp.AuthenticateAsync(settings.SmtpUser, settings.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await smtp.DisconnectAsync(quit: true, cancellationToken);
        return $"Signed in to the mail server {settings.SmtpHost}.";
    }

    public async Task<string?> TestInboxAsync(CancellationToken cancellationToken = default)
    {
        var settings = await integrations.GetEmailAsync(cancellationToken);
        if (!settings.CanReceive)
        {
            return null;
        }

        using var imap = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        await imap.ConnectAsync(settings.ImapHost, settings.ImapPort, Security(settings.ImapSecurity), cancellationToken);
        await imap.AuthenticateAsync(settings.ImapUser, settings.ImapPassword ?? string.Empty, cancellationToken);
        await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        var count = imap.Inbox.Count;
        await imap.DisconnectAsync(quit: true, cancellationToken);
        return $"Opened the reply mailbox {settings.ImapUser}; it holds {count} message(s).";
    }

    internal static SecureSocketOptions Security(string? security) => security?.Trim().ToLowerInvariant() switch
    {
        MailSecurity.None => SecureSocketOptions.None,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        MailSecurity.Ssl => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };

    private static EmailEnvelope ToEnvelope(IMessageSummary summary)
    {
        if (summary.Envelope is not { } envelope)
        {
            return new EmailEnvelope(null, null, [], null, [], null);
        }

        var recipients = envelope.To.Mailboxes.Concat(envelope.Cc.Mailboxes).Select(mailbox => mailbox.Address).ToList();
        return new EmailEnvelope(
            envelope.MessageId,
            envelope.InReplyTo,
            summary.References?.ToList() ?? [],
            envelope.From.Mailboxes.FirstOrDefault()?.Address,
            recipients,
            envelope.Subject)
        {
            Date = envelope.Date,
        };
    }

    private static string BodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody;
        }

        return message.HtmlBody is { } html ? HtmlText.ToPlainText(html) : string.Empty;
    }

    private async Task<IMailFolder?> ProcessedFolderAsync(ImapClient client, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || client.PersonalNamespaces.Count == 0)
        {
            return null;
        }

        try
        {
            var root = client.GetFolder(client.PersonalNamespaces[0]);
            try
            {
                return await root.GetSubfolderAsync(name, cancellationToken);
            }
            catch (FolderNotFoundException)
            {
                return await root.CreateAsync(name, isMessageFolder: true, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is ImapCommandException or ImapProtocolException or InvalidOperationException)
        {
            // Without the folder, handled replies stay in the inbox marked as seen.
            LogNoProcessedFolder(logger, ex, name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent email {MessageId} to {Recipients}")]
    private static partial void LogSent(ILogger logger, string messageId, string recipients);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail folder {Folder} could not be used; handled replies stay in the inbox")]
    private static partial void LogNoProcessedFolder(ILogger logger, Exception exception, string folder);
}

internal static partial class HtmlText
{
    public static string ToPlainText(string html)
    {
        var withBreaks = BlockEnd().Replace(html, "\n");
        var withoutTags = Tag().Replace(withBreaks, string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex("<(br|/p|/div|/li|/tr|/h[1-6])[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BlockEnd();

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
    private static partial System.Text.RegularExpressions.Regex Tag();
}
