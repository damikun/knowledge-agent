namespace KnowledgeAgent.Features.Chat.Live;

public abstract record LiveMessage(Guid ConversationId);

public sealed record MessagesStored(Guid ConversationId, int FirstSequence) : LiveMessage(ConversationId);

public sealed record StatusChanged(Guid ConversationId, ConversationStatus Status) : LiveMessage(ConversationId);
