using System.Collections.Concurrent;
using KnowledgeAgent.Features.Prompts.Langfuse;
using KnowledgeAgent.Features.Prompts.Rendering;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Prompts;

internal sealed partial class PromptEditor(
    LangfuseClient langfuse,
    PromptService prompts,
    IPromptTemplateRenderer renderer,
    IOptions<PromptOptions> options,
    ILogger<PromptEditor> logger)
{
    public const int MaxVersionsListed = 20;
    public const int MaxCommitMessageLength = 300;

    private const string LatestLabel = "latest";
    private const int FetchConcurrency = 4;
    private static readonly HashSet<string> Roles = new(["system", "user", "assistant"], StringComparer.Ordinal);

    public bool Enabled => prompts.UsesLangfuse;

    public string Label => string.IsNullOrWhiteSpace(options.Value.Label) ? LangfuseClient.DefaultLabel : options.Value.Label.Trim();

    public async Task<PromptVersionsDto?> ListVersionsAsync(string name, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new PromptVersionsDto(name, Editable: false, Label, LiveVersion: null, TotalVersions: 0, []);
        }

        var meta = await langfuse.GetPromptMetaAsync(name, cancellationToken).ConfigureAwait(false);
        if (meta is null || meta.Versions.Count == 0)
        {
            return null;
        }

        var live = await langfuse.GetPromptAsync(name, Label, cancellationToken: cancellationToken).ConfigureAwait(false);
        var wanted = meta.Versions.Distinct().OrderDescending().Take(MaxVersionsListed).ToList();
        if (live is not null && !wanted.Contains(live.Version))
        {
            wanted.Add(live.Version);
        }

        var fetched = new ConcurrentBag<PromptTemplate>();
        var parallelism = new ParallelOptions { MaxDegreeOfParallelism = FetchConcurrency, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(wanted, parallelism, async (version, token) =>
        {
            if (await langfuse.GetPromptAsync(name, version: version, cancellationToken: token).ConfigureAwait(false) is { } template)
            {
                fetched.Add(template);
            }
        }).ConfigureAwait(false);

        var versions = fetched
            .OrderByDescending(template => template.Version)
            .Select(template => ToDto(template, template.Version == live?.Version))
            .ToList();
        return new PromptVersionsDto(name, Editable: true, Label, live?.Version, meta.Versions.Distinct().Count(), versions);
    }

    public Dictionary<string, string[]> Validate(PromptTemplate source, SavePromptDraftRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (source.Type == PromptType.Chat)
        {
            if (request.Messages is not { Count: > 0 } messages)
            {
                errors["messages"] = ["A chat prompt needs at least one message."];
                return errors;
            }

            if (messages.Sum(message => message.Content?.Length ?? 0) > PromptEndpoints.MaxTemplateLength)
            {
                errors["messages"] = [$"The messages must not exceed {PromptEndpoints.MaxTemplateLength} characters in total."];
                return errors;
            }

            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                if (message.Role is null || !Roles.Contains(message.Role))
                {
                    errors[$"messages[{index}].role"] = ["The role must be system, user or assistant."];
                }

                if (TemplateError(message.Content ?? string.Empty) is { } error)
                {
                    errors[$"messages[{index}].content"] = [error];
                }
            }
        }
        else if (request.Text is null)
        {
            errors["text"] = ["A text prompt needs its template."];
        }
        else if (request.Text.Length > PromptEndpoints.MaxTemplateLength)
        {
            errors["text"] = [$"The template must not exceed {PromptEndpoints.MaxTemplateLength} characters."];
        }
        else if (TemplateError(request.Text) is { } error)
        {
            errors["text"] = [error];
        }

        if (request.CommitMessage is { Length: > MaxCommitMessageLength })
        {
            errors["commitMessage"] = [$"The note must not exceed {MaxCommitMessageLength} characters."];
        }

        if (errors.Count == 0 && SameContent(source, request))
        {
            errors[source.Type == PromptType.Chat ? "messages" : "text"] = [$"Nothing changed since version {source.Version}."];
        }

        return errors;
    }

    public async Task<PromptTemplate?> GetSourceAsync(string name, int? baseVersion, CancellationToken cancellationToken)
    {
        if (baseVersion is { } version)
        {
            return await langfuse.GetPromptAsync(name, version: version, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await langfuse.GetPromptAsync(name, Label, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? await langfuse.GetPromptAsync(name, LatestLabel, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<PromptVersionDto> SaveDraftAsync(PromptTemplate source, SavePromptDraftRequest request, string author, CancellationToken cancellationToken)
    {
        var draft = source with
        {
            Version = 0,
            Text = source.Type == PromptType.Chat ? null : request.Text,
            Messages = source.Type == PromptType.Chat ? request.Messages!.Select(message => new PromptMessage(message.Role, message.Content ?? string.Empty)).ToArray() : [],
        };

        var commit = CommitMessage(request.CommitMessage, author);
        var created = await langfuse.CreatePromptAsync(LangfuseCreatePromptRequest.FromTemplate(draft, [], commit), cancellationToken).ConfigureAwait(false);
        prompts.Invalidate(source.Name);
        LogDraftSaved(logger, source.Name, created.Version, source.Version, author);
        return ToDto(created, live: false);
    }

    public async Task<PublishPromptResponse?> PublishAsync(string name, PublishPromptRequest request, string author, CancellationToken cancellationToken)
    {
        var target = await langfuse.GetPromptAsync(name, version: request.Version, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return null;
        }

        var live = await langfuse.GetPromptAsync(name, Label, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (request.ExpectedLiveVersion is { } expected && live?.Version != expected)
        {
            throw new PromptEditConflictException(live is null
                ? $"No version of '{name}' is live any more. Reload to see the versions."
                : $"Version {live.Version} of '{name}' went live meanwhile. Reload to see it before you publish.");
        }

        if (live?.Version == target.Version)
        {
            return new PublishPromptResponse(name, Label, target.Version, live.Version);
        }

        // The version keeps its other labels; "latest" belongs to Langfuse.
        var labels = target.Labels
            .Where(label => !string.Equals(label, LatestLabel, StringComparison.Ordinal))
            .Append(Label)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        await langfuse.UpdateLabelsAsync(name, target.Version, labels, cancellationToken).ConfigureAwait(false);
        prompts.Invalidate(name);
        LogPublished(logger, name, Label, live?.Version, target.Version, author);
        return new PublishPromptResponse(name, Label, target.Version, live?.Version);
    }

    internal static string CommitMessage(string? note, string author)
    {
        var text = string.IsNullOrWhiteSpace(note) ? "Edited in the portal" : note.Trim().ReplaceLineEndings(" ");
        return $"{text} (by {author})";
    }

    private string? TemplateError(string template)
    {
        try
        {
            renderer.Validate(template);
            return null;
        }
        catch (PromptRenderException exception)
        {
            return exception.Message;
        }
    }

    private static bool SameContent(PromptTemplate source, SavePromptDraftRequest request) => source.Type == PromptType.Chat
        ? request.Messages is { } messages
            && messages.Count == source.Messages.Count
            && messages.Zip(source.Messages).All(pair => pair.First.Role == pair.Second.Role && (pair.First.Content ?? string.Empty) == pair.Second.Content)
        : string.Equals(request.Text, source.Text ?? string.Empty, StringComparison.Ordinal);

    private PromptVersionDto ToDto(PromptTemplate template, bool live) => new(
        template.Version,
        template.Type.ToContractValue(),
        template.Type == PromptType.Chat ? null : template.Text ?? string.Empty,
        template.Messages.Select(message => new PromptMessageDto(message.Role, message.Content)).ToArray(),
        template.Config,
        template.Labels,
        template.CommitMessage,
        live || template.Labels.Contains(Label, StringComparer.Ordinal));

    [LoggerMessage(EventId = 2040, Level = LogLevel.Information, Message = "Audit: prompt {PromptName} version {PromptVersion} saved as a draft from version {BaseVersion} by {Author}")]
    private static partial void LogDraftSaved(ILogger logger, string promptName, int promptVersion, int baseVersion, string author);

    [LoggerMessage(EventId = 2041, Level = LogLevel.Information, Message = "Audit: prompt {PromptName} label {Label} moved from version {PreviousVersion} to {PromptVersion} by {Author}")]
    private static partial void LogPublished(ILogger logger, string promptName, string label, int? previousVersion, int promptVersion, string author);
}

public sealed class PromptEditConflictException(string message) : Exception(message);
