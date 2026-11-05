using System.Collections.Concurrent;
using System.Diagnostics;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Judge;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Search;
using KnowledgeAgent.Features.Search.Reranking;
using KnowledgeAgent.Features.Sources;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace KnowledgeAgent.Features.Evaluations.Running;

public sealed class EvalRunner(ILogger logger, TimeProvider? timeProvider = null)
{
    public const string DefaultEnvironment = "evaluation";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<EvalRunResult> RunAsync(
        EvalRunPlan plan,
        IEvalTarget target,
        IReadOnlyList<IEvalRunObserver> observers,
        Func<string, CancellationToken, Task<string?>>? traceUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observers);

        var startedAt = _time.GetUtcNow();
        var services = target.Services;
        var judge = plan.Judge
            ? new EvalJudge(
                services.GetRequiredService<IPromptService>(),
                services.GetRequiredService<IChatClientFactory>(),
                EvalJudge.DefaultProfile,
                plan.Experiment?.Environment ?? DefaultEnvironment,
                logger)
            : null;
        var settings = await ResolveSettingsAsync(plan, target, judge, cancellationToken);
        if (plan.Experiment is { } experimentIdentity)
        {
            var metadata = new Dictionary<string, string>(experimentIdentity.Metadata, StringComparer.Ordinal)
            {
                ["target"] = settings.Target,
                ["profile"] = settings.Profile,
            };
            AddIfSet(metadata, "chat_model", settings.ChatModelLabel);
            AddIfSet(metadata, "embedding_model", settings.EmbeddingModel);
            AddIfSet(metadata, "judge_model", settings.JudgeModel);
            AddIfSet(metadata, "search_mode", settings.SearchMode);
            AddIfSet(metadata, "rerank_model", settings.RerankModel);
            AddIfSet(metadata, "prompt", settings.PromptName is null ? null : $"{settings.PromptName} v{settings.PromptVersion} ({settings.PromptOrigin})");
            plan = plan with { Experiment = experimentIdentity with { Metadata = metadata } };
        }

        var warnings = new ConcurrentQueue<string>();
        var items = plan.Dataset.Items;
        var references = items.SelectMany(item => item.ExpectedSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resolved = await target.ResolveSourcesAsync(references, cancellationToken);
        foreach (var reference in resolved.Unresolved)
        {
            var affected = items.Where(item => item.ExpectedSources.Contains(reference, StringComparer.OrdinalIgnoreCase)).Select(item => item.Id);
            warnings.Enqueue($"Expected source '{reference}' is not in the knowledge base (items: {string.Join(", ", affected)}).");
        }

        foreach (var observer in observers)
        {
            await NotifyAsync(observer, o => o.OnRunStartedAsync(plan, settings, items.Count, cancellationToken), warnings);
        }

        var results = new EvalItemResultDto?[items.Count];
        var cancelled = false;
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, items.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, plan.Parallelism), CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var outcome = await EvaluateItemAsync(plan, target, items[index], resolved, judge, warnings, traceUrl, token);
                    results[index] = outcome.Result;
                    foreach (var observer in observers)
                    {
                        await NotifyAsync(observer, o => o.OnItemCompletedAsync(plan, outcome, token), warnings);
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        var completed = results.OfType<EvalItemResultDto>().ToList();
        var result = new EvalRunResult(
            plan,
            settings,
            completed,
            MetricAggregator.Aggregate(completed),
            MetricAggregator.ByTag(completed),
            warnings.ToList(),
            startedAt,
            _time.GetUtcNow(),
            cancelled);

        foreach (var observer in observers)
        {
            await NotifyAsync(observer, o => o.OnRunCompletedAsync(result, CancellationToken.None), warnings);
        }

        return result with { Warnings = warnings.ToList() };
    }

    internal static async Task<EvalRunSettings> ResolveSettingsAsync(
        EvalRunPlan plan,
        IEvalTarget target,
        EvalJudge? judge,
        CancellationToken cancellationToken)
    {
        var services = target.Services;
        var profile = string.IsNullOrWhiteSpace(plan.Profile)
            ? services.GetRequiredService<IOptions<AgentOptions>>().Value.OrchestratorProfile
            : plan.Profile.Trim();
        var prompts = services.GetRequiredService<IPromptService>();

        PromptTemplate? prompt = null;
        try
        {
            prompt = await prompts.GetTemplateAsync(PromptNames.OrchestratorSystem, selector: null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the chat turn reports prompt problems itself.
        }

        ResolvedModelProfile chat;
        try
        {
            var configProfile = PromptConfig.ParseProfile(prompt?.Config);
            var patch = configProfile is null || configProfile.Equals(profile, StringComparison.OrdinalIgnoreCase)
                ? PromptConfig.ParseModelPatch(prompt?.Config)
                : null;
            chat = await services.GetRequiredService<IModelProfileResolver>().ResolveAsync(profile, patch, cancellationToken: cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            throw new EvalRunException($"The agent has no usable chat model: {ex.Message}", ex);
        }

        string? judgeModel = null;
        if (judge is not null)
        {
            var (model, unavailable) = await judge.DescribeAsync(cancellationToken);
            judgeModel = model ?? throw new EvalRunException(unavailable ?? "The LLM judge is not available.");
        }

        await using var scope = services.CreateAsyncScope();
        var stats = await scope.ServiceProvider.GetRequiredService<ISourceService>().GetStatsAsync(cancellationToken);
        var embeddingModel = stats.ReadyCountByEmbeddingModel.OrderByDescending(entry => entry.Value).Select(entry => entry.Key).FirstOrDefault();

        var search = services.GetRequiredService<IOptionsMonitor<SearchOptions>>().CurrentValue;
        var searchMode = SearchModes.Normalize(search.Mode) ?? SearchModes.Hybrid;
        var rerankModel = search.Rerank && services.GetService<IRerankService>() is { } reranker
            ? await reranker.DescribeAsync(cancellationToken)
            : null;

        return new EvalRunSettings(
            target.Kind,
            profile,
            chat.ProviderKey,
            chat.Model,
            embeddingModel,
            judgeModel,
            prompt?.Name ?? PromptNames.OrchestratorSystem,
            prompt?.Version,
            prompt?.Origin.ToString().ToLowerInvariant(),
            Math.Max(1, plan.Parallelism),
            Math.Max(plan.TopK, ItemScorer.RetrievalDepth),
            searchMode,
            rerankModel);
    }

    private async Task<EvalItemOutcome> EvaluateItemAsync(
        EvalRunPlan plan,
        IEvalTarget target,
        EvalItem item,
        ResolvedSources resolved,
        EvalJudge? judge,
        ConcurrentQueue<string> warnings,
        Func<string, CancellationToken, Task<string?>>? traceUrl,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var services = target.Services;
        var retrieval = await RetrieveAsync(services, item, Math.Max(plan.TopK, ItemScorer.RetrievalDepth), cancellationToken);
        if (retrieval.Error is not null)
        {
            warnings.Enqueue($"The retrieval check failed for item {item.Id}: {retrieval.Error}");
        }

        AgentTurnResult turn;
        Guid? conversationId = null;
        string? traceId = null, rootSpanId = null;
        var experiment = plan.Experiment is { } identity
            ? ExperimentItemScope.Begin(
                identity,
                item.LangfuseItemId ?? LangfuseDatasets.ItemId(plan.Dataset.Name, item.Id),
                item.LangfuseItemVersion,
                LangfuseDatasets.ExpectedOutput(item).ToJsonString(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tags"] = string.Join(",", item.Tags),
                    ["answerable"] = item.Answerable ? "true" : "false",
                })
            : null;
        try
        {
            foreach (var earlier in item.History.Where(turn => turn.Role == EvalTurnRoles.User))
            {
                var warmUp = await AgentTurnCapture.RunAsync(services, earlier.Content, plan.Profile, cancellationToken, conversationId);
                conversationId = warmUp.ConversationId ?? conversationId;
            }

            turn = await AgentTurnCapture.RunAsync(services, item.Question, plan.Profile, cancellationToken, conversationId);
        }
        finally
        {
            traceId = experiment?.TraceId;
            rootSpanId = experiment?.RootSpanId;
            experiment?.Dispose();
        }

        var score = ItemScorer.Score(item, retrieval, turn, resolved);
        var metrics = new Dictionary<string, double>(score.Metrics, StringComparer.Ordinal);

        EvalJudgementDto? judgement = null;
        if (judge is not null)
        {
            if (turn.Failed || turn.Answer.Length == 0)
            {
                judgement = new EvalJudgementDto(null, [], "Not judged: the agent produced no answer.");
            }
            else
            {
                var passages = await PassageLoader.LoadAsync(services, turn.Available, cancellationToken);
                var traceMetadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["eval_item_id"] = item.Id, ["eval_run"] = plan.Name };
                if (plan.Experiment is { } experimentIdentity)
                {
                    traceMetadata["experiment_id"] = experimentIdentity.Id;
                }

                if (traceId is not null)
                {
                    traceMetadata["graded_trace_id"] = traceId;
                }

                var outcome = await judge.JudgeAsync(item, turn.Answer, passages, traceMetadata, cancellationToken);
                judgement = outcome.Judgement;
                foreach (var (metric, value) in outcome.Metrics)
                {
                    metrics[metric] = value;
                }
            }
        }

        if ((turn.ConversationId ?? conversationId) is { } leftOver)
        {
            try
            {
                await target.CleanupConversationAsync(leftOver, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete eval conversation {ConversationId}", leftOver);
            }
        }

        string? traceLink = null;
        if (traceId is not null && traceUrl is not null)
        {
            try
            {
                traceLink = await traceUrl(traceId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not build the trace link for {TraceId}", traceId);
            }
        }

        var result = new EvalItemResultDto(
            item.Id,
            item.Question,
            item.Answerable,
            item.Tags,
            item.ExpectedSources,
            item.ExpectedFacts,
            item.ReferenceAnswer,
            turn.Failed ? EvalItemStatuses.Failed : EvalItemStatuses.Completed,
            turn.Answer.Length == 0 ? null : turn.Answer,
            turn.FinishReason,
            turn.Error,
            metrics,
            score.Retrieved,
            score.Citations,
            score.MissingFacts,
            judgement,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            turn.InputTokens,
            turn.OutputTokens,
            turn.ToolCalls,
            traceId,
            traceLink);
        return new EvalItemOutcome(result, rootSpanId);
    }

    private static async Task<RetrievalResult> RetrieveAsync(IServiceProvider services, EvalItem item, int topK, CancellationToken cancellationToken)
    {
        using var suppress = SuppressInstrumentationScope.Begin();
        try
        {
            await using var scope = services.CreateAsyncScope();
            var hits = await scope.ServiceProvider.GetRequiredService<IKnowledgeSearch>()
                .SearchAsync(new KnowledgeSearchRequest(item.Question, topK), cancellationToken);
            return new RetrievalResult(hits, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new RetrievalResult([], ex.Message);
        }
    }

    private static void AddIfSet(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private async Task NotifyAsync(IEvalRunObserver observer, Func<IEvalRunObserver, Task> notify, ConcurrentQueue<string> warnings)
    {
        try
        {
            await notify(observer);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eval run observer {Observer} failed", observer.GetType().Name);
            warnings.Enqueue($"{observer.GetType().Name} failed: {ex.Message}");
        }
    }
}
