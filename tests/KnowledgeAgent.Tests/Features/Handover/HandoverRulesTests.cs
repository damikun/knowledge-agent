using KnowledgeAgent.Features.Handover;
using KnowledgeAgent.Features.Integrations;
using KnowledgeAgent.Features.Widgets;

namespace KnowledgeAgent.Tests.Features.Handover;

public sealed class HandoverRulesTests
{
    private static readonly EmailSettings Email = new()
    {
        Enabled = true,
        SmtpHost = "smtp.example.com",
        FromAddress = "help@example.com",
        SupportAddress = "team@example.com",
        ReplyAddress = "replies@example.com",
        ImapHost = "imap.example.com",
        ImapUser = "replies",
    };

    private static readonly SlackSettings Slack = new() { Enabled = true, BotToken = "xoxb-1", AppToken = "xapp-1", ChannelId = "C0123456789" };

    [Fact]
    public void Replies_go_to_a_plus_address_that_names_the_conversation()
    {
        HandoverEmails.ReplyAddress(Email, "abc123def456").ShouldBe("replies+ka-abc123def456@example.com");
        HandoverEmails.ReplyAddress(Email with { PlusAddressing = false }, "abc123def456").ShouldBe("replies@example.com");
        HandoverEmails.Subject("abc123def456", "Refunds").ShouldBe("[KA-abc123def456] Refunds");
    }

    [Fact]
    public void A_reply_is_matched_by_its_address_subject_or_thread()
    {
        var token = "abc123def456";
        HandoverEmails.FindToken(Envelope(to: ["replies+ka-ABC123DEF456@example.com"])).ShouldBe(token);
        HandoverEmails.FindToken(Envelope(subject: "RE: AW: [KA-abc123def456] Refunds")).ShouldBe(token);
        HandoverEmails.FindToken(Envelope(references: [HandoverEmails.NewMessageId(token)])).ShouldBe(token);
        HandoverEmails.FindToken(Envelope(inReplyTo: HandoverEmails.NewMessageId(token))).ShouldBe(token);
        HandoverEmails.FindToken(Envelope(subject: "Newsletter", to: ["replies@example.com"])).ShouldBeNull();
    }

    [Theory]
    [InlineData("Thanks, the refund is on its way.\n\nOn Tue, 3 Nov 2026 at 10:00, Help <help@example.com> wrote:\n> Earlier text")]
    [InlineData("Thanks, the refund is on its way.\r\n\r\n-----Original Message-----\r\nFrom: Help")]
    [InlineData("Thanks, the refund is on its way.\n-- \nAnna Novak\nSupport lead")]
    [InlineData("Thanks, the refund is on its way.\n\nSent from my iPhone")]
    [InlineData("Thanks, the refund is on its way.\n\nFrom: Help <help@example.com>\nSent: Tuesday")]
    [InlineData("Thanks, the refund is on its way.\n________________________________\nVon: Help")]
    public void Only_the_new_text_of_a_reply_is_kept(string body) =>
        HandoverEmails.ReplyText(body).ShouldBe("Thanks, the refund is on its way.");

    [Fact]
    public void Requests_go_to_the_widget_channel_and_fall_back_to_what_is_set_up()
    {
        var slackPolicy = new WidgetHandover { Channel = WidgetChoices.HandoverSlack };
        HandoverService.ChooseChannel(slackPolicy, Email, Slack, isOpen: true).ShouldBe(WidgetChoices.HandoverSlack);
        HandoverService.ChooseChannel(slackPolicy, Email, Slack, isOpen: false).ShouldBe(WidgetChoices.HandoverEmail, "outside opening hours");
        HandoverService.ChooseChannel(slackPolicy, Email, Slack with { Enabled = false }, isOpen: true).ShouldBe(WidgetChoices.HandoverEmail);
        HandoverService.ChooseChannel(slackPolicy, Email with { Enabled = false }, Slack with { Enabled = false }, isOpen: true).ShouldBe(WidgetChoices.HandoverPortal);
        HandoverService.ChooseChannel(new WidgetHandover { Channel = WidgetChoices.HandoverEmail }, Email with { SupportAddress = null }, Slack, isOpen: true)
            .ShouldBe(WidgetChoices.HandoverPortal, "nobody to email");
        HandoverService.ChooseChannel(new WidgetHandover { Channel = WidgetChoices.HandoverEmail, EmailTo = ["desk@example.com"] }, Email with { SupportAddress = null }, Slack, isOpen: true)
            .ShouldBe(WidgetChoices.HandoverEmail);
        HandoverService.ChooseChannel(new WidgetHandover(), Email, Slack, isOpen: true).ShouldBe(WidgetChoices.HandoverPortal);
    }

    [Fact]
    public void Visitors_are_told_what_happens_next()
    {
        HandoverService.Expectation(WidgetChoices.HandoverEmail, isOpen: true, "ana@example.org").ShouldContain("ana@example.org");
        HandoverService.Expectation(WidgetChoices.HandoverEmail, isOpen: true, null).ShouldContain("leave your email");
        HandoverService.Expectation(WidgetChoices.HandoverSlack, isOpen: true, null).ShouldContain("asked a colleague");
        HandoverService.Expectation(WidgetChoices.HandoverPortal, isOpen: false, null).ShouldContain("away right now");
    }

    [Fact]
    public void Only_staff_addresses_can_answer_for_the_team()
    {
        MailPollJob.IsStaff("anna@example.com", Email, widget: null).ShouldBeTrue("same domain as the support address");
        MailPollJob.IsStaff("mallory@evil.test", Email, widget: null).ShouldBeFalse();
        MailPollJob.IsStaff("anna@example.com", Email with { StaffAddresses = ["boss@example.com"] }, widget: null).ShouldBeFalse("configured addresses replace the default");
        MailPollJob.IsStaff("boss@example.com", Email with { StaffAddresses = ["boss@example.com"] }, widget: null).ShouldBeTrue();
        MailPollJob.IsStaff("lead@partner.test", Email with { StaffDomains = ["partner.test"] }, widget: null).ShouldBeTrue();
        MailPollJob.IsStaff(null, Email, widget: null).ShouldBeFalse();
    }

    [Fact]
    public void Slack_text_is_escaped_and_replies_are_unescaped()
    {
        SlackMessages.Escape("<script> & co").ShouldBe("&lt;script&gt; &amp; co");
        SlackMessages.Unescape("1 &lt; 2 &amp;&amp; 3 &gt; 2").ShouldBe("1 < 2 && 3 > 2");
    }

    [Fact]
    public void A_reply_is_deduplicated_by_its_message_id_when_it_has_one()
    {
        MailPollJob.InboundDedupeKey(new InboundEmail(Envelope(), "Anna", "Thanks!")).ShouldBe("email:m1@mail.test");
    }

    [Fact]
    public void A_reply_without_a_message_id_gets_a_stable_sha256_key_of_sender_subject_date_and_text()
    {
        var sent = new DateTimeOffset(2026, 11, 5, 10, 30, 0, TimeSpan.FromHours(1));
        InboundEmail Mail(string text, string from = "anna@example.com", DateTimeOffset? date = null) =>
            new(new EmailEnvelope(null, null, [], from, ["replies@example.com"], "Re: Your request [KA-abc]") { Date = date ?? sent }, "Anna", text);

        var key = MailPollJob.InboundDedupeKey(Mail("Thanks, that helped.\r\nAnna"));

        // A fixed value: the key must not change between processes or instances, as string.GetHashCode does.
        key.ShouldBe("email:sha256:" + ExpectedHash);
        MailPollJob.InboundDedupeKey(Mail("Thanks, that helped.\nAnna\n", from: " Anna@Example.com")).ShouldBe(key, "line endings, outer space and the address case do not matter");
        MailPollJob.InboundDedupeKey(Mail("Thanks, that helped.\r\nAnna", date: sent.ToUniversalTime())).ShouldBe(key, "the same instant in another zone");
        MailPollJob.InboundDedupeKey(Mail("Thanks, that did not help.")).ShouldNotBe(key);
        MailPollJob.InboundDedupeKey(Mail("Thanks, that helped.\r\nAnna", date: sent.AddMinutes(1))).ShouldNotBe(key);
        MailPollJob.InboundDedupeKey(Mail("Thanks, that helped.\r\nAnna", from: "bob@example.com")).ShouldNotBe(key);
    }

    [Fact]
    public void The_email_and_the_Slack_request_link_to_the_inbox_at_the_public_address()
    {
        var conversation = new KnowledgeAgent.Features.Chat.Conversation { Title = "Refunds" };
        var handover = new KnowledgeAgent.Features.Handover.Handover { ConversationId = conversation.Id, Token = "abc123def456", Status = HandoverStatus.Pending };
        var context = new HandoverContext(handover, conversation, null, null, Email, Slack, "https://support.example.com/help");
        var link = $"https://support.example.com/help/inbox/{conversation.Id}";

        context.InboxLink.ShouldBe(link);
        var mail = HandoverEmails.Transcript(Email, handover, conversation, [], null, context.WidgetName, ["team@example.com"], context.InboxLink);
        mail.TextBody!.ShouldContain($"Open in the inbox: {link}");
        SlackMessages.Request(context).Blocks[0]!["text"]!["text"]!.GetValue<string>().ShouldEndWith($"\n<{link}|Open in the inbox>");

        var unknown = context with { PublicUrl = null };
        unknown.InboxLink.ShouldBeNull();
        SlackMessages.Request(unknown).Blocks[0]!["text"]!["text"]!.GetValue<string>().ShouldNotContain("Open in the inbox");
    }

    private const string ExpectedHash = "02180e9815861d36fb2d635f9d16b8e833e84ac5b811c6e65f36a47655d6d73f";

    private static EmailEnvelope Envelope(
        IReadOnlyList<string>? to = null,
        string? subject = null,
        IReadOnlyList<string>? references = null,
        string? inReplyTo = null) =>
        new("m1@mail.test", inReplyTo, references ?? [], "anna@example.com", to ?? ["replies@example.com"], subject);
}
