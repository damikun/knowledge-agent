using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;

namespace KnowledgeAgent.Tests.Features.Prompts.Rendering;

public sealed class TemplateVariableExtractorTests
{
    private readonly HandlebarsPromptRenderer _renderer = new(TimeProvider.System);

    [Theory]
    [InlineData("Hello {{name}}", new[] { "name" })]
    [InlineData("{{user.name}} {{user.address.city}} {{order/id}}", new[] { "order", "user" })]
    [InlineData("{{#if active}}x{{/if}} {{#unless done}}y{{/unless}}", new[] { "active", "done" })]
    [InlineData("{{#each items}}{{/each}} {{#with profile}}{{/with}}", new[] { "items", "profile" })]
    [InlineData("{{b}} {{a}} {{b}} {{a.x}}", new[] { "a", "b" })]
    [InlineData("{{{raw}}} {{~trimmed~}} {{~#if cond~}}{{~/if~}}", new[] { "cond", "raw", "trimmed" })]
    [InlineData("{{[odd key]}} {{data.[0]}}", new[] { "data", "odd key" })]
    public void Collects_top_level_names(string template, string[] expected)
    {
        _renderer.ExtractVariables(template).ShouldBe(expected);
    }

    [Fact]
    public void Includes_helper_arguments_hash_values_and_subexpressions_but_not_helpers()
    {
        const string template = """
            {{upper name}} {{date}} {{date published "yyyy"}} {{truncate text 10}} {{join tags ", "}}
            {{#if (eq tone "formal")}}{{/if}} {{#if (and (gt level 2) (not muted))}}{{/if}}
            {{json payload indented=pretty}} {{lookup map key}}
            """;

        _renderer.ExtractVariables(template).ShouldBe(
            ["key", "level", "map", "muted", "name", "payload", "pretty", "published", "tags", "text", "tone"]);
    }

    [Fact]
    public void Skips_names_relative_to_each_and_with_items()
    {
        const string template = """
            {{#each items}}{{title}} {{this.id}} {{./url}} {{../owner}} {{@root.app}} {{#if url}}{{url}}{{/if}}{{/each}}
            {{#with user}}{{name}} {{../company}}{{/with}}
            {{#each groups}}{{#each members}}{{name}} {{../../team}}{{/each}}{{/each}}
            """;

        _renderer.ExtractVariables(template).ShouldBe(["app", "company", "groups", "items", "owner", "team", "user"]);
    }

    [Fact]
    public void The_else_branch_of_each_uses_the_outer_context()
    {
        _renderer.ExtractVariables("{{#each items}}{{title}}{{else}}{{emptyText}}{{/each}} {{after}}")
            .ShouldBe(["after", "emptyText", "items"]);
    }

    [Fact]
    public void Else_if_chains_are_scanned()
    {
        _renderer.ExtractVariables("{{#if a}}{{x}}{{else if b}}{{y}}{{else}}{{z}}{{/if}}").ShouldBe(["a", "b", "x", "y", "z"]);
    }

    [Fact]
    public void Skips_data_variables_keywords_literals_and_this()
    {
        _renderer.ExtractVariables("{{@index}} {{@key}} {{this}} {{.}} {{true}} {{null}} {{eq 1 2}} {{eq \"a\" 'b'}} {{default x 3.5}}")
            .ShouldBe(["x"]);
    }

    [Fact]
    public void Skips_comments_raw_blocks_partials_and_escaped_tags()
    {
        const string template = """
            {{! {{hidden}} }} {{!-- {{alsoHidden}} }} --}} \{{literal}} {{{{raw}}}}{{rawContent}}{{{{/raw}}}} {{> partial}} {{shown}}
            """;

        _renderer.ExtractVariables(template).ShouldBe(["shown"]);
    }

    [Fact]
    public void Block_params_and_mustache_sections_are_understood()
    {
        _renderer.ExtractVariables("{{#each items as |item index|}}{{item.name}} {{index}}{{/each}} {{#section}}{{inner}}{{/section}}")
            .ShouldBe(["items", "section"]);
    }

    [Fact]
    public void Malformed_templates_do_not_throw()
    {
        _renderer.ExtractVariables("{{#if x}} {{y}} {{unclosed").ShouldBe(["x", "y"]);
        _renderer.ExtractVariables("{{/if}} {{z}} {{/each}}").ShouldBe(["z"]);
        _renderer.ExtractVariables("{{eq a \"unterminated}}").ShouldBe([]);
        _renderer.ExtractVariables(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void Finds_the_variables_of_the_default_prompts()
    {
        var catalog = new LocalPromptCatalog();

        Variables("orchestrator-system").ShouldBe(
        [
            "agentName", "allowGeneralKnowledge", "appName", "chunkCount", "connectionToolsEnabled", "feedbackEnabled", "frontAgentName",
            "handBackEnabled", "handoverEnabled", "instructions", "isVisitor", "language", "offerHandoverWhenUnsure", "pendingSourceCount",
            "researcherEnabled", "scopedSourceCount", "sourceCount", "sourceTypes", "specialistsEnabled", "today", "visitorFacts",
        ]);
        Variables("researcher-system").ShouldBe(["appName", "focusSources", "maxSearches", "today"]);
        Variables("conversation-title").ShouldBe(["message"]);

        IReadOnlyList<string> Variables(string name)
        {
            catalog.TryGet(name, out var template).ShouldBeTrue();
            return _renderer.ExtractVariables(template!.Text!);
        }
    }
}
