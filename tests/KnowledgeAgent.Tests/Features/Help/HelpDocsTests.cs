using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Help;

namespace KnowledgeAgent.Tests.Features.Help;

// Every help file has a title and ships in the assembly, and every portal link matches a route in web/src/app/routes.tsx.
public sealed partial class HelpDocsTests
{
    private static readonly string Root = FindRoot();
    private static readonly string HelpFolder = Path.Combine(Root, "docs", "help");

    public static TheoryData<string> HelpFiles() => new(Directory.GetFiles(HelpFolder, "*.md").Select(Path.GetFileName).Order(StringComparer.Ordinal)!);

    [Theory]
    [MemberData(nameof(HelpFiles))]
    public void Every_help_file_has_a_title_and_links_only_to_portal_routes(string file)
    {
        var markdown = File.ReadAllText(Path.Combine(HelpFolder, file));

        HelpMarkdown.Title(markdown).ShouldNotBeNullOrWhiteSpace($"{file} needs a # title on its first heading");
        markdown.ShouldNotContain("—", customMessage: $"{file}: plain words, no em dashes");
        var routes = PortalRoutes();
        var links = HelpMarkdown.PortalLinks(markdown).ToList();
        links.ShouldNotBeEmpty($"{file} should link to the portal page it is about");
        foreach (var link in links)
        {
            var path = link.Split('?', '#')[0].Trim('/');
            routes.Any(route => route.IsMatch(path)).ShouldBeTrue($"{file} links to {link}, which is no route in web/src/app/routes.tsx");
        }
    }

    [Fact]
    public void Every_help_file_ships_with_the_application()
    {
        var shipped = new BundledHelpSource().Snapshot.Sections.Where(section => section.Origin == HelpOrigin.Guide).Select(section => section.Document).ToHashSet();

        foreach (var file in Directory.GetFiles(HelpFolder, "*.md"))
        {
            shipped.ShouldContain(Path.GetFileNameWithoutExtension(file).ToLowerInvariant());
        }
    }

    [Fact]
    public void The_route_parser_reads_the_portal_routes()
    {
        var routes = PortalRoutes();

        routes.ShouldContain(route => route.IsMatch("agents/new"));
        routes.ShouldContain(route => route.IsMatch("widgets"));
        routes.ShouldContain(route => route.IsMatch("widgets/new"));
        routes.ShouldNotContain(route => route.IsMatch("settings/system"));
    }

    // Route paths are relative to "/"; ":name" is one segment and ":name?" an optional one.
    private static List<Regex> PortalRoutes()
    {
        var source = File.ReadAllText(Path.Combine(Root, "web", "src", "app", "routes.tsx"));
        return RoutePath().Matches(source)
            .Select(match => match.Groups[1].Value.Trim('/'))
            .Where(path => path.Length > 0 && path != "*")
            .Select(path => new Regex(
                "^" + string.Join(string.Empty, path.Split('/').Select((segment, index) =>
                {
                    var slash = index == 0 ? string.Empty : "/";
                    return segment.StartsWith(':')
                        ? segment.EndsWith('?') ? $"(?:{slash}[^/]+)?" : $"{slash}[^/]+"
                        : slash + Regex.Escape(segment);
                })) + "$",
                RegexOptions.CultureInvariant))
            .ToList();
    }

    private static string FindRoot([CallerFilePath] string path = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KnowledgeAgent.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [GeneratedRegex(@"path:\s*'([^']*)'")]
    private static partial Regex RoutePath();
}
