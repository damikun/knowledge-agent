using System.Globalization;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Gate;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Metrics;
using KnowledgeAgent.Features.Evaluations.Reporting;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace KnowledgeAgent.Eval;

internal static class EvalCli
{
    public const int ExitOk = 0;
    public const int ExitGateFailed = 1;
    public const int ExitError = 2;

    private const string ServiceName = "knowledge-agent-eval";

    public const string Help = """
        knowledge-agent-eval: evaluate the agent against golden question sets.

        Usage:
          knowledge-agent-eval [run] [options]   Run a golden set (default command)
          knowledge-agent-eval list              List the golden sets
          knowledge-agent-eval sync [--dataset]  Create or update the Langfuse dataset(s)

        Run options:
          --dataset <name|path>   Golden set name, or a folder / dataset.json path
          --datasets-dir <dir>    Folder of golden sets (default: ./evals/datasets, else the embedded sets)
          --judge                 Score answers with the LLM judge (costs tokens)
          --profile <name>        Chat model profile for the agent
          --tag <tag>             Only items with this tag (repeatable)
          --max-items <n>         Only the first n items
          --parallelism <n>       Items evaluated concurrently (default 3)
          --gate                  Exit with code 1 when the quality gate fails
          --update-baseline       Accept this run as the baseline of its configuration
          --report-dir <dir>      Where report.md and report.json go (default artifacts/evals/<time>-<dataset>)
          --name <text>           Run / experiment name
          --no-langfuse           Do not record the run in Langfuse even when it is configured
          --set <key=value>       Configuration override (repeatable), e.g. --set AI:Profiles:judge:Model=claude-opus-5-5
          --quiet                 Print only the summary line
          --verbose               Log at information level

        Exit codes: 0 = done (gate passed or not enforced), 1 = gate failed, 2 = error.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        CliArguments arguments;
        try
        {
            arguments = CliArguments.Parse(args);
        }
        catch (CliUsageException ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}\n\n{Help}");
            return ExitError;
        }

        if (arguments.Help)
        {
            Console.WriteLine(Help);
            return ExitOk;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return arguments.Command switch
            {
                "list" => List(arguments),
                "sync" => await SyncAsync(arguments, cancellation.Token),
                _ => await RunEvalAsync(arguments, cancellation.Token),
            };
        }
        catch (Exception ex) when (ex is CliUsageException or EvalDatasetException or EvalRunException or LangfuseEvalException or FileNotFoundException)
        {
            await Console.Error.WriteLineAsync("error: " + ex.Message);
            return ExitError;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync("cancelled");
            return ExitError;
        }
    }

    private static int List(CliArguments arguments)
    {
        foreach (var dataset in LoadDatasets(arguments))
        {
            Console.WriteLine($"{dataset.Name}: {dataset.Items.Count} items, {dataset.Corpus.Count} corpus documents");
            if (dataset.Description is not null)
            {
                Console.WriteLine("  " + dataset.Description);
            }

            Console.WriteLine("  tags: " + string.Join(", ", dataset.Tags));
            foreach (var baseline in dataset.Baselines.Values.OrderBy(baseline => baseline.Signature, StringComparer.Ordinal))
            {
                Console.WriteLine($"  baseline: {baseline.Signature}");
            }

            Console.WriteLine(dataset.DirectoryPath is null ? "  (embedded)" : "  " + dataset.DirectoryPath);
        }

        return ExitOk;
    }

    private static async Task<int> SyncAsync(CliArguments arguments, CancellationToken cancellationToken)
    {
        var configuration = BuildConfiguration(arguments);
        var langfuse = configuration.GetSection(LangfuseOptions.SectionName).Get<LangfuseOptions>() ?? new LangfuseOptions();
        if (!langfuse.IsConfigured)
        {
            throw new CliUsageException("Langfuse is not configured: set Langfuse:BaseUrl, Langfuse:PublicKey and Langfuse:SecretKey.");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        LangfuseEvalClient.ConfigureHttpClient(http, langfuse);
        var client = new LangfuseEvalClient(http);
        var datasets = arguments.Dataset is null ? LoadDatasets(arguments) : [LoadDataset(arguments)];
        foreach (var dataset in datasets)
        {
            var result = await LangfuseDatasets.SyncAsync(client, dataset, cancellationToken);
            Console.WriteLine($"{dataset.Name}: {result.Created} created, {result.Updated} updated, {result.Unchanged} unchanged, {result.Archived} archived (Langfuse dataset {result.Dataset.Id})");
        }

        return ExitOk;
    }

    private static async Task<int> RunEvalAsync(CliArguments arguments, CancellationToken cancellationToken)
    {
        var configuration = BuildConfiguration(arguments);
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            })
            .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
            .SetMinimumLevel(arguments.Verbose ? LogLevel.Information : LogLevel.Warning)
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddFilter("System", LogLevel.Warning)
            .AddFilter("Polly", LogLevel.Error));
        var logger = loggerFactory.CreateLogger("KnowledgeAgent.Eval");

        var dataset = LoadDataset(arguments);
        var startedAt = DateTimeOffset.UtcNow;
        var runId = Guid.CreateVersion7();
        var name = arguments.Name ?? $"{dataset.Name} {startedAt:yyyy-MM-dd HH:mm:ss} (cli)";
        var environment = configuration["Evaluation:LangfuseEnvironment"] is { Length: > 0 } configured ? configured : EvalRunner.DefaultEnvironment;

        var langfuseOptions = configuration.GetSection(LangfuseOptions.SectionName).Get<LangfuseOptions>() ?? new LangfuseOptions();
        var useLangfuse = langfuseOptions.IsConfigured && !arguments.NoLangfuse;

        // No hosted services run here, so the CLI creates its own tracer provider.
        using var tracer = useLangfuse
            ? LangfuseTracing.CreateTracerProvider(configuration, ServiceName, [new KeyValuePair<string, object>("langfuse.environment", environment)])
            : null;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        LangfuseEvalClient.ConfigureHttpClient(http, useLangfuse ? langfuseOptions : new LangfuseOptions());
        var client = new LangfuseEvalClient(http);
        var links = useLangfuse ? new LangfuseEvalLinks(langfuseOptions, client, logger) : null;

        EvalExperimentIdentity? experiment = null;
        if (useLangfuse)
        {
            var sync = await LangfuseDatasets.SyncAsync(client, dataset, cancellationToken);
            dataset = dataset.With(
                dataset.Items
                    .Select(item => sync.ItemsByLocalId.TryGetValue(item.Id, out var remote)
                        ? item with { LangfuseItemId = remote.Id, LangfuseItemVersion = remote.Version }
                        : item)
                    .ToList(),
                sync.Dataset.Id);
            experiment = new EvalExperimentIdentity(
                runId.ToString("D"),
                name,
                sync.Dataset.Id,
                $"Knowledge Agent evaluation of {dataset.Name} (CLI)",
                environment,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["source"] = LangfuseDatasets.MetadataSourceValue,
                    ["runner"] = "cli",
                    ["judge"] = arguments.Judge ? "true" : "false",
                });
            await Console.Error.WriteLineAsync(
                $"Langfuse dataset {dataset.Name}: {sync.Created} created, {sync.Updated} updated, {sync.Unchanged} unchanged, {sync.Archived} archived.");
        }

        var items = EvalItemFilter.Apply(dataset.Items, arguments.Tags, arguments.MaxItems);
        if (items.Count == 0)
        {
            throw new CliUsageException("No items match the filters.");
        }

        await Console.Error.WriteLineAsync($"Ingesting {dataset.Corpus.Count} corpus documents into an isolated knowledge base…");
        await using var target = await IsolatedEvalTarget.CreateAsync(
            new IsolatedTargetOptions { Configuration = configuration, LoggerFactory = loggerFactory },
            dataset.Corpus,
            cancellationToken);

        var plan = new EvalRunPlan(runId, name, dataset.With(items), arguments.Judge, arguments.Profile, arguments.Parallelism, ItemScorer.RetrievalDepth, experiment);
        var progress = new ConsoleProgressObserver(arguments.Quiet);
        var langfuseObserver = experiment is null ? null : new LangfuseExperimentObserver(client, logger);
        IEvalRunObserver[] observers = langfuseObserver is null ? [progress] : [progress, langfuseObserver];
        var result = await new EvalRunner(logger).RunAsync(plan, target, observers, links is null ? null : links.TraceAsync, cancellationToken);
        tracer?.ForceFlush(10_000);

        var signature = EvalSignature.Create(result.Settings, arguments.Judge);
        var baseline = dataset.Baselines.TryGetValue(signature, out var accepted) ? accepted : null;
        var gate = QualityGate.Evaluate(dataset.Gate, result.Metrics, result.FailedItems, baseline, result.Items.Count, dataset.Items.Count, arguments.Judge);
        var langfuseUrl = experiment is null || links is null ? null : await links.ExperimentAsync(experiment.DatasetId, experiment.Id, cancellationToken);
        var run = EvalRunMapper.ToDto(
            result,
            result.Cancelled ? EvalRunStatuses.Cancelled : EvalRunStatuses.Completed,
            gate,
            langfuseUrl,
            arguments.MaxItems,
            arguments.Tags,
            langfuseObserver?.Warnings.ToList());

        var markdown = EvalReportWriter.Markdown(run, gate.BaselineSignature is null ? null : baseline);
        var reportDirectory = arguments.ReportDir
            ?? Path.Combine(FindRepositoryRoot() ?? Environment.CurrentDirectory, "artifacts", "evals", $"{startedAt:yyyyMMdd-HHmmss}-{dataset.Name}");
        Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "report.md"), markdown, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "report.json"), EvalReportWriter.Json(run), CancellationToken.None);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
        {
            await File.AppendAllTextAsync(stepSummary, markdown + "\n", CancellationToken.None);
        }

        if (arguments.Quiet)
        {
            Console.WriteLine(Summary(run));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(markdown);
        }

        await Console.Error.WriteLineAsync($"Reports: {reportDirectory}");
        if (arguments.UpdateBaseline)
        {
            if (dataset.DirectoryPath is null)
            {
                throw new CliUsageException("The dataset was loaded from the embedded copy; run from the repository root (or pass --datasets-dir) to update its baseline.");
            }

            if (result.Cancelled || result.FailedItems > 0)
            {
                throw new CliUsageException("A cancelled run or one with failed items cannot become the baseline.");
            }

            if (result.Items.Count < dataset.Items.Count)
            {
                throw new CliUsageException("Only a run over the whole golden set can become the baseline (drop --tag and --max-items).");
            }

            var path = BaselineStore.Write(dataset.DirectoryPath, signature, result.Metrics, result.Items.Count, startedAt);
            await Console.Error.WriteLineAsync($"Baseline for {signature} written to {path}");
            return ExitOk;
        }

        if (baseline is null)
        {
            await Console.Error.WriteLineAsync($"No baseline for {signature} yet: only the minimums were checked. Accept this run with --update-baseline.");
        }

        return arguments.Gate && !gate.Passed ? ExitGateFailed : ExitOk;
    }

    internal static string Summary(EvalRunDto run)
    {
        var metrics = string.Join(
            "  ",
            new[] { EvalMetrics.RetrievalRecallAt5, EvalMetrics.CitationValidity, EvalMetrics.CitationSourceRecall, EvalMetrics.AnswerFactRecall, EvalMetrics.JudgeCorrectness }
                .Where(run.Summary.Metrics.ContainsKey)
                .Select(metric => $"{EvalMetricLabels.Of(metric)} {QualityGate.Percent(run.Summary.Metrics[metric].Mean)}"));
        var gate = run.Summary.Gate is null ? "no gate" : run.Summary.Gate.Passed ? "gate passed" : "gate FAILED";
        return string.Create(CultureInfo.InvariantCulture, $"{run.Summary.Dataset}: {run.Summary.CompletedItems}/{run.Summary.TotalItems} items, {run.Summary.FailedItems} failed, {gate}. {metrics}");
    }

    internal static IConfigurationRoot BuildConfiguration(CliArguments arguments)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets(typeof(EvalCli).Assembly, optional: true)
            .AddEnvironmentVariables();

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in arguments.Settings)
        {
            overrides[key] = value;
        }

        builder.AddInMemoryCollection(overrides);
        var configuration = builder.Build();

        // Without Langfuse the embedded prompts apply anyway. Selecting them avoids a warning per fetch.
        var langfuse = configuration.GetSection(LangfuseOptions.SectionName).Get<LangfuseOptions>();
        if (langfuse?.IsConfigured != true && !arguments.Settings.ContainsKey("Prompts:Store"))
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Prompts:Store"] = "Local" });
            configuration = builder.Build();
        }

        return configuration;
    }

    private static IReadOnlyList<EvalDataset> LoadDatasets(CliArguments arguments)
    {
        var directory = arguments.DatasetsDir ?? FindDatasetsDirectory();
        var datasets = directory is null ? EvalDatasetLoader.LoadEmbedded() : EvalDatasetLoader.LoadAll(directory);
        return datasets.Count > 0 ? datasets : throw new CliUsageException("No golden sets found.");
    }

    private static EvalDataset LoadDataset(CliArguments arguments)
    {
        if (arguments.Dataset is { } path && (Directory.Exists(path) || File.Exists(path)))
        {
            return EvalDatasetLoader.LoadDirectory(path);
        }

        var datasets = LoadDatasets(arguments);
        if (arguments.Dataset is null)
        {
            return datasets.Count == 1
                ? datasets[0]
                : throw new CliUsageException("Choose a golden set with --dataset: " + string.Join(", ", datasets.Select(dataset => dataset.Name)));
        }

        return datasets.FirstOrDefault(dataset => dataset.Name.Equals(arguments.Dataset, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliUsageException($"Unknown golden set '{arguments.Dataset}'. Known: {string.Join(", ", datasets.Select(dataset => dataset.Name))}.");
    }

    private static string? FindDatasetsDirectory()
    {
        var root = FindRepositoryRoot();
        return root is null ? null : Path.Combine(root, "evals", "datasets");
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "evals", "datasets")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
