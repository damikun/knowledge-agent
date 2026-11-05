using System.Collections.Concurrent;
using System.Diagnostics;
using KnowledgeAgent.Features.Evaluations.Langfuse;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class ExperimentItemScopeTests : IDisposable
{
    private const string SourceName = "KnowledgeAgent.Tests.ExperimentScope";
    private static readonly ActivitySource Source = new(SourceName);
    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public ExperimentItemScopeTests()
    {
        _listener = new ActivityListener
        {
            // By name: reading the static field here could create the source mid-registration and miss it.
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Tags_every_span_of_the_item_trace_and_marks_the_chat_turn_as_root()
    {
        var experiment = new EvalExperimentIdentity("exp-1", "Run 1", "ds-1", "Nightly", "evaluation", new Dictionary<string, string> { ["chat_model"] = "fake/m" });
        string? traceId, rootSpanId;
        using (var scope = ExperimentItemScope.Begin(experiment, "ds:item-1", "2026-10-27T10:00:00Z", "{\"answer\":\"27\"}", new Dictionary<string, string> { ["tags"] = "hr" }))
        {
            RunDetached(() =>
            {
                // Setup spans before the turn are not part of the item.
                using (Source.StartActivity("search knowledge"))
                {
                }

                using var root = Source.StartActivity(KnowledgeAgentTelemetry.ChatTurnSpanName);
                using (Source.StartActivity("tool search_knowledge_base"))
                {
                }
            });

            // A separate trace started while the scope is active stays untagged.
            RunDetached(() =>
            {
                using (Source.StartActivity("eval.judge"))
                {
                }
            });

            traceId = scope.TraceId;
            rootSpanId = scope.RootSpanId;
        }

        RunDetached(() =>
        {
            using (Source.StartActivity(KnowledgeAgentTelemetry.ChatTurnSpanName + "-after"))
            {
            }
        });

        var spans = _stopped.ToDictionary(activity => activity.OperationName);
        spans.Keys.ShouldBe(["search knowledge", "tool search_knowledge_base", KnowledgeAgentTelemetry.ChatTurnSpanName, "eval.judge", KnowledgeAgentTelemetry.ChatTurnSpanName + "-after"], ignoreOrder: true);
        var root = spans[KnowledgeAgentTelemetry.ChatTurnSpanName];
        traceId.ShouldBe(root.TraceId.ToHexString());
        rootSpanId.ShouldBe(root.SpanId.ToHexString());

        root.GetTagItem(ExperimentItemScope.ExperimentId).ShouldBe("exp-1");
        root.GetTagItem(ExperimentItemScope.ExperimentName).ShouldBe("Run 1");
        root.GetTagItem(ExperimentItemScope.ExperimentDatasetId).ShouldBe("ds-1");
        root.GetTagItem(ExperimentItemScope.ItemId).ShouldBe("ds:item-1");
        root.GetTagItem(ExperimentItemScope.ItemVersion).ShouldBe("2026-10-27T10:00:00Z");
        root.GetTagItem(ExperimentItemScope.ItemRootObservationId).ShouldBe(rootSpanId);
        root.GetTagItem(ExperimentItemScope.ItemExpectedOutput).ShouldBe("{\"answer\":\"27\"}");
        root.GetTagItem(ExperimentItemScope.ExperimentDescription).ShouldBe("Nightly");
        root.GetTagItem(ExperimentItemScope.ItemMetadataPrefix + "tags").ShouldBe("hr");
        root.GetTagItem(ExperimentItemScope.ExperimentMetadataPrefix + "chat_model").ShouldBe("fake/m");
        root.GetTagItem(ExperimentItemScope.Environment).ShouldBe("evaluation");

        var tool = spans["tool search_knowledge_base"];
        tool.GetTagItem(ExperimentItemScope.ExperimentId).ShouldBe("exp-1");
        tool.GetTagItem(ExperimentItemScope.ItemRootObservationId).ShouldBe(rootSpanId);
        tool.GetTagItem(ExperimentItemScope.ItemExpectedOutput).ShouldBeNull();

        spans["search knowledge"].GetTagItem(ExperimentItemScope.ExperimentId).ShouldBeNull();
        spans["eval.judge"].GetTagItem(ExperimentItemScope.ExperimentId).ShouldBeNull();
        spans[KnowledgeAgentTelemetry.ChatTurnSpanName + "-after"].GetTagItem(ExperimentItemScope.ExperimentId).ShouldBeNull();
    }

    [Fact]
    public async Task Concurrent_items_keep_their_own_context()
    {
        var experiment = new EvalExperimentIdentity("exp-2", "Run 2", null, null, "evaluation", new Dictionary<string, string>());

        async Task<string?> RunItemAsync(string itemId)
        {
            using var scope = ExperimentItemScope.Begin(experiment, itemId, null, null);
            await Task.Yield();
            RunDetached(() =>
            {
                using (Source.StartActivity(KnowledgeAgentTelemetry.ChatTurnSpanName))
                {
                }
            });
            return scope.TraceId;
        }

        var traces = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => RunItemAsync("item-" + index))));

        traces.Distinct().Count().ShouldBe(8);
        var tagged = _stopped.Where(activity => activity.GetTagItem(ExperimentItemScope.ExperimentId) is "exp-2").ToList();
        tagged.Count.ShouldBe(8);
        foreach (var activity in tagged)
        {
            var itemId = (string)activity.GetTagItem(ExperimentItemScope.ItemId)!;
            traces[int.Parse(itemId["item-".Length..])].ShouldBe(activity.TraceId.ToHexString());
        }
    }

    // Runs the action the way the chat service does: on another task with no ambient parent span.
    private static void RunDetached(Action action) => Task.Run(() =>
    {
        Activity.Current = null;
        action();
    }).GetAwaiter().GetResult();
}
