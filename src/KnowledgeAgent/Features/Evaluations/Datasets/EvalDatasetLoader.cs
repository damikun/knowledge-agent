using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KnowledgeAgent.Features.Evaluations.Datasets;

public static partial class EvalDatasetLoader
{
    public const string DatasetFileName = "dataset.json";
    public const string BaselinesFolder = "baselines";
    internal const string EmbeddedPrefix = "Datasets/";
    internal const int MaxIdLength = 100;

    private static readonly Lazy<IReadOnlyList<EvalDataset>> Embedded = new(() => LoadEmbedded(typeof(EvalDatasetLoader).Assembly));

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<EvalDataset> LoadEmbedded() => Embedded.Value;

    public static IReadOnlyList<EvalDataset> LoadAll(string datasetsRoot) =>
        Directory.Exists(datasetsRoot)
            ? Directory.GetDirectories(datasetsRoot)
                .Where(directory => File.Exists(Path.Combine(directory, DatasetFileName)))
                .Order(StringComparer.Ordinal)
                .Select(LoadDirectory)
                .ToList()
            : [];

    public static EvalDataset LoadDirectory(string path)
    {
        var directory = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path);
        var datasetFile = Path.Combine(directory, DatasetFileName);
        if (!File.Exists(datasetFile))
        {
            throw new FileNotFoundException($"No {DatasetFileName} in '{directory}'.", datasetFile);
        }

        var baselinesDirectory = Path.Combine(directory, BaselinesFolder);
        var baselines = Directory.Exists(baselinesDirectory)
            ? Directory.GetFiles(baselinesDirectory, "*.json").Order(StringComparer.Ordinal)
                .Select(file => (Path.GetFileName(file), File.ReadAllText(file)))
                .ToList()
            : [];

        var root = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Parse(
            File.ReadAllText(datasetFile),
            relativePath =>
            {
                // Corpus paths must stay inside the dataset folder.
                var full = Path.GetFullPath(Path.Combine(directory, relativePath));
                return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? File.ReadAllBytes(full) : null;
            },
            baselines,
            directory);
    }

    internal static IReadOnlyList<EvalDataset> LoadEmbedded(Assembly assembly)
    {
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal))
            .ToDictionary(name => name[EmbeddedPrefix.Length..].Replace('\\', '/'), name => name, StringComparer.Ordinal);

        var datasets = new List<EvalDataset>();
        foreach (var (path, resource) in resources.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var parts = path.Split('/');
            if (parts.Length != 2 || parts[1] != DatasetFileName)
            {
                continue;
            }

            var folder = parts[0] + "/";
            var baselines = resources
                .Where(entry => entry.Key.StartsWith(folder + BaselinesFolder + "/", StringComparison.Ordinal) && entry.Key.EndsWith(".json", StringComparison.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => (Path.GetFileName(entry.Key), Encoding.UTF8.GetString(Read(assembly, entry.Value))))
                .ToList();

            datasets.Add(Parse(
                Encoding.UTF8.GetString(Read(assembly, resource)),
                relativePath => resources.TryGetValue(folder + NormalizeRelative(relativePath), out var name) ? Read(assembly, name) : null,
                baselines,
                directoryPath: null));
        }

        return datasets;
    }

    internal static EvalDataset Parse(
        string json,
        Func<string, byte[]?> readFile,
        IEnumerable<(string FileName, string Json)> baselines,
        string? directoryPath)
    {
        DatasetFile file;
        try
        {
            file = JsonSerializer.Deserialize<DatasetFile>(json, JsonOptions) ?? new DatasetFile();
        }
        catch (JsonException ex)
        {
            throw new EvalDatasetException(directoryPath ?? "?", [$"{DatasetFileName} is not valid JSON: {ex.Message}"]);
        }

        var name = file.Name?.Trim() ?? string.Empty;
        var problems = new List<string>();
        if (!IsSlug(name))
        {
            problems.Add("\"name\" must be 1-100 characters: letters, digits, '.', '-' or '_'.");
        }

        var corpus = new List<EvalCorpusDocument>();
        foreach (var (entry, index) in (file.Corpus ?? []).Select((entry, index) => (entry, index)))
        {
            var relative = entry.File?.Trim();
            if (string.IsNullOrEmpty(relative))
            {
                problems.Add($"corpus[{index}] has no \"file\".");
                continue;
            }

            var content = readFile(NormalizeRelative(relative));
            if (content is null)
            {
                problems.Add($"Corpus file '{relative}' does not exist.");
                continue;
            }

            var fileName = Path.GetFileName(relative);
            if (corpus.Any(document => string.Equals(document.Key, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"Corpus file name '{fileName}' is used twice (items refer to documents by file name).");
                continue;
            }

            corpus.Add(new EvalCorpusDocument(
                fileName,
                string.IsNullOrWhiteSpace(entry.Title) ? fileName : entry.Title.Trim(),
                fileName,
                string.IsNullOrWhiteSpace(entry.Url) ? null : entry.Url.Trim(),
                content));
        }

        var items = new List<EvalItem>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, index) in (file.Items ?? []).Select((entry, index) => (entry, index)))
        {
            var id = entry.Id?.Trim() ?? string.Empty;
            var label = id.Length > 0 ? $"Item '{id}'" : $"items[{index}]";
            if (!IsSlug(id))
            {
                problems.Add($"{label}: \"id\" must be 1-{MaxIdLength} characters: letters, digits, '.', '-' or '_'.");
            }
            else if (!ids.Add(id))
            {
                problems.Add($"{label}: the id is used twice.");
            }

            var question = entry.Question?.Trim() ?? string.Empty;
            if (question.Length == 0)
            {
                problems.Add($"{label}: \"question\" is empty.");
            }

            var answerable = entry.Answerable ?? true;
            var sources = Clean(entry.ExpectedSources);
            var facts = Clean(entry.ExpectedFacts);
            if (!answerable && (sources.Count > 0 || facts.Count > 0))
            {
                problems.Add($"{label}: unanswerable items cannot have expected sources or facts.");
            }

            if (corpus.Count > 0)
            {
                foreach (var source in sources.Where(source => !corpus.Any(document => string.Equals(document.Key, source, StringComparison.OrdinalIgnoreCase))))
                {
                    problems.Add($"{label}: expected source '{source}' is not a corpus file.");
                }
            }

            if (facts.Any(fact => fact.Split('|').Any(alternative => alternative.Trim().Length == 0)))
            {
                problems.Add($"{label}: expected facts must not contain empty alternatives.");
            }

            items.Add(new EvalItem(
                id,
                question,
                answerable,
                sources,
                facts,
                string.IsNullOrWhiteSpace(entry.ReferenceAnswer) ? null : entry.ReferenceAnswer.Trim(),
                Clean(entry.Tags)));
        }

        if (items.Count == 0)
        {
            problems.Add("The dataset has no items.");
        }

        var gate = ParseGate(file.Gate, problems);
        var parsedBaselines = new Dictionary<string, EvalBaseline>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, baselineJson) in baselines)
        {
            var baseline = ParseBaseline(fileName, baselineJson, problems);
            if (baseline is not null)
            {
                parsedBaselines[baseline.Signature] = baseline;
            }
        }

        if (problems.Count > 0)
        {
            throw new EvalDatasetException(name.Length > 0 ? name : directoryPath ?? "?", problems);
        }

        return new EvalDataset
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(file.Description) ? null : file.Description.Trim(),
            Source = EvalDatasetSources.Builtin,
            Corpus = corpus,
            Items = items,
            Gate = gate,
            Baselines = parsedBaselines,
            DirectoryPath = directoryPath,
        };
    }

    internal static EvalBaseline? ParseBaseline(string fileName, string json, List<string> problems)
    {
        BaselineFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BaselineFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            problems.Add($"Baseline '{fileName}' is not valid JSON: {ex.Message}");
            return null;
        }

        var signature = file?.Signature?.Trim();
        if (string.IsNullOrEmpty(signature) || file?.Metrics is null)
        {
            problems.Add($"Baseline '{fileName}' needs \"signature\" and \"metrics\".");
            return null;
        }

        var unknown = file.Metrics.Keys.Where(metric => !EvalMetrics.All.Contains(metric, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            problems.Add($"Baseline '{fileName}' has unknown metrics: {string.Join(", ", unknown)}.");
            return null;
        }

        return new EvalBaseline(signature, new Dictionary<string, double>(file.Metrics, StringComparer.Ordinal), file.Items, file.CreatedAt);
    }

    private static EvalGateConfig ParseGate(GateFile? gate, List<string> problems)
    {
        if (gate is null)
        {
            return EvalGateConfig.Default;
        }

        var minimums = ParseShares(gate.Minimums, "gate.minimums", problems);

        var maxRegression = gate.MaxRegression ?? EvalGateConfig.DefaultMaxRegression;
        if (maxRegression is < 0 or > 1)
        {
            problems.Add("gate.maxRegression must be between 0 and 1.");
        }

        var regressionMetrics = gate.RegressionMetrics?.Select(metric => metric.Trim()).Where(metric => metric.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var metric in regressionMetrics?.Where(metric => !EvalMetrics.All.Contains(metric, StringComparer.Ordinal)) ?? [])
        {
            problems.Add($"gate.regressionMetrics: unknown metric '{metric}'.");
        }

        return new EvalGateConfig(minimums, maxRegression, regressionMetrics)
        {
            Required = ParseMetrics(gate.Required, "gate.required", problems),
            RequiredWithJudge = ParseMetrics(gate.RequiredWithJudge, "gate.requiredWithJudge", problems),
            Tolerances = ParseShares(gate.Tolerances, "gate.tolerances", problems),
            OfflineMinimums = ParseShares(gate.Offline?.Minimums, "gate.offline.minimums", problems),
        };
    }

    private static Dictionary<string, double> ParseShares(Dictionary<string, double>? values, string path, List<string> problems)
    {
        var parsed = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (metric, value) in values ?? [])
        {
            if (!EvalMetrics.All.Contains(metric, StringComparer.Ordinal))
            {
                problems.Add($"{path}: unknown metric '{metric}'.");
            }
            else if (value is < 0 or > 1)
            {
                problems.Add($"{path}.{metric} must be between 0 and 1.");
            }
            else
            {
                parsed[metric] = value;
            }
        }

        return parsed;
    }

    private static List<string> ParseMetrics(List<string>? values, string path, List<string> problems)
    {
        var parsed = (values ?? []).Select(metric => metric.Trim()).Where(metric => metric.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var metric in parsed.Where(metric => !EvalMetrics.All.Contains(metric, StringComparer.Ordinal)))
        {
            problems.Add($"{path}: unknown metric '{metric}'.");
        }

        return parsed.Where(metric => EvalMetrics.All.Contains(metric, StringComparer.Ordinal)).ToList();
    }

    internal static bool IsSlug(string value) => value.Length is > 0 and <= MaxIdLength && SlugPattern().IsMatch(value);

    private static List<string> Clean(IEnumerable<string?>? values) =>
        (values ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static byte[] Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SlugPattern();

    private sealed class DatasetFile
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<CorpusFile>? Corpus { get; set; }
        public List<ItemFile>? Items { get; set; }
        public GateFile? Gate { get; set; }
    }

    private sealed class CorpusFile
    {
        public string? File { get; set; }
        public string? Title { get; set; }
        public string? Url { get; set; }
    }

    private sealed class ItemFile
    {
        public string? Id { get; set; }
        public string? Question { get; set; }
        public bool? Answerable { get; set; }
        public List<string?>? ExpectedSources { get; set; }
        public List<string?>? ExpectedFacts { get; set; }
        public string? ReferenceAnswer { get; set; }
        public List<string?>? Tags { get; set; }
    }

    private sealed class GateFile
    {
        public List<string>? Required { get; set; }
        public List<string>? RequiredWithJudge { get; set; }
        public Dictionary<string, double>? Minimums { get; set; }
        public double? MaxRegression { get; set; }
        public Dictionary<string, double>? Tolerances { get; set; }
        public List<string>? RegressionMetrics { get; set; }
        public OfflineGateFile? Offline { get; set; }
    }

    private sealed class OfflineGateFile
    {
        public Dictionary<string, double>? Minimums { get; set; }
    }

    internal sealed class BaselineFile
    {
        public string? Signature { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public int Items { get; set; }
        public Dictionary<string, double>? Metrics { get; set; }
    }
}
