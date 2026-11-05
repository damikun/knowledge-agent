using KnowledgeAgent.Features.Evaluations.Datasets;
using Microsoft.Extensions.Options;

namespace KnowledgeAgent.Features.Evaluations;

public sealed class BuiltinDatasetCatalog(IOptions<EvaluationOptions> options)
{
    private readonly Lazy<IReadOnlyDictionary<string, EvalDataset>> _datasets = new(() => Load(options.Value));

    public IReadOnlyCollection<EvalDataset> All => _datasets.Value.Values.OrderBy(dataset => dataset.Name, StringComparer.Ordinal).ToList();

    public EvalDataset? Find(string name) => _datasets.Value.TryGetValue(name.Trim(), out var dataset) ? dataset : null;

    private static Dictionary<string, EvalDataset> Load(EvaluationOptions options)
    {
        var datasets = EvalDatasetLoader.LoadEmbedded().ToDictionary(dataset => dataset.Name, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.DatasetsPath))
        {
            foreach (var dataset in EvalDatasetLoader.LoadAll(options.DatasetsPath))
            {
                datasets[dataset.Name] = dataset;
            }
        }

        return datasets;
    }
}
