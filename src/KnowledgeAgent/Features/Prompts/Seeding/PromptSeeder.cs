using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Prompts.Seeding;

internal sealed class PromptSeeder : IDisposable
{
    public const string CommitMessage = "Seeded by Knowledge Agent";

    private static readonly IReadOnlyList<string> SeedLabels = [LangfuseClient.DefaultLabel];

    private readonly LangfuseClient _client;
    private readonly LocalPromptCatalog _local;
    private readonly TimeProvider _time;
    private readonly ILogger<PromptSeeder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _completed;
    private long _lastAttemptUtcTicks;

    public PromptSeeder(
        LangfuseClient client,
        LocalPromptCatalog local,
        IOptions<PromptOptions> promptOptions,
        IOptions<LangfuseOptions> langfuseOptions,
        TimeProvider timeProvider,
        ILogger<PromptSeeder> logger)
    {
        _client = client;
        _local = local;
        _time = timeProvider;
        _logger = logger;
        DisabledReason = !promptOptions.Value.SeedOnStartup ? "Prompts:SeedOnStartup is false"
            : PromptStoreMode.IsLocal(promptOptions.Value) ? "Prompts:Store is Local"
            : !langfuseOptions.Value.IsConfigured ? "Langfuse is not configured"
            : null;
    }

    public string? DisabledReason { get; }

    public bool IsEnabled => DisabledReason is null;

    public bool IsCompleted => _completed;

    public DateTimeOffset? LastAttemptAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastAttemptUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public async Task<PromptSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return PromptSeedResult.None;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed)
            {
                return PromptSeedResult.None;
            }

            Interlocked.Exchange(ref _lastAttemptUtcTicks, _time.GetUtcNow().UtcTicks);

            var existing = (await _client.ListAllPromptsAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(prompt => prompt.Name, StringComparer.Ordinal);

            var created = new List<string>();
            var present = new List<string>();
            var updated = new List<string>();
            foreach (var template in _local.All)
            {
                if (existing.TryGetValue(template.Name, out var listed))
                {
                    if (await UpdateIfUntouchedAsync(template, listed, cancellationToken).ConfigureAwait(false))
                    {
                        updated.Add(template.Name);
                    }
                    else
                    {
                        _logger.PromptAlreadyInLangfuse(template.Name);
                        present.Add(template.Name);
                    }

                    continue;
                }

                var request = LangfuseCreatePromptRequest.FromTemplate(template, LabelsFor(template), CommitMessage);
                var stored = await _client.CreatePromptAsync(request, cancellationToken).ConfigureAwait(false);
                _logger.PromptSeeded(template.Name, stored.Version);
                created.Add(template.Name);
            }

            _completed = true;
            _logger.SeedingCompleted(created.Count, present.Count + updated.Count);
            return new PromptSeedResult(created, present, updated);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    // Replaces a version this seeder wrote, unless someone edited the prompt or rolled it back since.
    private async Task<bool> UpdateIfUntouchedAsync(PromptTemplate template, LangfusePromptListItem stored, CancellationToken cancellationToken)
    {
        if (stored.Versions.Count == 0)
        {
            return false;
        }

        var production = await _client.GetPromptAsync(template.Name, LangfuseClient.DefaultLabel, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (production is null
            || !string.Equals(production.CommitMessage, CommitMessage, StringComparison.Ordinal)
            || production.Version != stored.Versions.Max()
            || SameContent(production, template))
        {
            return false;
        }

        var request = LangfuseCreatePromptRequest.FromTemplate(template, LabelsFor(template), CommitMessage);
        var created = await _client.CreatePromptAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.PromptUpdated(template.Name, production.Version, created.Version);
        return true;
    }

    private static bool SameContent(PromptTemplate remote, PromptTemplate local) =>
        remote.Type == local.Type
        && Normalize(remote.Text) == Normalize(local.Text)
        && remote.Messages.Select(message => (message.Role, Normalize(message.Content)))
            .SequenceEqual(local.Messages.Select(message => (message.Role, Normalize(message.Content))))
        && JsonNode.DeepEquals(remote.Config ?? new JsonObject(), local.Config ?? new JsonObject());

    private static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static IReadOnlyList<string> LabelsFor(PromptTemplate template)
    {
        // Langfuse maintains the "latest" label itself.
        var labels = template.Labels.Where(label => !string.Equals(label, "latest", StringComparison.Ordinal)).ToList();
        return labels.Count > 0 ? labels : SeedLabels;
    }
}

internal sealed record PromptSeedResult(IReadOnlyList<string> Created, IReadOnlyList<string> Existing, IReadOnlyList<string> Updated)
{
    public static readonly PromptSeedResult None = new([], [], []);
}
