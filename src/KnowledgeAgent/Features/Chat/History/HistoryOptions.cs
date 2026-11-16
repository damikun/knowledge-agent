namespace KnowledgeAgent.Features.Chat.History;

public enum HistoryMode
{
    Compact,

    Verbatim,
}

public sealed class HistoryOptions
{
    public const string SectionName = "Agents:History";

    public HistoryMode Mode { get; set; } = HistoryMode.Compact;

    public int KeepFullTurns { get; set; }

    public int McpExcerptChars { get; set; } = 1000;

    public int ReportExcerptChars { get; set; } = 1500;

    public int OtherExcerptChars { get; set; } = 600;
}
