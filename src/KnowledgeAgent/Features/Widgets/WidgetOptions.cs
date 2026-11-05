namespace KnowledgeAgent.Features.Widgets;

public sealed class WidgetOptions
{
    public const string SectionName = "Widgets";

    public string AssetsPath { get; set; } = "wwwroot/widget";

    public int VisitorTokenLifetimeDays { get; set; } = 30;

    public int PreviewTokenLifetimeMinutes { get; set; } = 60;

    public int SessionsPerMinutePerIp { get; set; } = 20;
    public int MessagesPerMinutePerIp { get; set; } = 30;
}
