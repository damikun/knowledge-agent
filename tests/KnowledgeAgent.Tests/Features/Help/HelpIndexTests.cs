using System.Threading.Channels;
using KnowledgeAgent.Features.Chat;
using KnowledgeAgent.Features.Chat.Agents;
using KnowledgeAgent.Features.Chat.Tools;
using KnowledgeAgent.Features.Help;

namespace KnowledgeAgent.Tests.Features.Help;

public sealed class HelpIndexTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly HelpSnapshot Shipped = new BundledHelpSource().Snapshot;

    [Fact]
    public void The_shipped_help_holds_the_guide_and_the_readme()
    {
        var sections = Shipped.Sections;
        sections.ShouldContain(section => section.Origin == HelpOrigin.Guide && section.Document == "connect-a-model");
        sections.ShouldContain(section => section.Origin == HelpOrigin.Docs && section.Document == "readme");
        sections.Select(section => section.Id).ShouldBeUnique();
        sections.Select(section => section.Key).ShouldBeUnique();
        Shipped.Version.ShouldNotBeNullOrWhiteSpace();

        sections.Where(section => section.Origin == HelpOrigin.Guide).ShouldAllBe(section => section.Link != null && section.Link.StartsWith('/'));
        sections.Where(section => section.Origin == HelpOrigin.Guide).ShouldAllBe(section => section.Summary.Length > 0);
    }

    [Fact]
    public void The_contents_list_every_section_with_its_id_title_and_first_sentence()
    {
        var index = new HelpIndex(BundledHelpSource.FromDocuments([
            ("models", "# Connect a model\n\nThe assistant needs a model. More text here. [Models](/models)\n\n## Add an API key\n```bash\nexport KEY=1\n```\nOpen [AI models](/models#providers), e.g. for Anthropic, and paste the key. Then save.", HelpOrigin.Guide),
            ("ops", "# Operations\n\n## Backups\n| Table | Kept |\n|---|---|\n| sources | always |\n\nBack up the database daily.", HelpOrigin.Docs),
        ]));

        var lines = index.Contents.Split('\n');

        lines.ShouldContain("## Guide (how to use the portal)");
        lines.ShouldContain("### Connect a model");
        lines.ShouldContain("- `guide/models` Connect a model: The assistant needs a model.");
        lines.ShouldContain("- `guide/models#add-an-api-key` Add an API key: Open AI models, e.g. for Anthropic, and paste the key.");
        lines.ShouldContain("## Repository docs (for whoever runs the installation)");
        lines.ShouldContain(line => line.StartsWith("- `docs/ops#backups` Backups: ", StringComparison.Ordinal));
        index.Contents.IndexOf("## Guide", StringComparison.Ordinal).ShouldBeLessThan(index.Contents.IndexOf("## Repository docs", StringComparison.Ordinal));
        index.Contents.ShouldNotContain("export KEY");

        var shipped = new HelpIndex(Shipped);
        shipped.Sections.ShouldAllBe(section => shipped.Contents.Contains($"`{section.Id}`"));
        HelpMarkdown.Summary(string.Join(' ', Enumerable.Repeat("word", 100))).Length.ShouldBeLessThanOrEqualTo(HelpMarkdown.MaxSummaryChars + 1);
    }

    [Fact]
    public void A_section_is_found_by_its_id_and_an_unknown_id_by_nothing()
    {
        var index = new HelpIndex(Shipped);

        index.Find("guide/connect-a-model#add-an-api-key").ShouldNotBeNull().Title.ShouldBe("Add an API key");
        index.Find(" `GUIDE/connect-a-model#add-an-api-key` ").ShouldNotBeNull().Link.ShouldBe("/models#providers");
        index.Find("guide/nothing-here").ShouldBeNull();
        index.Find("").ShouldBeNull();
        index.Find(null).ShouldBeNull();
    }

    [Fact]
    public async Task Reading_a_section_returns_its_markdown_with_portal_links_and_cites_it()
    {
        var index = new HelpIndex(Shipped);
        var tool = new ReadHelpSectionTool(index);
        var citations = new CitationRegistry();

        var result = await tool.InvokeAsync(Invocation(new() { ["id"] = "guide/connect-a-model#add-an-api-key" }, citations), Ct);

        result.IsError.ShouldBeFalse();
        result.Text.ShouldStartWith("[S1] Connect a model > Add an API key (id: guide/connect-a-model#add-an-api-key, portal page: /models#providers)");
        result.Text.ShouldContain("](/models");
        var citation = result.Citations.ShouldNotBeNull().ShouldHaveSingleItem();
        (citation.Ref, citation.SourceUrl).ShouldBe(("S1", "/models#providers"));

        var again = await new SearchHelpTool(index, page: null).InvokeAsync(Invocation(new() { ["query"] = "add an API key" }, citations), Ct);
        again.Text.ShouldContain("[S1] Connect a model > Add an API key (id: guide/connect-a-model#add-an-api-key");

        var unknown = await tool.InvokeAsync(Invocation(new() { ["id"] = "guide/made-up" }, citations), Ct);
        unknown.IsError.ShouldBeTrue();
        unknown.Text.ShouldContain("No help section has the id \"guide/made-up\"");
        unknown.Text.ShouldContain(SearchHelpTool.ToolName);
        unknown.Citations.ShouldBeNull();
    }

    [Fact]
    public void Keyword_search_finds_the_guide_section_for_a_question()
    {
        var index = new HelpIndex(Shipped);

        var hits = index.Search("How do I add an API key?", 5, page: null);

        hits.ShouldNotBeEmpty();
        var top = hits[0].Section;
        top.Document.ShouldBe("connect-a-model");
        top.Title.ShouldBe("Add an API key");
        top.Link.ShouldBe("/models#providers");
        hits.Select(hit => hit.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void The_guide_outranks_the_repository_docs_and_the_current_page_breaks_ties()
    {
        var index = new HelpIndex(BundledHelpSource.FromDocuments([
            ("ops", "# Operations\n\n## Snippet\nThe widget snippet is a script tag. [x](/jobs)", HelpOrigin.Docs),
            ("widgets", "# Widgets\n\n## Snippet\nThe widget snippet is a script tag. [Widgets](/widgets)", HelpOrigin.Guide),
            ("home", "# Home\n\n## Snippet\nThe widget snippet is a script tag. [Home](/home)", HelpOrigin.Guide),
        ]));

        index.Search("widget snippet", 3, page: null).Last().Section.Origin.ShouldBe(HelpOrigin.Docs);
        index.Search("widget snippet", 3, page: "/home")[0].Section.Document.ShouldBe("home");
        index.Search("widget snippet", 3, page: "/widgets/new")[0].Section.Document.ShouldBe("widgets");
        index.Search("the and of", 3, page: null).ShouldBeEmpty("stop words alone match nothing");
    }

    [Fact]
    public async Task The_index_is_rebuilt_from_whatever_the_source_returns()
    {
        var source = new SwitchingSource(BundledHelpSource.FromDocuments([("a", "# A\n\nFirst help. [Home](/home)", HelpOrigin.Guide)], version: "1.0.0"));
        var library = new HelpLibrary(source);

        var first = await library.GetIndexAsync(Ct);
        (first.Version, first.Sections.Count).ShouldBe(("1.0.0", 1));
        (await library.GetIndexAsync(Ct)).ShouldBeSameAs(first, "the index is built once");

        source.Next = BundledHelpSource.FromDocuments([("b", "# B\n\nSecond help.\n\n## More\nAnd more. [Home](/home)", HelpOrigin.Guide)], version: "1.1.0");
        var second = await library.ReloadAsync(Ct);

        (second.Version, second.Sections.Count).ShouldBe(("1.1.0", 2));
        second.Find("guide/a").ShouldBeNull();
        second.Find("guide/b#more").ShouldNotBeNull();
        (await library.GetIndexAsync(Ct)).ShouldBeSameAs(second);
    }

    [Fact]
    public void Headings_in_code_blocks_stay_text_and_long_sections_are_split()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("words", 200));
        var markdown = $"# Guide\n\nIntro. [Home](/home)\n\n## Steps\n```bash\n# not a heading\n```\n\n### Details\n{paragraph}\n\n{paragraph}\n\n{paragraph}";

        var sections = HelpMarkdown.Parse("guide", markdown, HelpOrigin.Guide);

        sections[0].Title.ShouldBe("Guide");
        sections[0].Links.ShouldBe(["/home"]);
        sections[1].Title.ShouldBe("Steps");
        sections[1].Text.ShouldContain("# not a heading");
        sections[1].Links.ShouldBeEmpty();
        sections.Where(section => section.Title == "Details").Count().ShouldBeGreaterThan(1);
        sections.Single(section => section.Id == "guide/guide#details").Path.ShouldBe("Guide > Steps > Details");
        sections.ShouldAllBe(section => section.Link == "/home");
        HelpMarkdown.Plain("See **[Models](/models#providers)** and `x`.").ShouldBe("See Models and x.");
    }

    private static ToolInvocation Invocation(Dictionary<string, object?> arguments, CitationRegistry citations)
    {
        var channel = Channel.CreateUnbounded<ChatStreamEvent>();
        var turn = new AgentTurnContext(Guid.NewGuid(), Guid.NewGuid(), [], citations, channel.Writer);
        return new ToolInvocation("call_1", new ToolArguments(arguments), turn, "help", null!, null!);
    }

    private sealed class SwitchingSource(HelpSnapshot first) : IHelpSource
    {
        public HelpSnapshot Next { get; set; } = first;

        public Task<HelpSnapshot> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Next);
    }
}
