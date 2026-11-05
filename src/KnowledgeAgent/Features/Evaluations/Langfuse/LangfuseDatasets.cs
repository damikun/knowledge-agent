using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Evaluations.Datasets;

namespace KnowledgeAgent.Features.Evaluations.Langfuse;

public sealed record LangfuseSyncResult(
    LangfuseDataset Dataset,
    int Created,
    int Updated,
    int Unchanged,
    int Archived,
    IReadOnlyDictionary<string, LangfuseDatasetItem> ItemsByLocalId);

public static class LangfuseDatasets
{
    public const string ItemIdSeparator = ":";
    public const string MetadataSourceValue = "knowledge-agent";

    // Prefixed with the dataset name because Langfuse item ids are unique per project, not per dataset.
    public static string ItemId(string datasetName, string itemId) => datasetName + ItemIdSeparator + itemId;

    public static async Task<LangfuseSyncResult> SyncAsync(LangfuseEvalClient client, EvalDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(dataset);

        var remote = await client.GetDatasetAsync(dataset.Name, cancellationToken)
            ?? await client.CreateDatasetAsync(
                dataset.Name,
                dataset.Description,
                new JsonObject
                {
                    ["source"] = MetadataSourceValue,
                    ["corpus"] = new JsonArray(dataset.Corpus.Select(document => (JsonNode)document.Title).ToArray()),
                },
                cancellationToken);

        var existing = (await client.ListItemsAsync(dataset.Name, cancellationToken)).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var byLocalId = new Dictionary<string, LangfuseDatasetItem>(StringComparer.OrdinalIgnoreCase);
        int created = 0, updated = 0, unchanged = 0, archived = 0;

        foreach (var item in dataset.Items)
        {
            var desired = ToUpsert(dataset.Name, item);

            // Unchanged items are not rewritten, so Langfuse dataset versions only track real edits.
            if (existing.TryGetValue(desired.Id, out var current) && current.IsActive && IsSame(current, desired))
            {
                unchanged++;
                byLocalId[item.Id] = current;
                continue;
            }

            var saved = await client.UpsertItemAsync(desired, cancellationToken);
            byLocalId[item.Id] = saved;
            if (current is null)
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        var localIds = dataset.Items.Select(item => ItemId(dataset.Name, item.Id)).ToHashSet(StringComparer.Ordinal);
        var prefix = dataset.Name + ItemIdSeparator;
        foreach (var stale in existing.Values.Where(item => item.IsActive && item.Id.StartsWith(prefix, StringComparison.Ordinal) && !localIds.Contains(item.Id)))
        {
            await client.UpsertItemAsync(
                new LangfuseDatasetItemUpsert(dataset.Name, stale.Id, stale.Input ?? new JsonObject(), stale.ExpectedOutput, stale.Metadata, "ARCHIVED"),
                cancellationToken);
            archived++;
        }

        return new LangfuseSyncResult(remote, created, updated, unchanged, archived, byLocalId);
    }

    public static LangfuseDatasetItemUpsert ToUpsert(string datasetName, EvalItem item) => new(
        datasetName,
        ItemId(datasetName, item.Id),
        new JsonObject { ["question"] = item.Question },
        ExpectedOutput(item),
        new JsonObject
        {
            ["source"] = MetadataSourceValue,
            ["tags"] = new JsonArray(item.Tags.Select(tag => (JsonNode)tag).ToArray()),
            ["answerable"] = item.Answerable,
        });

    public static JsonObject ExpectedOutput(EvalItem item)
    {
        var expected = new JsonObject();
        if (item.ReferenceAnswer is not null)
        {
            expected["answer"] = item.ReferenceAnswer;
        }

        expected["facts"] = new JsonArray(item.ExpectedFacts.Select(fact => (JsonNode)fact).ToArray());
        expected["sources"] = new JsonArray(item.ExpectedSources.Select(source => (JsonNode)source).ToArray());
        expected["answerable"] = item.Answerable;
        return expected;
    }

    public static (EvalDataset Dataset, IReadOnlyList<string> Warnings) ToEvalDataset(LangfuseDataset dataset, IReadOnlyList<LangfuseDatasetItem> items)
    {
        var warnings = new List<string>();
        var evalItems = new List<EvalItem>();
        var prefix = dataset.Name + ItemIdSeparator;
        foreach (var item in items.Where(item => item.IsActive))
        {
            // The extra keys cover items added to the dataset from chat traces.
            var question = FirstText(item.Input, "question", "message", "query", "input", "text");
            if (string.IsNullOrWhiteSpace(question))
            {
                warnings.Add($"Langfuse item {item.Id} has no question in its input and was skipped.");
                continue;
            }

            var expected = item.ExpectedOutput;
            var metadata = item.Metadata;
            var answerable = Bool(expected, "answerable") ?? Bool(metadata, "answerable") ?? true;
            evalItems.Add(new EvalItem(
                item.Id.StartsWith(prefix, StringComparison.Ordinal) ? item.Id[prefix.Length..] : item.Id,
                question.Trim(),
                answerable,
                answerable ? Strings(expected, "sources", "expectedSources").Concat(Strings(metadata, "sources", "expectedSources")).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [],
                answerable ? Strings(expected, "facts", "expectedFacts").Concat(Strings(metadata, "facts", "expectedFacts")).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [],
                FirstText(expected, "answer", "referenceAnswer", "output", "text"),
                Strings(metadata, "tags"))
            {
                LangfuseItemId = item.Id,
                LangfuseItemVersion = item.Version,
            });
        }

        return (new EvalDataset
        {
            Name = dataset.Name,
            Description = dataset.Description,
            Source = EvalDatasetSources.Langfuse,
            Items = evalItems,
            LangfuseDatasetId = dataset.Id,
        }, warnings);
    }

    private static bool IsSame(LangfuseDatasetItem current, LangfuseDatasetItemUpsert desired) =>
        JsonNode.DeepEquals(current.Input, desired.Input)
        && JsonNode.DeepEquals(current.ExpectedOutput, desired.ExpectedOutput)
        && JsonNode.DeepEquals(current.Metadata, desired.Metadata);

    private static string? FirstText(JsonNode? node, params string[] keys)
    {
        if (node is JsonValue value)
        {
            return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
        }

        if (node is not JsonObject json)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (json[key] is JsonValue candidate && candidate.GetValueKind() == JsonValueKind.String
                && candidate.GetValue<string>() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static bool? Bool(JsonNode? node, string key) =>
        node is JsonObject json && json[key] is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? value.GetValue<bool>()
            : null;

    private static List<string> Strings(JsonNode? node, params string[] keys)
    {
        if (node is not JsonObject json)
        {
            return [];
        }

        foreach (var key in keys)
        {
            if (json[key] is JsonArray array)
            {
                return array
                    .OfType<JsonValue>()
                    .Where(value => value.GetValueKind() == JsonValueKind.String)
                    .Select(value => value.GetValue<string>().Trim())
                    .Where(text => text.Length > 0)
                    .ToList();
            }
        }

        return [];
    }
}
