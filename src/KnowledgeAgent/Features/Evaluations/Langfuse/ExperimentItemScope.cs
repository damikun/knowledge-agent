using System.Diagnostics;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Infrastructure.Telemetry;

namespace KnowledgeAgent.Features.Evaluations.Langfuse;

public sealed class ExperimentItemScope : IDisposable
{
    public const string ExperimentId = "langfuse.experiment.id";
    public const string ExperimentName = "langfuse.experiment.name";
    public const string ExperimentDescription = "langfuse.experiment.description";
    public const string ExperimentDatasetId = "langfuse.experiment.dataset.id";
    public const string ExperimentMetadataPrefix = "langfuse.experiment.metadata.";
    public const string ItemId = "langfuse.experiment.item.id";
    public const string ItemVersion = "langfuse.experiment.item.version";
    public const string ItemRootObservationId = "langfuse.experiment.item.root_observation_id";
    public const string ItemExpectedOutput = "langfuse.experiment.item.expected_output";
    public const string ItemMetadataPrefix = "langfuse.experiment.item.metadata.";
    public const string Environment = "langfuse.environment";

    // Not OpenTelemetry baggage, which would leak experiment ids into outgoing HTTP headers.
    private static readonly AsyncLocal<ExperimentItemScope?> Current = new();
    private static readonly ActivityListener Listener = CreateListener();

    private readonly ExperimentItemScope? _previous;
    private readonly EvalExperimentIdentity _experiment;
    private readonly string _itemId;
    private readonly string? _itemVersion;
    private readonly string? _expectedOutputJson;
    private readonly IReadOnlyDictionary<string, string> _itemMetadata;
    private readonly Lock _gate = new();
    private bool _disposed;

    static ExperimentItemScope() => ActivitySource.AddActivityListener(Listener);

    private ExperimentItemScope(
        EvalExperimentIdentity experiment,
        string itemId,
        string? itemVersion,
        string? expectedOutputJson,
        IReadOnlyDictionary<string, string> itemMetadata)
    {
        _experiment = experiment;
        _itemId = itemId;
        _itemVersion = itemVersion;
        _expectedOutputJson = expectedOutputJson;
        _itemMetadata = itemMetadata;
        _previous = Current.Value;
        Current.Value = this;
    }

    public string? TraceId { get; private set; }
    public string? RootSpanId { get; private set; }

    public static ExperimentItemScope Begin(
        EvalExperimentIdentity experiment,
        string itemId,
        string? itemVersion,
        string? expectedOutputJson,
        IReadOnlyDictionary<string, string>? itemMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        return new ExperimentItemScope(experiment, itemId, itemVersion, expectedOutputJson, itemMetadata ?? new Dictionary<string, string>());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(Current.Value, this))
        {
            Current.Value = _previous;
        }
    }

    private static ActivityListener CreateListener() => new()
    {
        ShouldListenTo = source => source.Name.StartsWith(KnowledgeAgentTelemetry.Prefix, StringComparison.Ordinal),

        // Deliberately no Sample callback, so this listener only tags spans that an exporter samples anyway.
        ActivityStarted = activity => Current.Value?.OnStarted(activity),
    };

    private void OnStarted(Activity activity)
    {
        bool isRoot;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (RootSpanId is null)
            {
                if (activity.OperationName != KnowledgeAgentTelemetry.ChatTurnSpanName || activity.ParentSpanId != default)
                {
                    return;
                }

                TraceId = activity.TraceId.ToHexString();
                RootSpanId = activity.SpanId.ToHexString();
                isRoot = true;
            }
            else if (activity.TraceId.ToHexString() != TraceId)
            {
                return;
            }
            else
            {
                isRoot = false;
            }
        }

        activity.SetTag(ExperimentId, _experiment.Id);
        activity.SetTag(ExperimentName, _experiment.Name);
        if (_experiment.DatasetId is not null)
        {
            activity.SetTag(ExperimentDatasetId, _experiment.DatasetId);
        }

        activity.SetTag(ItemId, _itemId);
        activity.SetTag(ItemRootObservationId, RootSpanId);
        activity.SetTag(Environment, _experiment.Environment);
        if (_itemVersion is not null)
        {
            activity.SetTag(ItemVersion, _itemVersion);
        }

        foreach (var (key, value) in _experiment.Metadata)
        {
            activity.SetTag(ExperimentMetadataPrefix + key, value);
        }

        if (!isRoot)
        {
            return;
        }

        if (_expectedOutputJson is not null)
        {
            activity.SetTag(ItemExpectedOutput, _expectedOutputJson);
        }

        if (_experiment.Description is not null)
        {
            activity.SetTag(ExperimentDescription, _experiment.Description);
        }

        foreach (var (key, value) in _itemMetadata)
        {
            activity.SetTag(ItemMetadataPrefix + key, value);
        }
    }
}
