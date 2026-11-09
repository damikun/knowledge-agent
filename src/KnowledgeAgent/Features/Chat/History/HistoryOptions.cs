namespace KnowledgeAgent.Features.Chat.History;

public enum HistoryMode
{
    // Earlier turns without thinking, with long tool results replaced by short stubs.
    Compact,

    // Earlier turns exactly as they were sent.
    Verbatim,
}

public sealed class HistoryOptions
{
    public const string SectionName = "Agents:History";

    public HistoryMode Mode { get; set; } = HistoryMode.Compact;

    // The last N earlier turns keep their full tool results. Their thinking is dropped all the same.
    public int KeepFullTurns { get; set; }

    public int McpExcerptChars { get; set; } = 1000;

    public int ReportExcerptChars { get; set; } = 1500;

    public int OtherExcerptChars { get; set; } = 600;
}
