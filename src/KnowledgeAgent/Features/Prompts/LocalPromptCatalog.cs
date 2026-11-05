using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnowledgeAgent.Features.Prompts;

internal sealed class LocalPromptCatalog
{
    internal const string ResourcePrefix = "Prompts/";
    private const string TemplateExtension = ".hbs";
    private const string MetadataExtension = ".json";

    private static readonly IReadOnlyList<string> DefaultLabels = ["production"];
    private static readonly IReadOnlyList<string> DefaultTags = ["knowledge-agent"];

    private readonly Lazy<IReadOnlyDictionary<string, PromptTemplate>> _prompts;

    public LocalPromptCatalog()
        : this(typeof(LocalPromptCatalog).Assembly)
    {
    }

    internal LocalPromptCatalog(Assembly assembly) => _prompts = new(() => Load(assembly));

    public IReadOnlyList<PromptTemplate> All => _prompts.Value.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    public IReadOnlyCollection<string> Names => _prompts.Value.Keys.ToList();

    public bool Contains(string name) => _prompts.Value.ContainsKey(name);

    public bool TryGet(string name, [NotNullWhen(true)] out PromptTemplate? template)
    {
        if (_prompts.Value.TryGetValue(name, out var stored))
        {
            template = stored.Snapshot();
            return true;
        }

        template = null;
        return false;
    }

    private static Dictionary<string, PromptTemplate> Load(Assembly assembly)
    {
        var prompts = new Dictionary<string, PromptTemplate>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(TemplateExtension, StringComparison.Ordinal))
            {
                continue;
            }

            var name = resource[ResourcePrefix.Length..^TemplateExtension.Length];
            var text = NormalizeTemplate(ReadResource(assembly, resource)!);
            var metadata = ReadMetadata(assembly, ResourcePrefix + name + MetadataExtension);
            var type = metadata?["type"] is JsonValue typeValue && typeValue.GetValueKind() == JsonValueKind.String
                && string.Equals(typeValue.GetValue<string>(), "chat", StringComparison.OrdinalIgnoreCase)
                    ? PromptType.Chat
                    : PromptType.Text;

            prompts[name] = new PromptTemplate
            {
                Name = name,
                Version = 1,
                Type = type,
                Text = type == PromptType.Text ? text : null,
                Messages = type == PromptType.Chat ? [new PromptMessage("system", text)] : [],
                Config = metadata?["config"] is JsonObject config ? (JsonObject)config.DeepClone() : new JsonObject(),
                Labels = ReadStrings(metadata, "labels") ?? DefaultLabels,
                Tags = ReadStrings(metadata, "tags") ?? DefaultTags,
                Origin = PromptOrigin.Local,
            };
        }

        return prompts;
    }

    private static string NormalizeTemplate(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static string? ReadResource(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonObject? ReadMetadata(Assembly assembly, string resource)
    {
        var json = ReadResource(assembly, resource);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Embedded prompt metadata '{resource}' is not valid JSON.", ex);
        }
    }

    private static IReadOnlyList<string>? ReadStrings(JsonObject? metadata, string key) => metadata?[key] is JsonArray array
        ? array.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>()).ToList()
        : null;
}
