using ClickHouse.Driver;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal static class VectorStoreErrors
{
    private static readonly HashSet<int> PermanentClickHouseErrorCodes = [62, 115, 164, 192, 497, 516];

    public static bool IsTransient(Exception exception) => exception switch
    {
        VectorStoreSchemaException => false,
        ArgumentException => false,
        ClickHouseServerException server => !PermanentClickHouseErrorCodes.Contains(server.ErrorCode),
        _ => true,
    };
}
