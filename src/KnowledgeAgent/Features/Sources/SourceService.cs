using System.Text;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Fetching;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure.VectorStore;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources;

internal sealed partial class SourceService(
    ISourceRepository repository,
    IVectorStore vectorStore,
    IIngestionQueue queue,
    IOptionsMonitor<IngestionOptions> options,
    TimeProvider timeProvider,
    ILogger<SourceService> logger) : ISourceService
{
    internal const int MaxChunkPageSize = 200;
    internal const int MaxSites = 200;

    public async Task<Source> AddUrlAsync(string url, string? title = null, Guid? folderId = null, CancellationToken cancellationToken = default)
    {
        var uri = ValidateUrl(url);
        var userTitle = ValidateOptionalTitle(title);
        var now = timeProvider.GetUtcNow();
        var source = new Source
        {
            Id = Guid.CreateVersion7(now),
            Type = SourceType.Url,
            Status = SourceStatus.Pending,
            Url = uri.AbsoluteUri,
            Site = SourceDefaults.SiteOf(uri),
            Title = userTitle ?? SourceDefaults.TitleForUrl(uri),
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = AutoTitleMetadata(userTitle is null),
            FolderId = folderId,
        };

        return await SaveAndEnqueueAsync(source, content: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Source> AddWebsiteAsync(
        string url,
        WebsiteCrawlSettings settings,
        string? title = null,
        Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Scope == CrawlScope.Page)
        {
            return await AddUrlAsync(url, title, folderId, cancellationToken).ConfigureAwait(false);
        }

        var uri = CrawlUrl.Canonicalize(ValidateUrl(url))
            ?? throw new ValidationFailedException("url", "The URL must be an absolute http or https URL.");
        var crawl = ValidateCrawlSettings(settings);
        var userTitle = ValidateOptionalTitle(title);
        var now = timeProvider.GetUtcNow();
        var website = new Source
        {
            Id = Guid.CreateVersion7(now),
            Type = SourceType.Website,
            Status = SourceStatus.Pending,
            Url = uri.AbsoluteUri,
            Site = SourceDefaults.SiteOf(uri),
            Title = userTitle ?? SourceDefaults.TitleForUrl(uri),
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = AutoTitleMetadata(userTitle is null),
            FolderId = folderId,
            Crawl = crawl,
        };

        var saved = await repository.AddAsync(website, content: null, cancellationToken).ConfigureAwait(false);
        await queue.EnqueueCrawlAsync(saved.Id, cancellationToken).ConfigureAwait(false);
        LogAdded(logger, saved.Id, SourceDefaults.TypeName(saved.Type));
        return saved;
    }

    public async Task<Source> AddFileAsync(
        string fileName,
        string? contentType,
        byte[] content,
        string? title = null,
        Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        var name = SanitizeFileName(fileName);
        if (content is null || content.Length == 0)
        {
            throw new ValidationFailedException("file", "The file is empty.");
        }

        if (content.LongLength > settings.MaxUploadBytes)
        {
            throw new ValidationFailedException("file", $"The file is too large (maximum {UrlFetcher.FormatBytes(settings.MaxUploadBytes)}).");
        }

        var type = ResolveFileType(name, contentType)
            ?? throw new ValidationFailedException("file", "Unsupported file type. Allowed types: PDF (.pdf), text (.txt) and markdown (.md, .markdown).");

        if (type == SourceType.Pdf && !ContentTypeDetector.LooksLikePdf(content))
        {
            throw new ValidationFailedException("file", "The file is not a valid PDF document.");
        }

        if (type != SourceType.Pdf && !ContentTypeDetector.LooksLikeText(content))
        {
            throw new ValidationFailedException("file", "The file does not contain readable text.");
        }

        name ??= "document" + DefaultExtension(type);
        var userTitle = ValidateOptionalTitle(title);
        var now = timeProvider.GetUtcNow();
        var source = new Source
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Status = SourceStatus.Pending,
            Title = userTitle ?? SourceDefaults.TitleForFile(name),
            FileName = name,
            ContentType = StoredContentType(type, contentType),
            SizeBytes = content.LongLength,
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = AutoTitleMetadata(userTitle is null),
            FolderId = folderId,
        };

        var sourceContent = new SourceContent { SourceId = source.Id, RawBytes = content };
        return await SaveAndEnqueueAsync(source, sourceContent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Source> AddTextAsync(
        string title,
        string content,
        SourceType type = SourceType.Text,
        Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var cleanTitle = SourceDefaults.CleanTitle(title);
        if (cleanTitle is null)
        {
            errors["title"] = ["A title is required."];
        }
        else if (title.Trim().Length > SourceDefaults.MaxTitleLength)
        {
            errors["title"] = [$"The title must be at most {SourceDefaults.MaxTitleLength} characters long."];
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            errors["content"] = ["Content is required."];
        }
        else if (Encoding.UTF8.GetByteCount(content) > options.CurrentValue.MaxUploadBytes)
        {
            errors["content"] = [$"The content is too large (maximum {UrlFetcher.FormatBytes(options.CurrentValue.MaxUploadBytes)})."];
        }

        if (type is not (SourceType.Text or SourceType.Markdown))
        {
            errors["format"] = ["Text sources must be plain text or markdown."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var now = timeProvider.GetUtcNow();
        var source = new Source
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Status = SourceStatus.Pending,
            Title = cleanTitle!,
            ContentType = type == SourceType.Markdown ? "text/markdown" : "text/plain",
            SizeBytes = Encoding.UTF8.GetByteCount(content),
            CreatedAt = now,
            UpdatedAt = now,
            FolderId = folderId,
        };

        var sourceContent = new SourceContent { SourceId = source.Id, ExtractedText = content };
        return await SaveAndEnqueueAsync(source, sourceContent, cancellationToken).ConfigureAwait(false);
    }

    public Task<SourcePage> FindAsync(SourceQuery query, CancellationToken cancellationToken = default) =>
        repository.FindAsync(query, cancellationToken);

    public Task<IReadOnlyList<SiteCount>> CountBySiteAsync(SourceQuery query, CancellationToken cancellationToken = default) =>
        repository.CountBySiteAsync(query, MaxSites, cancellationToken);

    public Task<SourceStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        repository.GetStatsAsync(cancellationToken);

    public Task<Source?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetAsync(id, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Vectors go first so that a failure leaves the source listed and the delete can be retried.
        foreach (var page in await repository.ListPagesAsync(id, cancellationToken).ConfigureAwait(false))
        {
            await vectorStore.DeleteBySourceAsync(page.Id, cancellationToken).ConfigureAwait(false);
            await repository.DeleteAsync(page.Id, cancellationToken).ConfigureAwait(false);
        }

        var removedChunks = await vectorStore.DeleteBySourceAsync(id, cancellationToken).ConfigureAwait(false);
        var deleted = await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            LogDeleted(logger, id, removedChunks);
        }

        return deleted;
    }

    public async Task<Source?> ReingestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        await ResetAndEnqueueAsync(source, enqueue: true, cancellationToken).ConfigureAwait(false);
        return source;
    }

    public async Task<int> ReingestAllAsync(CancellationToken cancellationToken = default)
    {
        var sources = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var websites = sources.Where(source => source.Type == SourceType.Website).Select(source => source.Id).ToHashSet();
        foreach (var source in sources.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id))
        {
            // Reset without queueing: the website's crawl ingests them again instead of skipping them as unchanged.
            var crawled = source.ParentId is { } parentId && websites.Contains(parentId);
            await ResetAndEnqueueAsync(source, enqueue: !crawled, cancellationToken).ConfigureAwait(false);
        }

        LogReingestAll(logger, sources.Count);
        return sources.Count;
    }

    public async Task<int> QueueWaitingAsync(CancellationToken cancellationToken = default)
    {
        var waiting = await repository.ListByStatusAsync([SourceStatus.Waiting], cancellationToken).ConfigureAwait(false);
        foreach (var source in waiting.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id))
        {
            await ResetAndEnqueueAsync(source, enqueue: true, cancellationToken).ConfigureAwait(false);
        }

        if (waiting.Count > 0)
        {
            LogQueuedWaiting(logger, waiting.Count);
        }

        return waiting.Count;
    }

    public Task<IReadOnlyList<VectorSearchHit>> GetChunksAsync(Guid id, int skip = 0, int take = 50, CancellationToken cancellationToken = default) =>
        vectorStore.GetChunksAsync(id, Math.Max(0, skip), Math.Clamp(take, 1, MaxChunkPageSize), cancellationToken);

    private async Task<Source> SaveAndEnqueueAsync(Source source, SourceContent? content, CancellationToken cancellationToken)
    {
        var saved = await repository.AddAsync(source, content, cancellationToken).ConfigureAwait(false);
        await queue.EnqueueAsync(saved.Id, cancellationToken).ConfigureAwait(false);
        LogAdded(logger, saved.Id, SourceDefaults.TypeName(saved.Type));
        return saved;
    }

    private async Task ResetAndEnqueueAsync(Source source, bool enqueue, CancellationToken cancellationToken)
    {
        source.Status = SourceStatus.Pending;
        source.Stage = null;
        source.EmbeddedChunks = 0;
        source.Attempts = 0;
        source.Error = null;
        source.UpdatedAt = timeProvider.GetUtcNow();
        await repository.UpdateAsync(source, cancellationToken).ConfigureAwait(false);
        if (!enqueue)
        {
            return;
        }

        if (source.Type == SourceType.Website)
        {
            await queue.EnqueueCrawlAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await queue.EnqueueAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private WebsiteCrawlSettings ValidateCrawlSettings(WebsiteCrawlSettings settings)
    {
        var errors = new Dictionary<string, string[]>();
        var hardCap = options.CurrentValue.MaxCrawlPages;
        if (settings.MaxPages < 1 || settings.MaxPages > hardCap)
        {
            errors["maxPages"] = [$"Max pages must be between 1 and {hardCap}."];
        }

        var patterns = settings.Exclude.Select(pattern => pattern?.Trim() ?? "").Where(pattern => pattern.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (patterns.Count > WebsiteCrawlSettings.MaxExcludePatterns)
        {
            errors["exclude"] = [$"At most {WebsiteCrawlSettings.MaxExcludePatterns} exclude patterns are allowed."];
        }
        else if (patterns.Any(pattern => pattern.Length > WebsiteCrawlSettings.MaxExcludePatternLength))
        {
            errors["exclude"] = [$"Each exclude pattern must be at most {WebsiteCrawlSettings.MaxExcludePatternLength} characters long."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return settings with { Exclude = patterns };
    }

    private static Uri ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ValidationFailedException("url", "A URL is required.");
        }

        var trimmed = url.Trim();
        if (trimmed.Length > SourceDefaults.MaxUrlLength)
        {
            throw new ValidationFailedException("url", $"The URL must be at most {SourceDefaults.MaxUrlLength} characters long.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new ValidationFailedException("url", "The URL must be an absolute http or https URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ValidationFailedException("url", "URLs with embedded credentials are not supported.");
        }

        if (uri.AbsoluteUri.Length > SourceDefaults.MaxUrlLength)
        {
            throw new ValidationFailedException("url", $"The URL must be at most {SourceDefaults.MaxUrlLength} characters long.");
        }

        // Private and loopback hosts are not rejected here. The fetcher blocks them at connect time,
        // the only point where resolved addresses can be checked reliably.
        return uri;
    }

    private static string? ValidateOptionalTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (title.Trim().Length > SourceDefaults.MaxTitleLength)
        {
            throw new ValidationFailedException("title", $"The title must be at most {SourceDefaults.MaxTitleLength} characters long.");
        }

        return SourceDefaults.CleanTitle(title);
    }

    private static Dictionary<string, string> AutoTitleMetadata(bool automatic) =>
        automatic ? new Dictionary<string, string> { [SourceMetadataKeys.AutoTitle] = "true" } : [];

    private static string? SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // Browsers may send a client path such as C:\fakepath\name.pdf.
        var name = fileName.Trim();
        var separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = string.Concat(name.Where(c => !char.IsControl(c))).Trim();
        return name.Length == 0 ? null : TextUtilities.Truncate(name, 255);
    }

    internal static SourceType? ResolveFileType(string? fileName, string? contentType)
    {
        var byExtension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => SourceType.Pdf,
            ".txt" => SourceType.Text,
            ".md" or ".markdown" => SourceType.Markdown,
            _ => (SourceType?)null,
        };

        return byExtension ?? ContentTypeDetector.NormalizeMediaType(contentType) switch
        {
            "application/pdf" or "application/x-pdf" => SourceType.Pdf,
            "text/plain" => SourceType.Text,
            "text/markdown" or "text/x-markdown" => SourceType.Markdown,
            _ => null,
        };
    }

    private static string StoredContentType(SourceType type, string? declared)
    {
        var normalized = ContentTypeDetector.NormalizeMediaType(declared);
        return normalized is not null && ResolveFileType(null, normalized) == type
            ? normalized
            : type switch
            {
                SourceType.Pdf => "application/pdf",
                SourceType.Markdown => "text/markdown",
                _ => "text/plain",
            };
    }

    private static string DefaultExtension(SourceType type) => type switch
    {
        SourceType.Pdf => ".pdf",
        SourceType.Markdown => ".md",
        _ => ".txt",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Added {SourceType} source {SourceId} and queued it for ingestion")]
    private static partial void LogAdded(ILogger logger, Guid sourceId, string sourceType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted source {SourceId} ({RemovedChunks} chunks removed)")]
    private static partial void LogDeleted(ILogger logger, Guid sourceId, long removedChunks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Count} sources for re-ingestion")]
    private static partial void LogReingestAll(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued {Count} sources that were waiting for an embedding model")]
    private static partial void LogQueuedWaiting(ILogger logger, int count);
}
