namespace KnowledgeAgent.Features.Chat.Live;

// What one instance tells the others. Never the source of truth: a lost one is caught up from the database.
public abstract record LiveMessage(Guid ConversationId);

public sealed record MessagesStored(Guid ConversationId, int FirstSequence) : LiveMessage(ConversationId);

public sealed record StatusChanged(Guid ConversationId, ConversationStatus Status) : LiveMessage(ConversationId);
