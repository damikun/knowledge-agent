using System.Text.Json;

namespace KnowledgeAgent.Features.Evaluations;

internal static class EvalRunRecords
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static EvalRunSummaryDto ToSummary(EvalRunRecord record) => new(
        record.Id,
        record.Name,
        record.Dataset,
        record.DatasetSource,
        record.Status,
        record.Judge,
        record.TotalItems,
        record.CompletedItems,
        record.FailedItems,
        record.ChatModel,
        record.EmbeddingModel,
        Deserialize<Dictionary<string, EvalMetricValueDto>>(record.MetricsJson) ?? new Dictionary<string, EvalMetricValueDto>(),
        Deserialize<EvalGateDto>(record.GateJson),
        record.Error,
        record.LangfuseUrl,
        record.CreatedAt,
        record.StartedAt,
        record.CompletedAt);

    public static EvalRunDto ToDto(EvalRunRecord record)
    {
        var request = Deserialize<StartEvalRunRequest>(record.RequestJson);
        var config = Deserialize<EvalRunConfigDto>(record.ConfigJson) ?? new EvalRunConfigDto(
            record.DatasetSource == EvalDatasetSources.Builtin ? Running.EvalTargets.Isolated : Running.EvalTargets.Live,
            request?.Profile ?? string.Empty,
            record.ChatModel,
            record.EmbeddingModel,
            null,
            null,
            null,
            null,
            0,
            0,
            request?.MaxItems,
            request?.Tags ?? []);

        return new EvalRunDto(
            ToSummary(record),
            config,
            Deserialize<List<EvalTagSummaryDto>>(record.ByTagJson) ?? [],
            Deserialize<List<EvalItemResultDto>>(record.ItemsJson) ?? [],
            Deserialize<List<string>>(record.WarningsJson) ?? []);
    }
}
