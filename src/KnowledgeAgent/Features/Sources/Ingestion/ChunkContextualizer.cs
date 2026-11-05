using System.Text;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Sources.Chunking;
using KnowledgeAgent.Infrastructure.AI;
using KnowledgeAgent.Infrastructure.AI.Chat;
using KnowledgeAgent.Infrastructure.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Sources.Ingestion;

// Contextual retrieval: a passage on its own often lacks what it is about ("the limit is 30 days" of what?). Each chunk
// gets a short context, its document title and section path and, when a model is set up for it, a sentence or two
// written by that model. Search embeds and indexes the context with the chunk; citations still show only the chunk.
internal interface IChunkContextualizer
{
    Task<IReadOnlyList<string>> ContextualizeAsync(Source source, string documentText, IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken);
}

internal sealed partial class ChunkContextualizer(
    IServiceProvider services,
    IOptionsMonitor<IngestionOptions> options,
    ILogger<ChunkContextualizer> logger) : IChunkContextualizer
{
    public const string ProfileName = "contextualizer";
    public const int MaxContextLength = 600;

    // The part of the document sent with each chunk, centred on it: enough to place it, without paying for the whole
    // document on every call.
    internal const int DocumentWindowChars = 24_000;

    internal const string Instructions =
        "You place passages in the context of their document so that search can find them. "
        + "Answer with one or two short sentences of context and nothing else.";

    public async Task<IReadOnlyList<string>> ContextualizeAsync(Source source, string documentText, IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (settings.Contextualize == ContextualizeMode.Off)
        {
            return chunks.Select(_ => string.Empty).ToList();
        }

        var headings = HeadingContexts(source.Title, documentText, chunks);
        if (settings.Contextualize == ContextualizeMode.Headings || !await ShouldAskModelAsync(settings, cancellationToken))
        {
            return headings;
        }

        ChatModelHandle model;
        try
        {
            model = await services.GetRequiredService<IChatClientFactory>().CreateAsync(ProfileName, cancellationToken: cancellationToken);
        }
        catch (ModelNotConfiguredException ex)
        {
            LogNoModel(logger, ex.Message);
            return headings;
        }

        var contexts = headings.ToArray();
        var limit = Math.Min(chunks.Count, Math.Max(0, settings.MaxModelContextChunks));
        var failures = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, limit),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, settings.ModelContextParallelism), CancellationToken = cancellationToken },
            async (index, token) =>
            {
                try
                {
                    var situated = await SituateAsync(model, documentText, chunks[index], token);
                    if (situated.Length > 0)
                    {
                        contexts[index] = Combine(headings[index], situated);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    // One failed chunk keeps its heading context; the source is still ingested.
                    if (Interlocked.Increment(ref failures) == 1)
                    {
                        LogChunkFailed(logger, ex, source.Id);
                    }
                }
            });

        LogContextualized(logger, source.Id, limit - failures, chunks.Count, model.Profile.Model);
        return contexts;
    }

    // "Title > Section > Subsection (page 3)", from the Markdown headings above each chunk.
    internal static IReadOnlyList<string> HeadingContexts(string title, string text, IReadOnlyList<TextChunk> chunks)
    {
        var headings = Heading().Matches(text)
            .Select(match => (Position: match.Index, Level: match.Groups[1].Length, Text: Clean(match.Groups[2].Value)))
            .Where(heading => heading.Text.Length > 0)
            .ToList();
        var contexts = new List<string>(chunks.Count);
        var path = new List<(int Level, string Text)>();
        var next = 0;
        var searchFrom = 0;
        foreach (var chunk in chunks)
        {
            var probe = chunk.Content.Length > 80 ? chunk.Content[..80] : chunk.Content;
            var position = probe.Length == 0 ? -1 : text.IndexOf(probe, searchFrom, StringComparison.Ordinal);
            if (position >= 0)
            {
                searchFrom = position;
            }
            else
            {
                position = searchFrom;
            }

            // Headings that start inside the chunk's first line count as its section.
            while (next < headings.Count && headings[next].Position <= position + probe.Length)
            {
                var heading = headings[next++];
                path.RemoveAll(entry => entry.Level >= heading.Level);
                path.Add((heading.Level, heading.Text));
            }

            contexts.Add(Describe(title, path.Select(entry => entry.Text), chunk.Page));
        }

        return contexts;
    }

    internal static string Combine(string headings, string situated)
    {
        var combined = headings.Length == 0 ? situated : $"{headings}. {situated}";
        return combined.Length > MaxContextLength ? combined[..MaxContextLength] : combined;
    }

    // The document around the chunk, at most DocumentWindowChars long.
    internal static string Window(string documentText, string chunk)
    {
        if (documentText.Length <= DocumentWindowChars)
        {
            return documentText;
        }

        var probe = chunk.Length > 80 ? chunk[..80] : chunk;
        var position = Math.Max(0, documentText.IndexOf(probe, StringComparison.Ordinal));
        var start = Math.Clamp(position - (DocumentWindowChars / 2), 0, documentText.Length - DocumentWindowChars);
        return documentText.Substring(start, DocumentWindowChars);
    }

    private async Task<bool> ShouldAskModelAsync(IngestionOptions settings, CancellationToken cancellationToken)
    {
        if (settings.Contextualize == ContextualizeMode.Model)
        {
            return true;
        }

        // Auto: only once someone chose a model for the profile, so ingestion does not start paying for calls unasked.
        if (services.GetService<IModelProfileResolver>() is not { } resolver)
        {
            return false;
        }

        var profile = (await resolver.ListAsync(cancellationToken)).FirstOrDefault(state => state.Name == ProfileName);
        return profile is not null && (!string.IsNullOrWhiteSpace(profile.Override?.Model) || !string.IsNullOrWhiteSpace(profile.Configured.Model));
    }

    private static async Task<string> SituateAsync(ChatModelHandle model, string documentText, TextChunk chunk, CancellationToken cancellationToken)
    {
        var prompt = new StringBuilder()
            .Append("<document>\n").Append(Window(documentText, chunk.Content)).Append("\n</document>\n\n")
            .Append("Here is the passage to place within the document:\n<passage>\n").Append(chunk.Content).Append("\n</passage>\n\n")
            .Append("Give a short, succinct context that places this passage within the document, to improve search retrieval of it. ")
            .Append("Answer only with the context.")
            .ToString();
        var response = await model.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, Instructions), new ChatMessage(ChatRole.User, prompt)],
            model.CreateOptions(),
            cancellationToken);
        var text = Whitespace().Replace(response.Text ?? string.Empty, " ").Trim();
        return text.Length > MaxContextLength ? text[..MaxContextLength] : text;
    }

    private static string Describe(string title, IEnumerable<string> path, int? page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title.Trim());
        }

        foreach (var section in path)
        {
            if (!parts.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(section);
            }
        }

        var context = string.Join(" > ", parts);
        if (page is { } number)
        {
            context += $" (page {number})";
        }

        return context.Length > MaxContextLength ? context[..MaxContextLength] : context;
    }

    private static string Clean(string heading) => Whitespace().Replace(heading.Replace("*", string.Empty, StringComparison.Ordinal).Trim().TrimEnd('#').Trim(), " ");

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [LoggerMessage(Level = LogLevel.Information, Message = "No model for contextual retrieval ({Reason}); chunks get their title and section only")]
    private static partial void LogNoModel(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A chunk of source {SourceId} kept its heading context because the contextualizer failed")]
    private static partial void LogChunkFailed(ILogger logger, Exception exception, Guid sourceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Source {SourceId}: {Contextualized} of {ChunkCount} chunks got a context from {Model}")]
    private static partial void LogContextualized(ILogger logger, Guid sourceId, int contextualized, int chunkCount, string model);
}
