using System.Text.Json;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;

namespace KnowledgeAgent.Tests.Features.Prompts;

public sealed class DefaultPromptTests
{
    private const string FullOrchestrator = """
        You are Aria, the assistant of the "Acme KB" knowledge portal. You answer questions using the user's knowledge base: web pages, PDFs and notes they added.

        Today's date is 2026-10-22.

        # Knowledge base
        The knowledge base contains 1240 ready source(s) (900 url, 340 pdf) with 18530 searchable passages. 12 more are still being processed. Its contents are not listed here: find what you need with the tools below.

        This conversation is limited to 2 source(s) chosen by the user; searches and lookups only cover those.

        # How to work
        - Search with `search_knowledge_base` before answering questions about the user's content. Use several focused queries for multi-part questions.
        - Use `grep_sources` to find every passage with an exact name, number, code or phrase, and with `output` set to `sources` to list or count the sources that mention it. Search returns only the closest few passages, so it cannot show that something is missing or that a list is complete.
        - Use `find_sources` to look up sources by words in their title, URL or file name: to answer questions about what the knowledge base contains, or to get the ids of documents the user mentions and pass them in `source_ids` to search only those. Results are paged.
        - Use `read_source` when a passage needs more surrounding context.
        - For broad questions that need many searches across sources, delegate to the researcher sub-agent with `delegate_research`, then write the final answer from its findings.
        - Ground factual statements in retrieved passages and cite them inline with their reference tags, for example [S1] or [S2][S3]. Only cite tags that appear in tool results.
        - If the knowledge base does not cover the question, say so, then answer from general knowledge and label that part clearly as not coming from the knowledge base.
        - Keep answers concise and well structured; use Markdown lists or tables when they help.
        - Answer in German.

        # Additional instructions
        Be brief.
        Use tables.

        """;

    private const string EmptyOrchestrator = """
        You are , the assistant of the "" knowledge portal. You answer questions using the user's knowledge base: web pages, PDFs and notes they added.

        Today's date is .

        # Knowledge base
        The knowledge base has no ready sources yet. If the user asks about their documents, explain that they can add web pages, PDFs or text on the Sources page.

        # How to work
        - Search with `search_knowledge_base` before answering questions about the user's content. Use several focused queries for multi-part questions.
        - Use `grep_sources` to find every passage with an exact name, number, code or phrase, and with `output` set to `sources` to list or count the sources that mention it. Search returns only the closest few passages, so it cannot show that something is missing or that a list is complete.
        - Use `find_sources` to look up sources by words in their title, URL or file name: to answer questions about what the knowledge base contains, or to get the ids of documents the user mentions and pass them in `source_ids` to search only those. Results are paged.
        - Use `read_source` when a passage needs more surrounding context.
        - Ground factual statements in retrieved passages and cite them inline with their reference tags, for example [S1] or [S2][S3]. Only cite tags that appear in tool results.
        - If the knowledge base does not cover the question, say so plainly and do not answer from general knowledge.
        - Keep answers concise and well structured; use Markdown lists or tables when they help.
        - Answer in the language of the user's message.

        """;

    private const string FullResearcher = """
        You are the researcher sub-agent of the "Acme KB" knowledge portal. The orchestrator agent, not the user, gives you a research task. Today's date is 2026-10-22.

        Focus on these sources and pass their ids in `source_ids`: "Doc A" id=s1, "Notes" id=s2.

        Work method:
        - Run several targeted `search_knowledge_base` queries (at most 6), rephrasing and narrowing as you learn more. Use `grep_sources` to find every mention of an exact name, number or phrase, and `read_source` for surrounding context when needed.
        - Then return a findings report for the orchestrator: short bullet points, each backed by inline reference tags such as [S4] taken from the tool results. Note gaps, contradictions and open questions.
        - Do not invent facts or reference tags. If nothing relevant exists, say so.
        """;

    private readonly LocalPromptCatalog _catalog = new();
    private readonly HandlebarsPromptRenderer _renderer = new(TimeProvider.System);

    [Fact]
    public void Every_application_prompt_has_an_embedded_default()
    {
        _catalog.Names.ShouldBe(PromptNames.All, ignoreOrder: true);
        foreach (var name in PromptNames.All)
        {
            _catalog.TryGet(name, out var template).ShouldBeTrue();
            template!.Name.ShouldBe(name);
            template.Origin.ShouldBe(PromptOrigin.Local);
            template.Version.ShouldBe(1);
            template.Type.ShouldBe(PromptType.Text);
            template.Labels.ShouldBe(["production"]);
            template.Tags.ShouldBe(["knowledge-agent"]);
            template.Config.ShouldNotBeNull().Count.ShouldBe(0);
            template.Text.ShouldNotBeNullOrWhiteSpace();
            template.Text.ShouldNotEndWith("\n");
            template.Text.ShouldNotContain("\r");
        }
    }

    [Fact]
    public void The_conversation_title_prompt_matches_the_specification()
    {
        Text(PromptNames.ConversationTitle).ShouldBe(
            "Write a short title (at most 6 words) for a conversation that starts with the message below. "
            + "Reply with the title only, without quotes or trailing punctuation.\n\nMessage:\n{{message}}");
    }

    [Fact]
    public void Catalog_returns_independent_copies()
    {
        _catalog.TryGet(PromptNames.OrchestratorSystem, out var first);
        first!.Config!["model"] = "changed";

        _catalog.TryGet(PromptNames.OrchestratorSystem, out var second);

        second!.Config!.ContainsKey("model").ShouldBeFalse();
    }

    [Fact]
    public void Orchestrator_renders_every_section_with_full_variables()
    {
        Render(PromptNames.OrchestratorSystem, FullOrchestratorVariables()).ShouldBe(FullOrchestrator);
    }

    [Fact]
    public void Orchestrator_renders_the_fallback_sections_with_empty_variables()
    {
        Render(PromptNames.OrchestratorSystem, new Dictionary<string, object?>()).ShouldBe(EmptyOrchestrator);
    }

    [Fact]
    public void Orchestrator_reports_sources_still_processing_in_an_empty_knowledge_base_and_hides_optional_sections()
    {
        var variables = FullOrchestratorVariables();
        variables["sourceCount"] = 0;
        variables["chunkCount"] = 0;
        variables["sourceTypes"] = Array.Empty<object>();
        variables["pendingSourceCount"] = 3;
        variables.Remove("scopedSourceCount");
        variables["researcherEnabled"] = false;
        variables["allowGeneralKnowledge"] = false;
        variables["language"] = "";
        variables["instructions"] = null;

        var text = Render(PromptNames.OrchestratorSystem, variables);

        text.ShouldContain("# Knowledge base\nThe knowledge base has no ready sources yet (3 still being processed). If the user asks about their documents");
        text.ShouldNotContain("This conversation is limited to");
        text.ShouldNotContain("delegate_research");
        text.ShouldContain("say so plainly and do not answer from general knowledge.");
        text.ShouldContain("- Answer in the language of the user's message.");
        text.ShouldNotContain("# Additional instructions");
        text.ShouldNotContain("\n\n\n");
    }

    [Fact]
    public void Orchestrator_summary_line_omits_the_type_breakdown_and_processing_note_when_absent()
    {
        var variables = FullOrchestratorVariables();
        variables["sourceCount"] = 5;
        variables["chunkCount"] = 40;
        variables.Remove("sourceTypes");
        variables["pendingSourceCount"] = 0;

        Render(PromptNames.OrchestratorSystem, variables).ShouldContain(
            "The knowledge base contains 5 ready source(s) with 40 searchable passages. Its contents are not listed here: find what you need with the tools below.\n");
    }

    [Fact]
    public void Orchestrator_renders_the_same_from_json_variables()
    {
        // The API passes JSON request variables through unchanged, so they must render like CLR values.
        var json = JsonSerializer.SerializeToElement(FullOrchestratorVariables());
        var variables = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);

        Render(PromptNames.OrchestratorSystem, variables).ShouldBe(FullOrchestrator);
    }

    [Fact]
    public void Researcher_renders_with_full_and_empty_variables()
    {
        var full = new Dictionary<string, object?>
        {
            ["appName"] = "Acme KB",
            ["today"] = "2026-10-22",
            ["focusSources"] = new[] { new { id = "s1", title = "Doc A" }, new { id = "s2", title = "Notes" } },
            ["maxSearches"] = 6,
        };

        Render(PromptNames.ResearcherSystem, full).ShouldBe(FullResearcher);

        var empty = Render(PromptNames.ResearcherSystem, new Dictionary<string, object?>());
        empty.ShouldStartWith("You are the researcher sub-agent of the \"\" knowledge portal.");
        empty.ShouldContain("Today's date is .\n\nWork method:\n");
        empty.ShouldNotContain("Focus on these sources");
        empty.ShouldContain("(at most )");
    }

    [Fact]
    public void Researcher_omits_the_focus_section_for_an_empty_list()
    {
        var text = Render(PromptNames.ResearcherSystem, new Dictionary<string, object?> { ["focusSources"] = new List<object>(), ["maxSearches"] = 4 });

        text.ShouldNotContain("Focus on these sources");
        text.ShouldContain("(at most 4)");
    }

    [Fact]
    public void Conversation_title_renders_the_message()
    {
        Render(PromptNames.ConversationTitle, new Dictionary<string, object?> { ["message"] = "How do I add <PDFs> & notes?" })
            .ShouldEndWith("\n\nMessage:\nHow do I add <PDFs> & notes?");
        Render(PromptNames.ConversationTitle, new Dictionary<string, object?>()).ShouldEndWith("Message:\n");
    }

    private static Dictionary<string, object?> FullOrchestratorVariables() => new()
    {
        ["agentName"] = "Aria",
        ["appName"] = "Acme KB",
        ["today"] = "2026-10-22",
        ["sourceCount"] = 1240,
        ["chunkCount"] = 18530L,
        ["sourceTypes"] = new[] { new { type = "url", count = 900 }, new { type = "pdf", count = 340 } },
        ["pendingSourceCount"] = 12,
        ["scopedSourceCount"] = 2,
        ["researcherEnabled"] = true,
        ["allowGeneralKnowledge"] = true,
        ["language"] = "German",
        ["instructions"] = "Be brief.\nUse tables.",
    };

    private string Text(string name)
    {
        _catalog.TryGet(name, out var template).ShouldBeTrue();
        return template!.Text!;
    }

    private string Render(string name, IReadOnlyDictionary<string, object?> variables) => _renderer.Render(Text(name), variables);
}
