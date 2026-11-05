using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Gate;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Evaluations.Reporting;
using KnowledgeAgent.Features.Evaluations.Running;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Evaluations;

internal sealed class EvalRunExecutor(
    IServiceProvider services,
    IConfiguration configuration,
    BuiltinDatasetCatalog catalog,
    LangfuseEvalClient langfuse,
    LangfuseEvalLinks links,
    IOptions<EvaluationOptions> options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<EvalRunExecutor>();

    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken, CancellationToken stoppingToken)
    {
        var record = await GetAsync(runId, CancellationToken.None);
        if (record is null || record.Status != EvalRunStatuses.Queued)
        {
            return;
        }

        var request = EvalRunRecords.Deserialize<StartEvalRunRequest>(record.RequestJson) ?? new StartEvalRunRequest(record.Dataset);
        record.Status = EvalRunStatuses.Running;
        record.StartedAt = timeProvider.GetUtcNow();
        await SaveAsync(record);

        IEvalTarget? target = null;
        var warnings = new List<string>();
        try
        {
            var experiment = await PrepareExperimentAsync(record, await LoadDatasetAsync(record, warnings, cancellationToken), warnings, cancellationToken);
            var dataset = experiment.Dataset;
            var items = EvalItemFilter.Apply(dataset.Items, request.Tags, request.MaxItems);
            record.TotalItems = items.Count;
            record.LangfuseExperimentId = experiment.Identity?.Id;
            record.LangfuseUrl = experiment.Identity is null ? null : await links.ExperimentAsync(experiment.Identity.DatasetId, experiment.Identity.Id, cancellationToken);
            await SaveAsync(record);

            target = dataset.Source == EvalDatasetSources.Builtin
                ? await IsolatedEvalTarget.CreateAsync(
                    new IsolatedTargetOptions { Configuration = configuration, Parent = services, LoggerFactory = loggerFactory },
                    dataset.Corpus,
                    cancellationToken)
                : new LiveEvalTarget(services, options.Value.KeepLiveConversations);

            var plan = new EvalRunPlan(
                record.Id,
                record.Name,
                dataset.With(items),
                record.Judge,
                request.Profile,
                options.Value.Parallelism,
                ItemScorer.RetrievalDepth,
                experiment.Identity);
            using var progress = new RecordProgressObserver(this, record, request);
            var langfuseObserver = experiment.Identity is null ? null : new LangfuseExperimentObserver(langfuse, _logger);
            IEvalRunObserver[] observers = langfuseObserver is null ? [progress] : [progress, langfuseObserver];

            var result = await new EvalRunner(loggerFactory.CreateLogger<EvalRunner>(), timeProvider)
                .RunAsync(plan, target, observers, links.TraceAsync, cancellationToken);

            var signature = EvalSignature.Create(result.Settings, record.Judge);
            var baseline = dataset.Baselines.TryGetValue(signature, out var accepted) ? accepted : null;
            var gate = QualityGate.Evaluate(dataset.Gate, result.Metrics, result.FailedItems, baseline, result.Items.Count, dataset.Items.Count);
            warnings.AddRange(result.Warnings);
            if (langfuseObserver is not null)
            {
                warnings.AddRange(langfuseObserver.Warnings);
            }

            var final = EvalRunMapper.ToDto(result, result.Cancelled ? EvalRunStatuses.Cancelled : EvalRunStatuses.Completed, gate, record.LangfuseUrl, request.MaxItems, request.Tags ?? []);
            Apply(record, final, warnings.Distinct(StringComparer.Ordinal).ToList());
            record.Status = final.Summary.Status;
            record.CompletedAt = timeProvider.GetUtcNow();
            await SaveAsync(record);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            var cancelled = ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
            if (!cancelled)
            {
                _logger.LogWarning(ex, "Eval run {RunId} failed", runId);
            }

            record = await GetAsync(runId, CancellationToken.None) ?? record;
            record.Status = cancelled ? EvalRunStatuses.Cancelled : EvalRunStatuses.Failed;
            record.Error = cancelled ? null : ex is EvalRunException or LangfuseEvalException ? ex.Message : $"The run failed: {ex.Message}";
            record.WarningsJson = EvalRunRecords.Serialize(warnings);
            record.CompletedAt = timeProvider.GetUtcNow();
            await SaveAsync(record);
        }
        finally
        {
            if (target is not null)
            {
                await target.DisposeAsync();
            }
        }
    }

    internal static void Apply(EvalRunRecord record, EvalRunDto run, IReadOnlyList<string>? warnings = null)
    {
        record.CompletedItems = run.Summary.CompletedItems;
        record.FailedItems = run.Summary.FailedItems;
        record.ChatModel = run.Summary.ChatModel;
        record.EmbeddingModel = run.Summary.EmbeddingModel;
        record.ConfigJson = EvalRunRecords.Serialize(run.Config);
        record.MetricsJson = EvalRunRecords.Serialize(run.Summary.Metrics);
        record.ByTagJson = EvalRunRecords.Serialize(run.ByTag);
        record.ItemsJson = EvalRunRecords.Serialize(run.Items);
        record.GateJson = run.Summary.Gate is null ? null : EvalRunRecords.Serialize(run.Summary.Gate);
        record.WarningsJson = EvalRunRecords.Serialize(warnings ?? run.Warnings);
    }

    internal async Task SaveAsync(EvalRunRecord record)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEvalRunRepository>().UpdateAsync(record, CancellationToken.None);
    }

    private async Task<EvalRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEvalRunRepository>().GetAsync(runId, cancellationToken);
    }

    private async Task<EvalDataset> LoadDatasetAsync(EvalRunRecord record, List<string> warnings, CancellationToken cancellationToken)
    {
        if (record.DatasetSource == EvalDatasetSources.Builtin)
        {
            return catalog.Find(record.Dataset) ?? throw new EvalRunException($"There is no builtin dataset '{record.Dataset}' any more.");
        }

        if (record.DatasetSource == EvalDatasetSources.Custom)
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICustomDatasetService>().LoadAsync(record.Dataset, cancellationToken)
                ?? throw new EvalRunException($"There is no dataset '{record.Dataset}' any more.");
        }

        var remote = await langfuse.GetDatasetAsync(record.Dataset, cancellationToken)
            ?? throw new EvalRunException($"Langfuse has no dataset '{record.Dataset}'.");
        var (dataset, problems) = LangfuseDatasets.ToEvalDataset(remote, await langfuse.ListItemsAsync(remote.Name, cancellationToken));
        warnings.AddRange(problems);
        return dataset;
    }

    private async Task<(EvalExperimentIdentity? Identity, EvalDataset Dataset)> PrepareExperimentAsync(
        EvalRunRecord record,
        EvalDataset dataset,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!options.Value.RecordInLangfuse || !langfuse.IsConfigured)
        {
            return (null, dataset);
        }

        try
        {
            var withIds = dataset;
            if (dataset.Source is EvalDatasetSources.Builtin or EvalDatasetSources.Custom)
            {
                var sync = await LangfuseDatasets.SyncAsync(langfuse, dataset, cancellationToken);
                withIds = dataset.With(
                    dataset.Items
                        .Select(item => sync.ItemsByLocalId.TryGetValue(item.Id, out var remote)
                            ? item with { LangfuseItemId = remote.Id, LangfuseItemVersion = remote.Version }
                            : item)
                        .ToList(),
                    sync.Dataset.Id);
            }

            var identity = new EvalExperimentIdentity(
                record.Id.ToString("D"),
                record.Name,
                withIds.LangfuseDatasetId,
                $"Knowledge Agent evaluation of {dataset.Name}",
                options.Value.LangfuseEnvironment,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source"] = LangfuseDatasets.MetadataSourceValue,
                    ["dataset_source"] = record.DatasetSource,
                    ["judge"] = record.Judge ? "true" : "false",
                });
            return (identity, withIds);
        }
        catch (LangfuseEvalException ex)
        {
            warnings.Add($"The run is not recorded in Langfuse: {ex.Message}");
            return (null, dataset);
        }
    }

    private sealed class RecordProgressObserver(EvalRunExecutor executor, EvalRunRecord record, StartEvalRunRequest request) : IEvalRunObserver, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<EvalItemResultDto> _items = [];

        public async Task OnRunStartedAsync(EvalRunPlan plan, EvalRunSettings settings, int totalItems, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                record.TotalItems = totalItems;
                record.ChatModel = settings.ChatModelLabel;
                record.EmbeddingModel = settings.EmbeddingModel;
                record.ConfigJson = EvalRunRecords.Serialize(EvalRunMapper.ToConfig(settings, request.MaxItems, request.Tags ?? []));
                await executor.SaveAsync(record);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task OnItemCompletedAsync(EvalRunPlan plan, EvalItemOutcome outcome, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                _items.Add(outcome.Result);
                var ordered = plan.Dataset.Items
                    .Select(item => _items.FirstOrDefault(result => result.ItemId == item.Id))
                    .OfType<EvalItemResultDto>()
                    .ToList();
                record.CompletedItems = ordered.Count;
                record.FailedItems = ordered.Count(item => item.Status == EvalItemStatuses.Failed);
                record.ItemsJson = EvalRunRecords.Serialize(ordered);
                record.MetricsJson = EvalRunRecords.Serialize(MetricAggregator.Aggregate(ordered));
                record.ByTagJson = EvalRunRecords.Serialize(MetricAggregator.ByTag(ordered));
                await executor.SaveAsync(record);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose() => _gate.Dispose();
    }
}
