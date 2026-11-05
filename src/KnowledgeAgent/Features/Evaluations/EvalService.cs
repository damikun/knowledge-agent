using System.Globalization;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Judge;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalService(
    BuiltinDatasetCatalog catalog,
    ICustomDatasetRepository customDatasets,
    ICustomDatasetService customDatasetService,
    IEvalRunRepository runs,
    EvalRunQueue queue,
    LangfuseEvalClient langfuse,
    LangfuseEvalLinks links,
    IOptions<EvaluationOptions> options,
    IPromptService prompts,
    IChatClientFactory chatClients,
    IModelProfileResolver profiles,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IEvalService
{
    public const int MaxRunListLimit = 200;

    internal const int MaxLangfuseDatasets = 50;
    private readonly ILogger _logger = loggerFactory.CreateLogger<EvalService>();

    public async Task<EvalDatasetListDto> ListDatasetsAsync(CancellationToken cancellationToken = default)
    {
        var builtin = catalog.All;
        var custom = await customDatasets.ListAsync(cancellationToken);
        var remote = new List<LangfuseDataset>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? langfuseError = null;
        if (langfuse.IsConfigured)
        {
            try
            {
                remote.AddRange((await langfuse.ListDatasetsAsync(cancellationToken)).Take(MaxLangfuseDatasets));
                foreach (var dataset in remote.Where(dataset =>
                    builtin.All(local => !local.Name.Equals(dataset.Name, StringComparison.OrdinalIgnoreCase))
                    && custom.All(local => !local.Dataset.Name.Equals(dataset.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    counts[dataset.Name] = (await langfuse.ListItemsAsync(dataset.Name, cancellationToken)).Count(item => item.IsActive);
                }
            }
            catch (LangfuseEvalException ex)
            {
                langfuseError = ex.Message;
            }
        }

        var datasets = new List<EvalDatasetDto>();
        foreach (var dataset in builtin)
        {
            var synced = remote.FirstOrDefault(candidate => candidate.Name.Equals(dataset.Name, StringComparison.OrdinalIgnoreCase));
            datasets.Add(new EvalDatasetDto(
                dataset.Name,
                EvalDatasetSources.Builtin,
                dataset.Description,
                dataset.Items.Count,
                dataset.Corpus.Count,
                dataset.Tags,
                synced?.Id,
                synced is null ? null : await links.DatasetAsync(synced.Id, cancellationToken)));
        }

        foreach (var summary in custom)
        {
            var synced = remote.FirstOrDefault(candidate => candidate.Name.Equals(summary.Dataset.Name, StringComparison.OrdinalIgnoreCase));
            datasets.Add(CustomDatasetService.ToDto(summary, synced?.Id, synced is null ? null : await links.DatasetAsync(synced.Id, cancellationToken)));
        }

        foreach (var dataset in remote.Where(dataset => counts.ContainsKey(dataset.Name)).OrderBy(dataset => dataset.Name, StringComparer.Ordinal))
        {
            datasets.Add(new EvalDatasetDto(
                dataset.Name,
                EvalDatasetSources.Langfuse,
                dataset.Description,
                counts[dataset.Name],
                0,
                [],
                dataset.Id,
                await links.DatasetAsync(dataset.Id, cancellationToken)));
        }

        var (judgeModel, _) = await Judge().DescribeAsync(cancellationToken);
        return new EvalDatasetListDto(datasets, langfuse.IsConfigured, langfuseError, judgeModel is not null, judgeModel);
    }

    public async Task<EvalDatasetSyncResultDto?> SyncDatasetAsync(string name, CancellationToken cancellationToken = default)
    {
        var dataset = catalog.Find(name);
        if (dataset is null)
        {
            return null;
        }

        if (!langfuse.IsConfigured)
        {
            throw new ValidationFailedException("langfuse", "Langfuse is not configured (Langfuse:BaseUrl, Langfuse:PublicKey and Langfuse:SecretKey).");
        }

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

    public async Task<EvalRunSummaryDto> StartRunAsync(StartEvalRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = request.Dataset?.Trim() ?? string.Empty;
        var source = string.IsNullOrWhiteSpace(request.Source) ? EvalDatasetSources.Builtin : request.Source.Trim().ToLowerInvariant();
        var tags = (request.Tags ?? []).Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var maxItems = request.MaxItems;

        if (name.Length == 0)
        {
            errors["dataset"] = "A dataset is required.";
        }

        if (!EvalDatasetSources.IsKnown(source))
        {
            errors["source"] = "source must be 'builtin', 'langfuse' or 'custom'.";
        }

        if (maxItems is < 1)
        {
            errors["maxItems"] = "maxItems must be at least 1.";
        }

        if (request.Name is { Length: > 200 })
        {
            errors["name"] = "name must be at most 200 characters.";
        }

        var profile = string.IsNullOrWhiteSpace(request.Profile) ? null : request.Profile.Trim();
        if (profile is not null)
        {
            try
            {
                var resolved = await profiles.ResolveAsync(profile, cancellationToken: cancellationToken);
                if (resolved.Kind != ModelKind.Chat)
                {
                    errors["profile"] = $"'{profile}' is not a chat profile.";
                }
            }
            catch (ModelNotConfiguredException ex)
            {
                errors["profile"] = ex.Message;
            }
        }

        if (request.Judge)
        {
            var (_, unavailable) = await Judge().DescribeAsync(cancellationToken);
            if (unavailable is not null)
            {
                errors["judge"] = unavailable;
            }
        }

        var total = 0;
        if (errors.Count == 0)
        {
            var items = await LoadItemsAsync(name, source, errors, cancellationToken);
            if (items is not null)
            {
                total = EvalItemFilter.Apply(items, tags, maxItems).Count;
                if (total == 0)
                {
                    errors[tags.Count > 0 ? "tags" : "dataset"] = tags.Count > 0 ? "No items have these tags." : "The dataset has no items.";
                }
                else if (total > options.Value.MaxItemsPerRun)
                {
                    errors["maxItems"] = string.Create(CultureInfo.InvariantCulture, $"A run can have at most {options.Value.MaxItemsPerRun} items; set maxItems.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors.ToDictionary(error => error.Key, error => new[] { error.Value }));
        }

        var now = timeProvider.GetUtcNow();
        var record = new EvalRunRecord
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{name} {now:yyyy-MM-dd HH:mm:ss}" : request.Name.Trim(),
            Dataset = name,
            DatasetSource = source,
            Status = EvalRunStatuses.Queued,
            Judge = request.Judge,
            TotalItems = total,
            RequestJson = EvalRunRecords.Serialize(request with { Dataset = name, Source = source, Tags = tags, Profile = profile }),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await runs.AddAsync(record, cancellationToken);
        await queue.EnqueueAsync(record.Id, cancellationToken);
        return EvalRunRecords.ToSummary(record);
    }

    public async Task<IReadOnlyList<EvalRunSummaryDto>> ListRunsAsync(int limit = 20, string? dataset = null, CancellationToken cancellationToken = default) =>
        (await runs.ListAsync(Math.Clamp(limit, 1, MaxRunListLimit), string.IsNullOrWhiteSpace(dataset) ? null : dataset.Trim(), cancellationToken))
            .Select(EvalRunRecords.ToSummary)
            .ToList();

    public async Task<EvalRunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken = default) =>
        await runs.GetAsync(id, cancellationToken) is { } record ? EvalRunRecords.ToDto(record) : null;

    public async Task<EvalRunSummaryDto?> CancelRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await runs.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.Status == EvalRunStatuses.Queued)
        {
            record.Status = EvalRunStatuses.Cancelled;
            record.CompletedAt = timeProvider.GetUtcNow();
            await runs.UpdateAsync(record, cancellationToken);
        }
        else if (record.Status == EvalRunStatuses.Running && !queue.TryCancel(id))
        {
            // No process is running it any more, so nothing else will close it.
            record.Status = EvalRunStatuses.Cancelled;
            record.CompletedAt = timeProvider.GetUtcNow();
            await runs.UpdateAsync(record, cancellationToken);
        }

        return EvalRunRecords.ToSummary(await runs.GetAsync(id, cancellationToken) ?? record);
    }

    public async Task<EvalRunDeleteResult> DeleteRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await runs.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return EvalRunDeleteResult.NotFound;
        }

        if (!EvalRunStatuses.IsFinished(record.Status))
        {
            return EvalRunDeleteResult.Active;
        }

        return await runs.DeleteAsync(id, cancellationToken) ? EvalRunDeleteResult.Deleted : EvalRunDeleteResult.NotFound;
    }

    private async Task<IReadOnlyList<EvalItem>?> LoadItemsAsync(string name, string source, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        if (source == EvalDatasetSources.Builtin)
        {
            if (catalog.Find(name) is { } dataset)
            {
                return dataset.Items;
            }

            errors["dataset"] = $"There is no builtin dataset '{name}'.";
            return null;
        }

        if (source == EvalDatasetSources.Custom)
        {
            if (await customDatasetService.LoadAsync(name, cancellationToken) is { } custom)
            {
                return custom.Items;
            }

            errors["dataset"] = $"There is no dataset '{name}'.";
            return null;
        }

        if (!langfuse.IsConfigured)
        {
            errors["source"] = "Langfuse is not configured, so Langfuse datasets cannot be run.";
            return null;
        }

        try
        {
            var remote = await langfuse.GetDatasetAsync(name, cancellationToken);
            if (remote is null)
            {
                errors["dataset"] = $"Langfuse has no dataset '{name}'.";
                return null;
            }

            return LangfuseDatasets.ToEvalDataset(remote, await langfuse.ListItemsAsync(name, cancellationToken)).Dataset.Items;
        }
        catch (LangfuseEvalException ex)
        {
            errors["dataset"] = ex.Message;
            return null;
        }
    }

    private EvalJudge Judge() => new(prompts, chatClients, EvalJudge.DefaultProfile, options.Value.LangfuseEnvironment, _logger);
}
