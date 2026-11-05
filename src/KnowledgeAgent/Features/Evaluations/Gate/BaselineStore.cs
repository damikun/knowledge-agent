using System.Text;
using System.Text.Json;
using KnowledgeAgent.Features.Evaluations.Datasets;

namespace KnowledgeAgent.Features.Evaluations.Gate;

public static class BaselineStore
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Write(string datasetDirectory, string signature, IReadOnlyDictionary<string, EvalMetricValueDto> metrics, int items, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetDirectory);
        var directory = Path.Combine(datasetDirectory, EvalDatasetLoader.BaselinesFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, signature + ".json");
        var file = new EvalDatasetLoader.BaselineFile
        {
            Signature = signature,
            CreatedAt = createdAt,
            Items = items,
            Metrics = EvalMetrics.All
                .Where(metrics.ContainsKey)
                .ToDictionary(metric => metric, metric => Math.Round(metrics[metric].Mean, 4), StringComparer.Ordinal),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, WriteOptions) + "\n", new UTF8Encoding(false));
        return path;
    }
}
