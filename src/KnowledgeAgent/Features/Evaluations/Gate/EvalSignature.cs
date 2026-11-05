using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Evaluations.Running;

namespace KnowledgeAgent.Features.Evaluations.Gate;

public static partial class EvalSignature
{
    public static string Create(EvalRunSettings settings, bool judge)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // The search mode is deliberately left out, so turning hybrid search off shows up as a regression.
        var parts = new List<string>
        {
            Slug(settings.EmbeddingModel ?? "no-embeddings"),
            Slug(settings.ChatModelLabel ?? "no-chat-model"),
        };
        if (settings.RerankModel is not null)
        {
            parts.Add("rerank-" + Slug(settings.RerankModel));
        }

        if (judge && settings.JudgeModel is not null)
        {
            parts.Add("judge-" + Slug(settings.JudgeModel));
        }

        return string.Join("__", parts);
    }

    internal static string Slug(string value) => NonSlug().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9.]+")]
    private static partial Regex NonSlug();
}
