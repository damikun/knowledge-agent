using KnowledgeAgent.Features.Evaluations.Datasets;
using KnowledgeAgent.Features.Evaluations.Running;
using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Features.Evaluations.Metrics;

public static class ItemScorer
{
    public const int RetrievalDepth = 10;

    public static ItemScore Score(EvalItem item, RetrievalResult retrieval, AgentTurnResult turn, ResolvedSources sources)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(sources);

        var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
        var expected = item.ExpectedSources.Select(reference => sources[reference]).ToList();
        bool IsExpected(Guid sourceId) => expected.Any(ids => ids.Contains(sourceId));
        var measuresSources = item.Answerable && expected.Count > 0;

        var hits = retrieval.Hits.Take(RetrievalDepth).ToList();
        if (measuresSources && retrieval.Error is null)
        {
            metrics[EvalMetrics.RetrievalRecallAt5] = Recall(hits, expected, 5);
            metrics[EvalMetrics.RetrievalRecallAt10] = Recall(hits, expected, RetrievalDepth);
            var firstRelevant = hits.FindIndex(hit => IsExpected(hit.SourceId));
            metrics[EvalMetrics.RetrievalMrrAt10] = firstRelevant < 0 ? 0 : 1.0 / (firstRelevant + 1);
        }

        var retrieved = hits
            .Select((hit, index) => (hit, rank: index + 1))
            .GroupBy(entry => entry.hit.SourceId)
            .Select(group => group.First())
            .Select(entry => new EvalRetrievedSourceDto(entry.rank, entry.hit.SourceTitle, Math.Round(entry.hit.Score, 4), IsExpected(entry.hit.SourceId)))
            .ToList();

        var cited = AnswerText.Citations(turn.Answer);
        var citations = cited
            .Select(reference => turn.Available.TryGetValue(reference, out var citation)
                ? new EvalCitationResultDto(reference, true, citation.SourceTitle, IsExpected(citation.SourceId))
                : new EvalCitationResultDto(reference, false, null, false))
            .ToList();
        var citedSources = cited
            .Where(turn.Available.ContainsKey)
            .Select(reference => turn.Available[reference].SourceId)
            .Distinct()
            .ToList();

        if (cited.Count > 0)
        {
            metrics[EvalMetrics.CitationValidity] = citations.Count(citation => citation.Valid) / (double)cited.Count;
        }

        if (measuresSources)
        {
            var seen = turn.Available.Values.Select(citation => citation.SourceId).ToHashSet();
            metrics[EvalMetrics.AgentSourceRecall] = expected.Count(ids => ids.Overlaps(seen)) / (double)expected.Count;
            metrics[EvalMetrics.CitationSourceRecall] = expected.Count(ids => citedSources.Any(ids.Contains)) / (double)expected.Count;
            if (citedSources.Count > 0)
            {
                metrics[EvalMetrics.CitationPrecision] = citedSources.Count(IsExpected) / (double)citedSources.Count;
            }
        }

        if (item.Answerable && AnswerText.CitationCoverage(turn.Answer) is { } coverage)
        {
            metrics[EvalMetrics.CitationCoverage] = coverage;
        }

        var missingFacts = item.ExpectedFacts.Where(fact => !AnswerText.ContainsFact(turn.Answer, fact)).ToList();
        if (item.ExpectedFacts.Count > 0)
        {
            metrics[EvalMetrics.AnswerFactRecall] = (item.ExpectedFacts.Count - missingFacts.Count) / (double)item.ExpectedFacts.Count;
        }

        // A failed turn produced no answer, which is not the same as declining.
        if (!item.Answerable && !turn.Failed)
        {
            metrics[EvalMetrics.AnswerAbstained] = cited.Count == 0 ? 1 : 0;
        }

        return new ItemScore(metrics, retrieved, citations, missingFacts);
    }

    internal static double Recall(IReadOnlyList<KnowledgeSearchHit> hits, IReadOnlyList<IReadOnlySet<Guid>> expected, int k)
    {
        if (expected.Count == 0)
        {
            return 0;
        }

        var top = hits.Take(k).Select(hit => hit.SourceId).ToHashSet();
        return expected.Count(ids => ids.Overlaps(top)) / (double)expected.Count;
    }
}

public sealed record ItemScore(
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<EvalRetrievedSourceDto> Retrieved,
    IReadOnlyList<EvalCitationResultDto> Citations,
    IReadOnlyList<string> MissingFacts);
