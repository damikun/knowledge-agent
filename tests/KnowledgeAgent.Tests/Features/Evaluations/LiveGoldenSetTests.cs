using System.Text.Json;
using KnowledgeAgent.Features.Evaluations;
using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.TestKit;
using KnowledgeAgent.Tests.Features.Evaluations.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Evaluations;

// The golden set with a real Claude model answering and judging, on test embeddings so no embedding key is needed.
// Opt-in: RUN_LIVE_LLM_TESTS=1 and ANTHROPIC_API_KEY; LIVE_ANTHROPIC_MODEL picks a cheaper model.
public sealed class LiveGoldenSetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_golden_set_with_a_real_model()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var live = Environment.GetEnvironmentVariable("RUN_LIVE_LLM_TESTS") is "1" or "true";
        Assert.SkipUnless(live && !string.IsNullOrWhiteSpace(key), "Set RUN_LIVE_LLM_TESTS=1 and ANTHROPIC_API_KEY to run the live golden set.");

        var ct = TestContext.Current.CancellationToken;
        var model = Environment.GetEnvironmentVariable("LIVE_ANTHROPIC_MODEL") is { Length: > 0 } chosen ? chosen : "claude-haiku-5-5";
        var maxItems = int.TryParse(Environment.GetEnvironmentVariable("LIVE_EVAL_MAX_ITEMS"), out var limit) ? limit : int.MaxValue;
        var full = EvalDatasetLoader.LoadEmbedded().Single(candidate => candidate.Name == GoldenSetGateTests.DatasetName);
        var dataset = new EvalDataset
        {
            Name = full.Name,
            Description = full.Description,
            Source = full.Source,
            Corpus = full.Corpus,
            Items = full.Items.Take(maxItems).ToList(),
            Gate = full.Gate,
        };

        var settings = EvalTestHost.TestModelSettings();
        settings["AI:Providers:anthropic:ApiKey"] = key;
        settings["AI:Providers:anthropic:DefaultChatModel"] = model;
        foreach (var profile in new[] { "orchestrator", "researcher", "utility", "judge" })
        {
            settings[$"AI:Profiles:{profile}:Provider"] = "anthropic";
        }

        var options = new IsolatedTargetOptions
        {
            Configuration = EvalTestHost.AppSettings(settings),
            ConfigureServices = services => services.AddSingleton<IAiProvider>(new FakeAiProvider()),
        };
        await using var target = await IsolatedEvalTarget.CreateAsync(options, dataset.Corpus, ct);

        var result = await new EvalRunner(NullLogger.Instance).RunAsync(
            new EvalRunPlan(Guid.NewGuid(), $"live {model}", dataset, Judge: true, Profile: null, Parallelism: 4, TopK: 10),
            target,
            [],
            cancellationToken: ct);

        foreach (var (name, value) in result.Metrics.OrderBy(metric => metric.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{name} = {value.Mean:F3} (n={value.Count})");
        }

        var directory = Path.Combine(Path.GetTempPath(), "knowledge-agent-live-evals");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{model}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        output.WriteLine($"report: {path}");

        result.FailedItems.ShouldBe(0, string.Join(Environment.NewLine, result.Items.Where(item => item.Error is not null).Select(item => $"{item.ItemId}: {item.Error}")));
    }
}
