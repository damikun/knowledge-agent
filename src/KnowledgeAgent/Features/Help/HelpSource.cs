using System.Reflection;
using KnowledgeAgent.Infrastructure.Hosting;

namespace KnowledgeAgent.Features.Help;

public sealed record HelpSnapshot(string Version, IReadOnlyList<HelpSection> Sections);

public interface IHelpSource
{
    Task<HelpSnapshot> LoadAsync(CancellationToken cancellationToken);
}

public sealed class HelpOptions
{
    public const string SectionName = "Help";

    // Reserved for the docs site's JSON export; not read yet.
    public string? RemoteUrl { get; set; }
}

internal sealed class BundledHelpSource : IHelpSource
{
    internal const string GuidePrefix = "Help/guide/";
    internal const string DocsPrefix = "Help/docs/";

    private readonly Lazy<HelpSnapshot> _snapshot;

    public BundledHelpSource()
        : this(typeof(BundledHelpSource).Assembly)
    {
    }

    internal BundledHelpSource(Assembly assembly) => _snapshot = new(() => new HelpSnapshot(AppVersion.Of(assembly), Load(assembly)));

    public HelpSnapshot Snapshot => _snapshot.Value;

    public Task<HelpSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot.Value);

    internal static HelpSnapshot FromDocuments(IEnumerable<(string Document, string Markdown, HelpOrigin Origin)> documents, string version = "test") =>
        new(version, documents.SelectMany(document => HelpMarkdown.Parse(document.Document, document.Markdown, document.Origin)).ToList());

    private static List<HelpSection> Load(Assembly assembly)
    {
        var sections = new List<HelpSection>();
        foreach (var resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var origin = resource.StartsWith(GuidePrefix, StringComparison.Ordinal) ? HelpOrigin.Guide
                : resource.StartsWith(DocsPrefix, StringComparison.Ordinal) ? HelpOrigin.Docs
                : (HelpOrigin?)null;
            if (origin is null || !resource.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var prefix = origin == HelpOrigin.Guide ? GuidePrefix : DocsPrefix;
            var document = resource[prefix.Length..^3].ToLowerInvariant();
            sections.AddRange(HelpMarkdown.Parse(document, reader.ReadToEnd(), origin.Value));
        }

        return sections;
    }
}
