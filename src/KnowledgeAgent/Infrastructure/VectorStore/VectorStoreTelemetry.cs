using System.Diagnostics;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Infrastructure.VectorStore;

internal static class VectorStoreTelemetry
{
    public const string ActivitySourceName = KnowledgeAgentTelemetry.VectorStore;

    public static ActivitySource Source { get; } = new(ActivitySourceName);

    public static Activity? StartOperation(string operation, string system, string database, string table)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity($"{operation} {database}.{table}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("db.system.name", system);
            activity.SetTag("db.namespace", database);
            activity.SetTag("db.collection.name", table);
            activity.SetTag("db.operation.name", operation);
        }

        return activity;
    }

    public static void RecordException(this Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }
}
