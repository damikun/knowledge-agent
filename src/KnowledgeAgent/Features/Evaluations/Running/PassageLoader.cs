using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Evaluations.Judge;
using KnowledgeAgent.Infrastructure.VectorStore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeAgent.Features.Evaluations.Running;

internal static class PassageLoader
{
    private const int PageSize = 200;
    private const int MaxChunksPerSource = 5_000;

    public static async Task<IReadOnlyList<JudgePassage>> LoadAsync(
        IServiceProvider services,
        IReadOnlyDictionary<string, CitationDto> available,
        CancellationToken cancellationToken)
    {
        var vectorStore = services.GetRequiredService<IVectorStore>();
        var contentByChunk = new Dictionary<Guid, string>();
        foreach (var sourceId in available.Values.Select(citation => citation.SourceId).Distinct())
        {
            var wanted = available.Values.Where(citation => citation.SourceId == sourceId).Select(citation => citation.ChunkId).ToHashSet();
            for (var skip = 0; skip < MaxChunksPerSource && wanted.Count > 0; skip += PageSize)
            {
                var page = await vectorStore.GetChunksAsync(sourceId, skip, PageSize, cancellationToken);
                foreach (var chunk in page.Where(chunk => wanted.Remove(chunk.ChunkId)))
                {
                    contentByChunk[chunk.ChunkId] = chunk.Content;
                }

                if (page.Count < PageSize)
                {
                    break;
                }
            }
        }

        return available.Values
            .OrderBy(citation => int.TryParse(citation.Ref.AsSpan(1), out var number) ? number : int.MaxValue)
            .Select(citation => new JudgePassage(
                citation.Ref,
                citation.SourceTitle,
                contentByChunk.TryGetValue(citation.ChunkId, out var content) ? content : citation.Snippet))
            .ToList();
    }
}
