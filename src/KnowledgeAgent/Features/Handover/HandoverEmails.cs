using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Widgets;
using MimeKit;

namespace KnowledgeAgent.Features.Handover;

internal static partial class HandoverEmails
{
    public const string MessageIdDomain = "knowledge-agent";

    // box+ka-token@domain, or the plain mailbox, in which case the [KA-token] in the subject identifies the conversation.
    public static string ReplyAddress(EmailSettings settings, string token)
    {
        var mailbox = settings.ReplyAddress ?? settings.FromAddress!;
        if (!settings.PlusAddressing)
        {
            return mailbox;
        }

        var at = mailbox.LastIndexOf('@');
        return at <= 0 ? mailbox : $"{mailbox[..at]}+ka-{token}{mailbox[at..]}";
    }

    public static string Subject(string token, string title) => $"[KA-{token}] {title}";

    public static string NewMessageId(string token) => $"ka-{token}-{Guid.NewGuid():N}@{MessageIdDomain}";

    public static MimeMessage Transcript(
        EmailSettings settings,
        Handover handover,
        Conversation conversation,
        IReadOnlyList<ConversationMessage> messages,
        WidgetVisitor? visitor,
        string widgetName,
        IReadOnlyList<string> recipients,
        string? link)
    {
        var message = Base(settings, handover, recipients, Subject(handover.Token, $"{widgetName}: {conversation.Title}"));
        var text = new StringBuilder()
            .AppendLine("A visitor asked for a person. Reply to this email to answer them in the chat.")
            .AppendLine()
            .AppendLine($"Visitor: {VisitorLine(visitor)}")
            .AppendLine($"Reason: {handover.Reason ?? "Asked for a person"}");
        if (handover.Summary is { } summary)
        {
            text.AppendLine($"Summary: {summary}");
        }

        if (link is not null)
        {
            text.AppendLine($"Open in the inbox: {link}");
        }

        text.AppendLine().AppendLine("Conversation so far:").AppendLine();
        foreach (var item in messages)
        {
            text.AppendLine($"{Label(item)}: {item.Text}").AppendLine();
        }

        message.Body = new TextPart("plain") { Text = text.ToString() };
        return message;
    }

    public static MimeMessage FollowUp(EmailSettings settings, Handover handover, Conversation conversation, ConversationMessage item, IReadOnlyList<string> recipients)
    {
        var message = Base(settings, handover, recipients, "Re: " + Subject(handover.Token, conversation.Title));
        message.Body = new TextPart("plain") { Text = $"{Label(item)}: {item.Text}\n\nReply to this email to answer in the chat." };
        return message;
    }

    public static MimeMessage ToVisitor(EmailSettings settings, Handover handover, string widgetTitle, ConversationMessage reply, string visitorEmail)
    {
        var message = Base(settings, handover, [visitorEmail], $"Re: {widgetTitle} [KA-{handover.Token}]");
        var html = $"<p>{WebUtility.HtmlEncode(reply.AuthorName ?? "Our team")} replied:</p>"
            + $"<blockquote>{WebUtility.HtmlEncode(reply.Text).Replace("\n", "<br>", StringComparison.Ordinal)}</blockquote>"
            + "<p>Reply to this email or open the chat again to continue.</p>";
        message.Body = new BodyBuilder
        {
            TextBody = $"{reply.AuthorName ?? "Our team"} replied:\n\n{reply.Text}\n\nReply to this email or open the chat again to continue.",
            HtmlBody = html,
        }.ToMessageBody();
        return message;
    }

    public static string? FindToken(EmailEnvelope envelope)
    {
        foreach (var address in envelope.To)
        {
            if (PlusToken().Match(address) is { Success: true } plus)
            {
                return plus.Groups[1].Value.ToLowerInvariant();
            }
        }

        if (envelope.Subject is { } subject && SubjectToken().Match(subject) is { Success: true } tagged)
        {
            return tagged.Groups[1].Value.ToLowerInvariant();
        }

        foreach (var reference in envelope.References.Append(envelope.InReplyTo).OfType<string>())
        {
            if (ThreadToken().Match(reference) is { Success: true } thread)
            {
                return thread.Groups[1].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    public static string ReplyText(string body)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('>') || QuoteHeader().IsMatch(trimmed) || trimmed is "-- " or "--" || trimmed.StartsWith("________________", StringComparison.Ordinal))
            {
                break;
            }

            kept.Add(line.TrimEnd());
        }

        var text = string.Join('\n', kept).Trim();
        return SentFrom().Replace(text, string.Empty).Trim();
    }

    private static MimeMessage Base(EmailSettings settings, Handover handover, IReadOnlyList<string> recipients, string subject)
    {
        var message = new MimeMessage { Subject = subject, MessageId = NewMessageId(handover.Token) };
        message.From.Add(new MailboxAddress(settings.FromName ?? "Knowledge Agent", settings.FromAddress ?? throw new InvalidOperationException("Email has no sender address.")));
        message.ReplyTo.Add(MailboxAddress.Parse(ReplyAddress(settings, handover.Token)));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        if (handover.EmailMessageId is { } first)
        {
            message.InReplyTo = first;
            message.References.Add(first);
        }

        return message;
    }

    private static string VisitorLine(WidgetVisitor? visitor) => visitor switch
    {
        { Name: { } name, Email: { } email } => $"{name} <{email}>",
        { Email: { } email } => email,
        { Name: { } name } => $"{name} (no email address)",
        _ => "Anonymous (no email address)",
    };

    private static string Label(ConversationMessage message) => message.Author switch
    {
        MessageAuthor.Visitor or MessageAuthor.User => "Visitor",
        MessageAuthor.Assistant => "Assistant",
        MessageAuthor.Staff => message.AuthorName ?? "Staff",
        _ => "Note",
    };

    [GeneratedRegex(@"\+ka-([a-z0-9]{12})@", RegexOptions.IgnoreCase)]
    private static partial Regex PlusToken();

    [GeneratedRegex(@"\[KA-([a-z0-9]{12})\]", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectToken();

    [GeneratedRegex(@"ka-([a-z0-9]{12})-[0-9a-f]{32}@", RegexOptions.IgnoreCase)]
    private static partial Regex ThreadToken();

    // "On Tue, 3 Nov 2026 at 10:00, Anna <a@b.c> wrote:", "Am ... schrieb ...:", Outlook's "From: ..." block.
    [GeneratedRegex(@"^(On\s.+wrote:|Am\s.+schrieb.+:|Le\s.+a écrit\s?:|-----\s?Original Message\s?-----|From:\s.+|Von:\s.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeader();

    [GeneratedRegex(@"\n*(Sent from my \w+|Get Outlook for \w+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SentFrom();
}
