namespace KnowledgeAgent.Features.Help;

// Flags only: the help never sees the data itself.
public sealed record HelpSetupDto(bool ChatModel, bool EmbeddingModel, bool Sources, bool Agent, bool Widget);

public sealed record HelpStatusDto(bool Enabled, bool CanManage, bool ModelReady, HelpSetupDto? Setup);

public sealed record SaveHelpSettingsRequest(bool Enabled);

public sealed record HelpHistoryMessage(string Role, string Text);

public sealed record HelpChatRequest(string Message, string? Page = null, IReadOnlyList<HelpHistoryMessage>? History = null, Guid? SessionId = null);
