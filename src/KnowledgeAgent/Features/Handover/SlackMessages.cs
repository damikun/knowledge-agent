using System.Text;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Chat;

namespace KnowledgeAgent.Features.Handover;

internal static class SlackMessages
{
    public const string TakeAction = "ka_take";
    public const string ReturnAction = "ka_return";
    public const string CloseAction = "ka_close";

    // Slack truncates long messages; the inbox always has the whole conversation.
    private const int MaxTranscriptLength = 3500;

    public static (string Text, JsonArray Blocks) Request(HandoverContext context)
    {
        var handover = context.Handover;
        var visitor = context.Visitor switch
        {
            { Name: { } name, Email: { } email } => $"{name} ({email})",
            { Email: { } email } => email,
            { Name: { } name } => name,
            _ => "Anonymous visitor",
        };
        var state = handover.Status switch
        {
            HandoverStatus.Pending => ":wave: *Waiting for someone to take it.* Reply in the thread to answer the visitor.",
            HandoverStatus.Active => $":speech_balloon: *{Escape(handover.Assignee ?? "Someone")} is answering.* Replies in the thread reach the visitor.",
            HandoverStatus.Cancelled => ":leftwards_arrow_with_hook: *The visitor went back to the assistant.*",
            HandoverStatus.Expired => ":hourglass: *Nobody took it in time.* The visitor is back with the assistant; replies in the thread still reach them.",
            _ => $":white_check_mark: *Done* ({Escape(handover.Assignee ?? "staff")}).",
        };
        var details = new StringBuilder()
            .Append($"*{Escape(context.WidgetName)}*: a visitor asked for a person\n")
            .Append($"*Visitor:* {Escape(visitor)}\n")
            .Append($"*Reason:* {Untrusted(handover.Reason ?? "Asked for a person")}");
        if (handover.Summary is { } summary)
        {
            details.Append($"\n*{AiSummaryLabel}:* {Untrusted(summary)}");
        }

        if (context.InboxLink is { } link)
        {
            details.Append($"\n<{link}|Open in the inbox>");
        }

        var blocks = new JsonArray
        {
            Section(details.ToString()),
            new JsonObject { ["type"] = "context", ["elements"] = new JsonArray { new JsonObject { ["type"] = "mrkdwn", ["text"] = state } } },
        };
        if (handover.Status is HandoverStatus.Pending or HandoverStatus.Active)
        {
            var buttons = new JsonArray();
            if (handover.Status == HandoverStatus.Pending)
            {
                buttons.Add(Button("Take it", TakeAction, handover.Id, style: "primary"));
            }

            buttons.Add(Button("Back to the assistant", ReturnAction, handover.Id));
            buttons.Add(Button("Close", CloseAction, handover.Id, style: "danger"));
            blocks.Add(new JsonObject { ["type"] = "actions", ["elements"] = buttons });
        }

        return ($"{context.WidgetName}: a visitor asked for a person", blocks);
    }

    public static string Transcript(HandoverContext context)
    {
        var text = new StringBuilder("*Conversation so far*\n");
        foreach (var message in context.Conversation.Messages.OrderBy(item => item.Sequence))
        {
            text.Append($"*{Label(message)}:* {Text(message)}\n");
        }

        var transcript = text.ToString();
        return transcript.Length > MaxTranscriptLength ? transcript[..MaxTranscriptLength] + "\n_(shortened, the inbox has the whole conversation)_" : transcript;
    }

    public static string Relay(ConversationMessage message, string via) => message.Author == MessageAuthor.Staff
        ? $"*{Escape(message.AuthorName ?? "Staff")}* ({via}): {Escape(message.Text)}"
        : $"*{Label(message)}:* {Text(message)}";

    public const string AiSummaryLabel = "AI summary (unverified)";

    // The model's words, defanged so its links are not clickable.
    private static string Untrusted(string text) => LinkDefang.ForSlack(Escape(text));

    private static string Text(ConversationMessage message) => message.Author == MessageAuthor.Assistant ? Untrusted(message.Text) : Escape(message.Text);

    // Slack's three control characters.
    public static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    public static string Unescape(string text) =>
        text.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

    private static string Label(ConversationMessage message) => message.Author switch
    {
        MessageAuthor.Visitor or MessageAuthor.User => "Visitor",
        MessageAuthor.Assistant => "Assistant",
        MessageAuthor.Staff => message.AuthorName ?? "Staff",
        _ => "Note",
    };

    private static JsonObject Section(string markdown) => new()
    {
        ["type"] = "section",
        ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = markdown },
    };

    private static JsonObject Button(string text, string actionId, Guid handoverId, string? style = null)
    {
        var button = new JsonObject
        {
            ["type"] = "button",
            ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = text },
            ["action_id"] = actionId,
            ["value"] = handoverId.ToString(),
        };
        if (style is not null)
        {
            button["style"] = style;
        }

        return button;
    }
}
