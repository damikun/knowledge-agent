using KnowledgeAgent.Features.Chat;

namespace KnowledgeAgent.Features.Widgets;

// Built in one place, so a playground test gets exactly what visitors get.
internal static class WidgetChatCaller
{
    public static ChatCaller ForVisitor(Widget widget, WidgetVisitor visitor) =>
        new(ConversationChannel.Widget, visitor.Name, widget.Id, visitor.Id, HandoverAvailable: widget.Settings.Behavior.HandoverEnabled,
            FeedbackAvailable: widget.Settings.Behavior.FeedbackEnabled);

    public static ChatCaller ForPreview(Widget widget, WidgetVisitor visitor) =>
        new(ConversationChannel.Portal, visitor.Name, widget.Id, visitor.Id, HandoverAvailable: widget.Settings.Behavior.HandoverEnabled,
            FeedbackAvailable: widget.Settings.Behavior.FeedbackEnabled);

    public static ChatCaller ForTest(Widget widget, string? staffName, string? staffId = null) =>
        new(ConversationChannel.Portal, staffName, widget.Id, HandoverAvailable: widget.Settings.Behavior.HandoverEnabled, StaffId: staffId,
            FeedbackAvailable: widget.Settings.Behavior.FeedbackEnabled);
}
