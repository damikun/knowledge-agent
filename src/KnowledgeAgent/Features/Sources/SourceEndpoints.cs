using System.Globalization;
using KnowledgeAgent.Features.Sources.Crawling;
using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Infrastructure;
using KnowledgeAgent.Infrastructure.Jobs;
using KnowledgeAgent.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace KnowledgeAgent.Features.Sources;

internal static class SourceEndpoints
{
    public const int DefaultChunkPageSize = 50;
    public const int DefaultSourcePageSize = 25;
    public const int MaxSourceQueryLength = 500;
    public const string AnyStatus = "any";
    public const string UnfiledFolder = "unfiled";
    public const string AnyParent = "any";
    public const long MultipartOverheadBytes = 1024 * 1024;

    public static long UploadRequestLimit(IConfiguration configuration)
    {
        var maxUploadBytes = configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()?.MaxUploadBytes
            ?? new IngestionOptions().MaxUploadBytes;
        return Math.Max(maxUploadBytes, 0) + MultipartOverheadBytes;
    }

    public static RouteGroupBuilder MapSourceEndpoints(this IEndpointRouteBuilder routes, long uploadRequestLimit)
    {
        var group = routes.MapGroup("/sources").WithTags("Sources");

        group.MapGet("/", ListAsync)
            .WithName("ListSources")
            .WithSummary("Find sources, one page at a time.")
            .WithDescription(
                "query: words that must all appear in the title, URL or file name (case-insensitive); titles starting with it "
                + "come first, then alphabetical. Without a query, newest first. type: url, pdf, text, markdown or website. "
                + "status: comma-separated pending, processing, ready, failed, waiting, needsbrowser; 'any' or absent = all. "
                + "ids: comma-separated source ids (at most 100). folder: a folder id for the sources directly in that folder, "
                + "or 'unfiled' for the sources outside every folder; absent = all. site: the web site of URL sources, such as "
                + "example.com. parent: a website id for its pages, 'any' for all sources; absent = every source except crawled "
                + "pages. offset: default 0. limit: 1-100, default 25.");

        group.MapGet("/sites", GetSitesAsync)
            .WithName("ListSourceSites")
            .WithSummary("Web sites of the matching URL sources with the number of sources from each, the most first.")
            .WithDescription($"Takes the query, type, status and folder filters of GET /api/sources. At most {SourceService.MaxSites} sites.");

        group.MapGet("/stats", GetStatsAsync)
            .WithName("GetSourceStats")
            .WithSummary("Source counts per status, ready sources per type and embedding model, and the ready chunk total.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSource")
            .WithSummary("Get one source.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/url", AddUrlAsync)
            .WithName("AddUrlSource")
            .WithSummary("Add a web page or a website; it is fetched and ingested in the background. Optional folderId files it in a folder.")
            .WithDescription(
                "scope: page (default) adds the one page; section adds a website crawled from the page through the pages under its "
                + "path; site crawls the whole host. Each crawled page becomes a source whose parentId is the website. maxPages "
                + $"(default {WebsiteCrawlSettings.DefaultMaxPages}) caps the pages, up to Ingestion:MaxCrawlPages. exclude: path patterns "
                + "to skip, where * matches anything.");

        group.MapPost("/upload", UploadAsync)
            .WithName("UploadSource")
            .WithSummary("Upload a PDF, TXT or Markdown file (multipart field 'file', optional 'title' and 'folderId').")
            .DisableAntiforgery()
            .WithMetadata(new RequestBodySizeLimit(uploadRequestLimit))
            .WithFormOptions(multipartBodyLengthLimit: uploadRequestLimit)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/text", AddTextAsync)
            .WithName("AddTextSource")
            .WithSummary("Add pasted text or Markdown. Optional folderId files it in a folder.");

        group.MapPost("/reindex", ReindexAsync)
            .WithName("ReindexSources")
            .WithSummary("Re-ingest every source (e.g. after the embedding model changed).");

        group.MapPost("/{id:guid}/reingest", ReingestAsync)
            .WithName("ReingestSource")
            .WithSummary("Re-run ingestion for one source.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSource")
            .WithSummary("Delete a source, its content and its vectors.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/chunks", GetChunksAsync)
            .WithName("GetSourceChunks")
            .WithSummary("Stored chunks of a source, ordered by chunk index.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapSourceFolderEndpoints();
        return group;
    }

    // Query values are bound as strings so malformed ones produce field errors instead of a bare 400.
    private static async Task<Results<Ok<SourcePageDto>, ValidationProblem>> ListAsync(
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] string[]? status,
        [FromQuery] string? offset,
        [FromQuery] string? limit,
        [FromQuery] string[]? ids,
        [FromQuery] string? folder,
        [FromQuery] string? site,
        [FromQuery] string? parent,
        ISourceService sources,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var filters = ParseFilters(query, type, status, folder, site, errors);

        var pageOffset = 0;
        if (!string.IsNullOrWhiteSpace(offset)
            && (!int.TryParse(offset, NumberStyles.None, CultureInfo.InvariantCulture, out pageOffset) || pageOffset < 0))
        {
            errors.Add("offset", "offset must be a non-negative integer.");
        }

        var pageLimit = DefaultSourcePageSize;
        if (!string.IsNullOrWhiteSpace(limit)
            && (!int.TryParse(limit, NumberStyles.None, CultureInfo.InvariantCulture, out pageLimit)
                || pageLimit is < 1 or > SourceQuery.MaxLimit))
        {
            errors.Add("limit", $"limit must be between 1 and {SourceQuery.MaxLimit}.");
        }

        var sourceIds = new HashSet<Guid>();
        foreach (var value in SplitList(ids))
        {
            if (!Guid.TryParse(value, out var id))
            {
                errors.Add("ids", "ids must be comma-separated source ids.");
                break;
            }

            sourceIds.Add(id);
        }

        if (sourceIds.Count > SourceQuery.MaxLimit)
        {
            errors.Add("ids", $"ids accepts at most {SourceQuery.MaxLimit} source ids.");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        // A citation links to a page by its id, so ids alone also find pages.
        var parentFilter = sourceIds.Count > 0 && string.IsNullOrWhiteSpace(parent) ? null : ParseParent(parent, errors);
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var page = await sources.FindAsync(filters with { Ids = sourceIds, Parent = parentFilter, Offset = pageOffset, Limit = pageLimit }, cancellationToken);
        return TypedResults.Ok(page.ToDto(pageLimit, jobs));
    }

    private static async Task<Results<Ok<SourceSiteDto[]>, ValidationProblem>> GetSitesAsync(
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] string[]? status,
        [FromQuery] string? folder,
        ISourceService sources,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        var filters = ParseFilters(query, type, status, folder, site: null, errors) with { Parent = ParentFilter.TopLevel };
        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var sites = await sources.CountBySiteAsync(filters, cancellationToken);
        return TypedResults.Ok(sites.Select(site => new SourceSiteDto(site.Site, site.Count)).ToArray());
    }

    // The list, the site counts and bulk moves share these filters; prefix names the request field that holds them.
    internal static SourceQuery ParseFilters(
        string? query,
        string? type,
        IEnumerable<string?>? status,
        string? folder,
        string? site,
        ValidationErrors errors,
        string prefix = "")
    {
        var text = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (text is { Length: > MaxSourceQueryLength })
        {
            errors.Add(prefix + "query", $"query must be at most {MaxSourceQueryLength} characters.");
        }

        SourceType? sourceType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (TryParseName<SourceType>(type, out var parsedType))
            {
                sourceType = parsedType;
            }
            else
            {
                errors.Add(prefix + "type", $"type must be one of: {Names<SourceType>()}.");
            }
        }

        var statuses = new List<SourceStatus>();
        var anyStatus = false;
        foreach (var value in SplitList(status))
        {
            if (string.Equals(value, AnyStatus, StringComparison.OrdinalIgnoreCase))
            {
                anyStatus = true;
            }
            else if (TryParseName<SourceStatus>(value, out var parsedStatus))
            {
                statuses.Add(parsedStatus);
            }
            else
            {
                errors.Add(prefix + "status", $"status must be '{AnyStatus}' or a comma-separated list of: {Names<SourceStatus>()}.");
                break;
            }
        }

        FolderFilter? folderFilter = null;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            if (string.Equals(folder.Trim(), UnfiledFolder, StringComparison.OrdinalIgnoreCase))
            {
                folderFilter = FolderFilter.Unfiled;
            }
            else if (Guid.TryParse(folder, out var folderId))
            {
                folderFilter = new FolderFilter(folderId);
            }
            else
            {
                errors.Add(prefix + "folder", $"folder must be a folder id or '{UnfiledFolder}'.");
            }
        }

        string? siteFilter = null;
        if (!string.IsNullOrWhiteSpace(site))
        {
            siteFilter = SourceDefaults.NormalizeSite(site);
            if (siteFilter.Length > SourceDefaults.MaxSiteLength)
            {
                errors.Add(prefix + "site", $"site must be at most {SourceDefaults.MaxSiteLength} characters.");
            }
        }

        return new SourceQuery(text, sourceType, anyStatus ? null : statuses, Folder: folderFilter, Site: siteFilter);
    }

    // Absent: the sources that are not pages of a website. A website id: its pages. "any": everything.
    internal static ParentFilter? ParseParent(string? parent, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return ParentFilter.TopLevel;
        }

        if (string.Equals(parent.Trim(), AnyParent, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Guid.TryParse(parent, out var parentId))
        {
            return new ParentFilter(parentId);
        }

        errors.Add("parent", $"parent must be a website id or '{AnyParent}'.");
        return null;
    }

    private static async Task<Ok<SourceStatsDto>> GetStatsAsync(ISourceService sources, CancellationToken cancellationToken)
    {
        var stats = await sources.GetStatsAsync(cancellationToken);
        return TypedResults.Ok(stats.ToDto());
    }

    private static async Task<Results<Ok<SourceDto>, ProblemHttpResult>> GetAsync(
        Guid id,
        ISourceService sources,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        var source = await sources.GetAsync(id, cancellationToken);
        return source is null ? SourceNotFound(id) : TypedResults.Ok(source.ToDto(jobs));
    }

    private static async Task<Results<Accepted<SourceDto>, ValidationProblem>> AddUrlAsync(
        AddUrlSourceRequest request,
        ISourceService sources,
        ISourceFolderService folders,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return ApiResults.Validation("url", "A URL is required.");
        }

        if (await IsMissingFolderAsync(request.FolderId, folders, cancellationToken))
        {
            return ApiResults.Validation("folderId", MissingFolderMessage);
        }

        if (!TryParseScope(request.Scope, out var scope))
        {
            return ApiResults.Validation("scope", "scope must be one of: page, section, site.");
        }

        var settings = new WebsiteCrawlSettings
        {
            Scope = scope,
            MaxPages = request.MaxPages ?? WebsiteCrawlSettings.DefaultMaxPages,
            Exclude = request.Exclude ?? [],
        };
        var source = await sources.AddWebsiteAsync(request.Url.Trim(), settings, request.Title.TrimToNull(), request.FolderId, cancellationToken);
        return Accepted(source, jobs);
    }

    private static async Task<Results<Accepted<SourceDto>, ValidationProblem>> UploadAsync(
        IFormFile? file,
        [FromForm] string? title,
        [FromForm] string? folderId,
        ISourceService sources,
        ISourceFolderService folders,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ApiResults.Validation("file", "A file is required (multipart/form-data field 'file').");
        }

        if (file.Length == 0)
        {
            return ApiResults.Validation("file", "The uploaded file is empty.");
        }

        Guid? targetFolder = null;
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            if (!Guid.TryParse(folderId, out var parsed))
            {
                return ApiResults.Validation("folderId", "folderId must be a folder id.");
            }

            targetFolder = parsed;
        }

        if (await IsMissingFolderAsync(targetFolder, folders, cancellationToken))
        {
            return ApiResults.Validation("folderId", MissingFolderMessage);
        }

        var content = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(content, cancellationToken);
        }

        var fileName = Path.GetFileName(file.FileName);
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType;
        var source = await sources.AddFileAsync(fileName, contentType, content, title.TrimToNull(), targetFolder, cancellationToken);
        return Accepted(source, jobs);
    }

    private static async Task<Results<Accepted<SourceDto>, ValidationProblem>> AddTextAsync(
        AddTextSourceRequest request,
        ISourceService sources,
        ISourceFolderService folders,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("title", "A title is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            errors.Add("content", "Content must not be empty.");
        }

        var type = ParseTextFormat(request.Format);
        if (type is null)
        {
            errors.Add("format", "Format must be 'text' or 'markdown'.");
        }

        if (await IsMissingFolderAsync(request.FolderId, folders, cancellationToken))
        {
            errors.Add("folderId", MissingFolderMessage);
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var source = await sources.AddTextAsync(request.Title.Trim(), request.Content, type!.Value, request.FolderId, cancellationToken);
        return Accepted(source, jobs);
    }

    private static async Task<Ok<ReindexResponse>> ReindexAsync(ISourceService sources, CancellationToken cancellationToken)
    {
        var queued = await sources.ReingestAllAsync(cancellationToken);
        return TypedResults.Ok(new ReindexResponse(queued));
    }

    private static async Task<Results<Accepted<SourceDto>, ProblemHttpResult>> ReingestAsync(
        Guid id,
        ISourceService sources,
        JobEngineInfo jobs,
        CancellationToken cancellationToken)
    {
        var source = await sources.ReingestAsync(id, cancellationToken);
        return source is null ? SourceNotFound(id) : Accepted(source, jobs);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ISourceService sources,
        CancellationToken cancellationToken) =>
        await sources.DeleteAsync(id, cancellationToken) ? TypedResults.NoContent() : SourceNotFound(id);

    private static async Task<Results<Ok<ChunkDto[]>, ValidationProblem, ProblemHttpResult>> GetChunksAsync(
        Guid id,
        int? skip,
        int? take,
        ISourceService sources,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();
        if (skip is < 0)
        {
            errors.Add("skip", "skip must not be negative.");
        }

        if (take is < 1 or > SourceService.MaxChunkPageSize)
        {
            errors.Add("take", $"take must be between 1 and {SourceService.MaxChunkPageSize}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        if (await sources.GetAsync(id, cancellationToken) is null)
        {
            return SourceNotFound(id);
        }

        var chunks = await sources.GetChunksAsync(id, skip ?? 0, take ?? DefaultChunkPageSize, cancellationToken);
        return TypedResults.Ok(chunks.Select(c => c.ToChunkDto()).ToArray());
    }

    private static bool TryParseScope(string? scope, out CrawlScope parsed)
    {
        parsed = CrawlScope.Page;
        return string.IsNullOrWhiteSpace(scope) || TryParseName(scope, out parsed);
    }

    private static IEnumerable<string> SplitList(IEnumerable<string?>? values) =>
        values?.SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) ?? [];

    // Not Enum.TryParse, which also accepts numbers and comma-separated flag lists.
    private static bool TryParseName<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Names<TEnum>() where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(name => name.ToLowerInvariant()));

    internal static SourceType? ParseTextFormat(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "text" or "txt" or "plain" or "text/plain" => SourceType.Text,
        "markdown" or "md" or "text/markdown" => SourceType.Markdown,
        _ => null,
    };

    private const string MissingFolderMessage = "The folder does not exist.";

    private static async Task<bool> IsMissingFolderAsync(Guid? folderId, ISourceFolderService folders, CancellationToken cancellationToken) =>
        folderId is { } id && await folders.GetAsync(id, cancellationToken) is null;

    private static Accepted<SourceDto> Accepted(Source source, JobEngineInfo jobs) =>
        TypedResults.Accepted($"/api/sources/{source.Id}", source.ToDto(jobs));

    private static ProblemHttpResult SourceNotFound(Guid id) => ApiResults.NotFound($"Source '{id}' was not found.");
}
