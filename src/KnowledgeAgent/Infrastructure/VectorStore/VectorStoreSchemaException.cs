namespace KnowledgeAgent.Infrastructure.VectorStore;

internal sealed class VectorStoreSchemaException(string message) : InvalidOperationException(message);
