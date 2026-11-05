using System.Globalization;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Infrastructure.Web;

namespace KnowledgeAgent.Features.Evaluations.Datasets;

public interface ICustomDatasetService
{
    Task<EvalDatasetDto> CreateAsync(CreateEvalDatasetRequest request, CancellationToken cancellationToken = default);

    Task<EvalDatasetDto?> UpdateAsync(Guid id, UpdateEvalDatasetRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvalDatasetItemDto>?> ListItemsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EvalDatasetItemDto?> AddItemAsync(Guid id, SaveEvalItemRequest request, CancellationToken cancellationToken = default);

    Task<EvalDatasetItemDto?> UpdateItemAsync(Guid id, Guid itemId, SaveEvalItemRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken = default);

    Task<ImportEvalItemsResultDto?> ImportItemsAsync(Guid id, ImportEvalItemsRequest request, CancellationToken cancellationToken = default);

    Task<int?> DeleteItemsAsync(Guid id, DeleteEvalItemsRequest request, CancellationToken cancellationToken = default);

    Task<EvalDatasetSyncResultDto?> SyncAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EvalDataset?> LoadAsync(string name, CancellationToken cancellationToken = default);
}

internal sealed class CustomDatasetService(
    ICustomDatasetRepository datasets,
    BuiltinDatasetCatalog catalog,
    LangfuseEvalClient langfuse,
    LangfuseEvalLinks links,
    TimeProvider timeProvider) : ICustomDatasetService
{
    public const int MaxItems = 2000;
    public const int MaxBatchItems = 1000;
    private const int MaxTurns = 20;
    private const int MaxListEntries = 50;
    private const int MaxFactLength = 1000;
    private const int MaxSourceLength = 500;
    private const int MaxTags = 20;
    private const int MaxTagLength = 50;

    public async Task<EvalDatasetDto> CreateAsync(CreateEvalDatasetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = await ValidNameAsync(request.Name, null, cancellationToken);
        var description = ValidDescription(request.Description);
        var now = timeProvider.GetUtcNow();
        var dataset = new CustomDatasetRecord { Name = name, Description = description, CreatedAt = now, UpdatedAt = now };

        IReadOnlyList<EvalItem> copied = [];
        if (!string.IsNullOrWhiteSpace(request.CopyFrom))
        {
            var source = string.IsNullOrWhiteSpace(request.CopyFromSource) ? EvalDatasetSources.Builtin : request.CopyFromSource.Trim().ToLowerInvariant();
            var original = await FindToCopyAsync(request.CopyFrom.Trim(), source, cancellationToken)
                ?? throw new ValidationFailedException("copyFrom", $"There is no {source} dataset '{request.CopyFrom.Trim()}'.");
            if (original.Items.Count > MaxItems)
            {
                throw new ValidationFailedException("copyFrom", $"A dataset can have at most {MaxItems} questions; '{original.Name}' has {original.Items.Count}.");
            }

            copied = original.Items;
            dataset.CopiedFrom = $"{source}:{original.Name}";
            dataset.Description ??= original.Description;
        }

        // A microsecond apart, the precision PostgreSQL keeps, so copied questions keep their order.
        var items = copied.Select((item, index) => new CustomDatasetItemRecord
            {
                Question = item.Question,
                ReferenceAnswer = item.ReferenceAnswer,
                ExpectedFacts = item.ExpectedFacts.ToList(),
                ExpectedSources = item.ExpectedSources.ToList(),
                Answerable = item.Answerable,
                Tags = item.Tags.ToList(),
                Source = CustomDatasetItemSources.Copy,
                History = item.History.Count == 0 ? null : new EvalItemHistory { Turns = item.History.ToList() },
                CreatedAt = now.AddTicks(index * TimeSpan.TicksPerMicrosecond),
                UpdatedAt = now,
            })
            .ToList();

        if (!await datasets.AddAsync(dataset, items, cancellationToken))
        {
            throw NameTaken();
        }

        return ToDto(new CustomDatasetSummary(dataset, items.Count, Tags(items)));
    }

    public async Task<EvalDatasetDto?> UpdateAsync(Guid id, UpdateEvalDatasetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dataset = await datasets.GetAsync(id, cancellationToken);
        if (dataset is null)
        {
            return null;
        }

        if (request.Name is not null)
        {
            dataset.Name = await ValidNameAsync(request.Name, id, cancellationToken);
        }

        if (request.Description is not null)
        {
            dataset.Description = ValidDescription(request.Description);
        }

        if (!await datasets.UpdateAsync(dataset, cancellationToken))
        {
            throw NameTaken();
        }

        var items = await datasets.ListItemsAsync(id, cancellationToken);
        return ToDto(new CustomDatasetSummary(dataset, items.Count, Tags(items)));
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => datasets.DeleteAsync(id, cancellationToken);

    public async Task<IReadOnlyList<EvalDatasetItemDto>?> ListItemsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await datasets.GetAsync(id, cancellationToken) is null
            ? null
            : (await datasets.ListItemsAsync(id, cancellationToken)).Select(ToDto).ToList();

    public async Task<EvalDatasetItemDto?> AddItemAsync(Guid id, SaveEvalItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await datasets.GetAsync(id, cancellationToken) is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var item = Apply(new CustomDatasetItemRecord { DatasetId = id, CreatedAt = now }, request, now);
        if (await datasets.CountItemsAsync(id, cancellationToken) >= MaxItems)
        {
            throw new ValidationFailedException("question", $"A dataset can have at most {MaxItems} questions. Create another dataset.");
        }

        try
        {
            await datasets.AddItemAsync(item, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        return ToDto(item);
    }

    public async Task<EvalDatasetItemDto?> UpdateItemAsync(Guid id, Guid itemId, SaveEvalItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = await datasets.GetItemAsync(id, itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        Apply(
            item,
            request with { ConversationId = item.ConversationId, MessageId = item.MessageId, Source = CustomDatasetItemSources.Of(item) },
            timeProvider.GetUtcNow());
        return await datasets.UpdateItemAsync(item, cancellationToken) ? ToDto(item) : null;
    }

    public Task<bool> DeleteItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken = default) =>
        datasets.DeleteItemAsync(id, itemId, cancellationToken);

    public async Task<ImportEvalItemsResultDto?> ImportItemsAsync(Guid id, ImportEvalItemsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await datasets.GetAsync(id, cancellationToken) is null)
        {
            return null;
        }

        var rows = request.Items ?? [];
        if (rows.Count == 0)
        {
            throw new ValidationFailedException("items", "Add at least one question.");
        }

        if (rows.Count > MaxBatchItems)
        {
            throw new ValidationFailedException("items", $"At most {MaxBatchItems} questions in one upload. Split the file.");
        }

        var existing = await datasets.ListItemsAsync(id, cancellationToken);
        var seen = existing.Select(DuplicateKey).ToHashSet(StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var created = new List<CustomDatasetItemRecord>();
        var skipped = new List<int>();
        for (var index = 0; index < rows.Count; index++)
        {
            var prefix = $"items[{index}].";
            if (rows[index] is not { } row)
            {
                errors[prefix + "question"] = ["The row is empty."];
                continue;
            }

            // Distinct timestamps keep the rows in file order (a microsecond apart, the precision PostgreSQL keeps).
            var item = new CustomDatasetItemRecord { DatasetId = id, CreatedAt = now.AddTicks(index * TimeSpan.TicksPerMicrosecond) };
            if (!TryApply(item, row with { Source = row.Source ?? CustomDatasetItemSources.Upload }, now, prefix, errors))
            {
                continue;
            }

            if (!seen.Add(DuplicateKey(item)))
            {
                skipped.Add(index);
                continue;
            }

            created.Add(item);
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        if (existing.Count + created.Count > MaxItems)
        {
            throw new ValidationFailedException(
                "items",
                $"A dataset can have at most {MaxItems} questions; this one has {existing.Count} and the upload adds {created.Count}. Create another dataset.");
        }

        if (created.Count > 0)
        {
            try
            {
                await datasets.AddItemsAsync(id, created, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        return new ImportEvalItemsResultDto(created.Count, skipped.Count, skipped, created.Select(ToDto).ToList());
    }

    public async Task<int?> DeleteItemsAsync(Guid id, DeleteEvalItemsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await datasets.GetAsync(id, cancellationToken) is null)
        {
            return null;
        }

        var ids = (request.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ValidationFailedException("ids", "Choose at least one question.");
        }

        if (ids.Count > MaxItems)
        {
            throw new ValidationFailedException("ids", $"At most {MaxItems} questions at a time.");
        }

        return await datasets.DeleteItemsAsync(id, ids, cancellationToken);
    }

    public async Task<EvalDatasetSyncResultDto?> SyncAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await datasets.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!langfuse.IsConfigured)
        {
            throw new ValidationFailedException("langfuse", "Langfuse is not configured (Langfuse:BaseUrl, Langfuse:PublicKey and Langfuse:SecretKey).");
        }

        var dataset = ToEvalDataset(record, await datasets.ListItemsAsync(id, cancellationToken));
        var result = await LangfuseDatasets.SyncAsync(langfuse, dataset, cancellationToken);
        return new EvalDatasetSyncResultDto(
            dataset.Name,
            result.Dataset.Id,
            result.Created,
            result.Updated,
            result.Unchanged,
            result.Archived,
            await links.DatasetAsync(result.Dataset.Id, cancellationToken));
    }

    public async Task<EvalDataset?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var record = await datasets.FindByNameAsync(name, cancellationToken);
        return record is null ? null : ToEvalDataset(record, await datasets.ListItemsAsync(record.Id, cancellationToken));
    }

    internal static EvalDatasetDto ToDto(CustomDatasetSummary summary, string? langfuseDatasetId = null, string? langfuseUrl = null) => new(
        summary.Dataset.Name,
        EvalDatasetSources.Custom,
        summary.Dataset.Description,
        summary.ItemCount,
        0,
        summary.Tags,
        langfuseDatasetId,
        langfuseUrl,
        summary.Dataset.Id,
        summary.Dataset.CopiedFrom,
        summary.Dataset.UpdatedAt);

    private static EvalDatasetItemDto ToDto(CustomDatasetItemRecord item) => new(
        item.Id,
        item.Question,
        item.ReferenceAnswer,
        item.ExpectedFacts,
        item.ExpectedSources,
        item.Answerable,
        item.Tags,
        item.ConversationId,
        item.MessageId,
        item.CreatedAt,
        item.UpdatedAt,
        CustomDatasetItemSources.Of(item),
        item.History?.Turns ?? []);

    // The item id is the record's, so Langfuse keeps one item per question across renames of its text.
    private static EvalDataset ToEvalDataset(CustomDatasetRecord record, IReadOnlyList<CustomDatasetItemRecord> items) => new()
    {
        Name = record.Name,
        Description = record.Description,
        Source = EvalDatasetSources.Custom,
        Items = items.Select(item => new EvalItem(
                item.Id.ToString("N", CultureInfo.InvariantCulture),
                item.Question,
                item.Answerable,
                item.Answerable ? item.ExpectedSources : [],
                item.Answerable ? item.ExpectedFacts : [],
                item.ReferenceAnswer,
                item.Tags)
            {
                History = item.History?.Turns ?? [],
            })
            .ToList(),
    };

    private async Task<EvalDataset?> FindToCopyAsync(string name, string source, CancellationToken cancellationToken)
    {
        switch (source)
        {
            case EvalDatasetSources.Builtin:
                return catalog.Find(name);
            case EvalDatasetSources.Custom:
                return await LoadAsync(name, cancellationToken);
            case EvalDatasetSources.Langfuse:
                if (!langfuse.IsConfigured)
                {
                    throw new ValidationFailedException("copyFromSource", "Langfuse is not configured, so its datasets cannot be copied.");
                }

                try
                {
                    var remote = await langfuse.GetDatasetAsync(name, cancellationToken);
                    return remote is null ? null : LangfuseDatasets.ToEvalDataset(remote, await langfuse.ListItemsAsync(remote.Name, cancellationToken)).Dataset;
                }
                catch (LangfuseEvalException ex)
                {
                    throw new ValidationFailedException("copyFrom", ex.Message);
                }

            default:
                throw new ValidationFailedException("copyFromSource", "copyFromSource must be builtin, langfuse or custom.");
        }
    }

    private async Task<string> ValidNameAsync(string? name, Guid? self, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException("name", "A name is required.");
        }

        if (trimmed.Length > CustomDatasetRecord.MaxNameLength)
        {
            throw new ValidationFailedException("name", $"The name can have at most {CustomDatasetRecord.MaxNameLength} characters.");
        }

        if (!trimmed.All(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '.'))
        {
            throw new ValidationFailedException("name", "Use letters, digits, spaces, dashes, underscores and dots.");
        }

        // Runs and Langfuse know a dataset by its name, so it must not be that of a built-in one.
        if (catalog.Find(trimmed) is not null)
        {
            throw new ValidationFailedException("name", $"'{trimmed}' is the name of a built-in dataset. Choose another one.");
        }

        if (await datasets.FindByNameAsync(trimmed, cancellationToken) is { } other && other.Id != self)
        {
            throw NameTaken();
        }

        return trimmed;
    }

    private static string? ValidDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (trimmed is { Length: > CustomDatasetRecord.MaxDescriptionLength })
        {
            throw new ValidationFailedException("description", $"The description can have at most {CustomDatasetRecord.MaxDescriptionLength} characters.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static CustomDatasetItemRecord Apply(CustomDatasetItemRecord item, SaveEvalItemRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!TryApply(item, request, now, string.Empty, errors))
        {
            throw new ValidationFailedException(errors);
        }

        return item;
    }

    private static bool TryApply(
        CustomDatasetItemRecord item,
        SaveEvalItemRequest request,
        DateTimeOffset now,
        string prefix,
        Dictionary<string, string[]> errors)
    {
        var before = errors.Count;
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            errors[prefix + "question"] = ["A question is required."];
        }
        else if (question.Length > CustomDatasetItemRecord.MaxQuestionLength)
        {
            errors[prefix + "question"] = [$"The question can have at most {CustomDatasetItemRecord.MaxQuestionLength} characters."];
        }

        var answer = request.ReferenceAnswer?.Trim();
        if (answer is { Length: > CustomDatasetItemRecord.MaxAnswerLength })
        {
            errors[prefix + "referenceAnswer"] = [$"The expected answer can have at most {CustomDatasetItemRecord.MaxAnswerLength} characters."];
        }

        var facts = Entries(request.ExpectedFacts, MaxListEntries, MaxFactLength, prefix + "expectedFacts", "facts", errors);
        var sources = Entries(request.ExpectedSources, MaxListEntries, MaxSourceLength, prefix + "expectedSources", "sources", errors);
        var tags = Entries(request.Tags, MaxTags, MaxTagLength, prefix + "tags", "tags", errors);
        var source = request.Source?.Trim().ToLowerInvariant() ?? (request.ConversationId is null ? CustomDatasetItemSources.Manual : CustomDatasetItemSources.Conversation);
        if (!CustomDatasetItemSources.All.Contains(source))
        {
            errors[prefix + "source"] = ["The source must be manual, conversation, upload or copy."];
        }

        var history = History(request.History, prefix + "history", errors);
        if (errors.Count > before)
        {
            return false;
        }

        item.Question = question;
        item.ReferenceAnswer = string.IsNullOrEmpty(answer) ? null : answer;
        item.ExpectedFacts = facts;
        item.ExpectedSources = sources;
        item.Answerable = request.Answerable;
        item.Tags = tags;
        item.ConversationId = request.ConversationId;
        item.MessageId = request.MessageId;
        item.Source = source;
        item.History = history.Count == 0 ? null : new EvalItemHistory { Turns = history };
        item.UpdatedAt = now;
        return true;
    }

    private static List<EvalTurn> History(IReadOnlyList<EvalTurn>? turns, string field, Dictionary<string, string[]> errors)
    {
        var history = (turns ?? [])
            .Where(turn => turn is not null && !string.IsNullOrWhiteSpace(turn.Content))
            .Select(turn => new EvalTurn(turn.Role?.Trim().ToLowerInvariant() ?? string.Empty, turn.Content.Trim()))
            .ToList();
        if (history.Count > MaxTurns)
        {
            errors[field] = [$"At most {MaxTurns} earlier turns."];
        }
        else if (history.Any(turn => turn.Role is not (EvalTurnRoles.User or EvalTurnRoles.Assistant)))
        {
            errors[field] = ["Each earlier turn is from the user or the assistant."];
        }
        else if (history.Any(turn => turn.Content.Length > CustomDatasetItemRecord.MaxAnswerLength))
        {
            errors[field] = [$"Each earlier turn can have at most {CustomDatasetItemRecord.MaxAnswerLength} characters."];
        }

        return history;
    }

    private static string DuplicateKey(CustomDatasetItemRecord item) =>
        string.Join(
            "\n",
            (item.History?.Turns ?? []).Where(turn => turn.Role == EvalTurnRoles.User).Select(turn => turn.Content).Append(item.Question)
                .Select(text => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant()));

    private static List<string> Entries(IReadOnlyList<string>? values, int maxCount, int maxLength, string field, string noun, Dictionary<string, string[]> errors)
    {
        var entries = (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (entries.Count > maxCount)
        {
            errors[field] = [$"At most {maxCount} {noun}."];
        }
        else if (entries.Any(entry => entry.Length > maxLength))
        {
            errors[field] = [$"Each of the {noun} can have at most {maxLength} characters."];
        }

        return entries;
    }

    private static List<string> Tags(IEnumerable<CustomDatasetItemRecord> items) =>
        items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private static ValidationFailedException NameTaken() => new("name", "Another dataset has this name. Choose another one.");
}
