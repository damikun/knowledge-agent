using KnowledgeAgent.Infrastructure.Langfuse;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Prompts.Langfuse;

internal sealed class LangfuseLinkBuilder(
    LangfuseClient client,
    IOptions<LangfuseOptions> options,
    TimeProvider timeProvider,
    ILogger<LangfuseLinkBuilder> logger) : IDisposable
{
    internal static readonly TimeSpan LookupRetryDelay = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _projectId;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    public async Task<string?> GetPromptUrlAsync(string promptName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);

        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return null;
        }

        var projectId = await GetProjectIdAsync(cancellationToken).ConfigureAwait(false);
        if (projectId is null)
        {
            return null;
        }

        var baseUrl = (string.IsNullOrWhiteSpace(settings.PublicUrl) ? settings.BaseUrl! : settings.PublicUrl).Trim().TrimEnd('/');
        return $"{baseUrl}/project/{Uri.EscapeDataString(projectId)}/prompts/{Uri.EscapeDataString(promptName)}";
    }

    internal async Task<string?> GetProjectIdAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ProjectId is { } configured && !string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (Volatile.Read(ref _projectId) is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_projectId is not null || timeProvider.GetUtcNow() < _retryAfter)
            {
                return _projectId;
            }

            try
            {
                var projects = await client.GetProjectsAsync(cancellationToken).ConfigureAwait(false);
                if (projects.Count == 0)
                {
                    _retryAfter = timeProvider.GetUtcNow() + LookupRetryDelay;
                    logger.ProjectLookupFailed("no project is visible to the API key");
                    return null;
                }

                // A project-scoped API key sees exactly one project.
                Volatile.Write(ref _projectId, projects[0].Id);
                return _projectId;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _retryAfter = timeProvider.GetUtcNow() + LookupRetryDelay;
                logger.ProjectLookupFailed(ex.Message);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
