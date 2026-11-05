using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Features.Evaluations.Langfuse;

public sealed class LangfuseEvalLinks(LangfuseOptions options, LangfuseEvalClient client, ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Read without the gate; set only once the lookup has ended, so a reader never takes a lookup under way for one
    // that found nothing.
    private volatile string? _projectId = string.IsNullOrWhiteSpace(options.ProjectId) ? null : options.ProjectId.Trim();
    private volatile bool _lookedUp;

    private string? BaseUrl
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(options.PublicUrl) ? options.BaseUrl : options.PublicUrl;
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
        }
    }

    public async Task<string?> DatasetAsync(string? datasetId, CancellationToken cancellationToken) =>
        datasetId is null ? null : await BuildAsync($"datasets/{Uri.EscapeDataString(datasetId)}", cancellationToken);

    public async Task<string?> ExperimentAsync(string? datasetId, string experimentId, CancellationToken cancellationToken) =>
        datasetId is null
            ? await BuildAsync("experiments", cancellationToken)
            : await BuildAsync($"datasets/{Uri.EscapeDataString(datasetId)}/runs/{Uri.EscapeDataString(experimentId)}", cancellationToken);

    public async Task<string?> TraceAsync(string? traceId, CancellationToken cancellationToken) =>
        traceId is null ? null : await BuildAsync($"traces/{Uri.EscapeDataString(traceId)}", cancellationToken);

    private async Task<string?> BuildAsync(string path, CancellationToken cancellationToken)
    {
        var baseUrl = BaseUrl;
        if (baseUrl is null)
        {
            return null;
        }

        var projectId = await ProjectIdAsync(cancellationToken);
        return projectId is null ? null : $"{baseUrl}/project/{Uri.EscapeDataString(projectId)}/{path}";
    }

    private async Task<string?> ProjectIdAsync(CancellationToken cancellationToken)
    {
        if (_projectId is not null || _lookedUp || !client.IsConfigured)
        {
            return _projectId;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_lookedUp)
            {
                _projectId = await client.GetProjectIdAsync(cancellationToken);
                _lookedUp = true;
            }
        }
        catch (LangfuseEvalException ex)
        {
            _lookedUp = true;
            logger.LogDebug(ex, "Could not look up the Langfuse project id; links are omitted");
        }
        finally
        {
            _gate.Release();
        }

        return _projectId;
    }

    public void Dispose() => _gate.Dispose();
}
