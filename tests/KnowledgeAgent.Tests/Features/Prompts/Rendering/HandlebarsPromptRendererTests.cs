using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Prompts.Rendering;

public sealed class HandlebarsPromptRendererTests
{
    private const string Json = """
        {
          "user": { "name": "Ann", "tags": ["a", "b"], "address": { "city": "Prague" } },
          "items": [],
          "count": 3,
          "ratio": 0.5,
          "flag": true,
          "none": null,
          "sources": [ { "title": "Doc A", "url": "https://a" }, { "title": "Doc B", "url": null } ]
        }
        """;

    private const string JsonTemplate =
        "{{user.name}}|{{#each user.tags}}{{this}}{{#unless @last}},{{/unless}}{{/each}}|{{user.address.city}}|"
        + "{{#if items}}has{{else}}empty{{/if}}|{{count}}|{{ratio}}|{{flag}}|[{{none}}]|"
        + "{{#each sources}}{{@index}}={{title}}{{#if url}}@{{url}}{{/if}};{{/each}}|{{#if (gt count 2)}}many{{/if}}";

    private const string JsonExpected = "Ann|a,b|Prague|empty|3|0.5|true|[]|0=Doc A@https://a;1=Doc B;|many";

    private readonly HandlebarsPromptRenderer _renderer = new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 9, 30, 0, TimeSpan.Zero)));

    [Fact]
    public void Replaces_variables()
    {
        Render("Hello {{name}}, you have {{count}} new messages.", ("name", "Ann"), ("count", 3))
            .ShouldBe("Hello Ann, you have 3 new messages.");
    }

    [Fact]
    public void Resolves_nested_paths_on_anonymous_objects_and_dictionaries()
    {
        var user = new { name = "Ann", address = new Dictionary<string, object?> { ["city"] = "Prague" } };

        Render("{{user.name}} lives in {{user.address.city}}.", ("user", user)).ShouldBe("Ann lives in Prague.");
    }

    [Fact]
    public void Matches_pascal_case_properties_case_insensitively()
    {
        var source = new SourceInfo("Doc", new Uri("https://example.com/doc"));

        Render("{{source.title}} <{{source.url}}>", ("source", source)).ShouldBe("Doc <https://example.com/doc>");
    }

    [Fact]
    public void Renders_missing_variables_as_empty_text()
    {
        Render("[{{missing}}][{{user.missing}}][{{missing.deep.path}}]", ("user", new { name = "Ann" })).ShouldBe("[][][]");
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    [InlineData(null, "off")]
    [InlineData("", "off")]
    [InlineData("x", "on")]
    [InlineData(0, "off")]
    [InlineData(2, "on")]
    public void If_else_follows_handlebars_truthiness(object? value, string expected)
    {
        Render("{{#if flag}}on{{else}}off{{/if}}", ("flag", value)).ShouldBe(expected);
    }

    [Fact]
    public void If_treats_a_missing_variable_as_false() => Render("{{#if flag}}on{{else}}off{{/if}}").ShouldBe("off");

    [Fact]
    public void Supports_else_if_chains()
    {
        const string template = "{{#if a}}A{{else if b}}B{{else}}C{{/if}}";

        Render(template, ("a", true)).ShouldBe("A");
        Render(template, ("b", true)).ShouldBe("B");
        Render(template).ShouldBe("C");
    }

    [Fact]
    public void Unless_is_the_inverse_of_if()
    {
        const string template = "{{#unless done}}todo{{else}}finished{{/unless}}";

        Render(template, ("done", false)).ShouldBe("todo");
        Render(template, ("done", true)).ShouldBe("finished");
    }

    [Fact]
    public void Each_exposes_index_first_and_last()
    {
        var items = new[] { new { name = "a" }, new { name = "b" }, new { name = "c" } };

        Render("{{#each items}}{{@index}}:{{name}}{{#if @first}} (first){{/if}}{{#if @last}} (last){{/if}}{{#unless @last}}, {{/unless}}{{/each}}", ("items", items))
            .ShouldBe("0:a (first), 1:b, 2:c (last)");
    }

    [Fact]
    public void Each_over_a_dictionary_exposes_key()
    {
        var settings = new Dictionary<string, object?> { ["mode"] = "fast", ["retries"] = 3 };

        Render("{{#each settings}}{{@key}}={{this}}{{#unless @last}}; {{/unless}}{{/each}}", ("settings", settings))
            .ShouldBe("mode=fast; retries=3");
    }

    [Fact]
    public void Each_can_read_the_parent_context_and_root()
    {
        Render("{{#each items}}{{this}}/{{../owner}}/{{@root.owner}} {{/each}}", ("items", new[] { "x", "y" }), ("owner", "me"))
            .ShouldBe("x/me/me y/me/me ");
    }

    [Fact]
    public void Each_renders_else_for_empty_lists()
    {
        Render("{{#each items}}{{this}}{{else}}none{{/each}}", ("items", Array.Empty<string>())).ShouldBe("none");
    }

    [Fact]
    public void With_changes_the_context()
    {
        var user = new { name = "Ann", address = new { city = "Prague" } };

        Render("{{#with user}}{{name}} ({{address.city}}){{/with}}", ("user", user)).ShouldBe("Ann (Prague)");
    }

    [Fact]
    public void Does_not_html_escape()
    {
        const string html = "<b>&</b> \"double\" 'single' `tick`";

        Render("{{html}}|{{{html}}}", ("html", html)).ShouldBe(html + "|" + html);
    }

    [Fact]
    public void Renders_json_element_variables_parsed_from_json_text()
    {
        using var document = JsonDocument.Parse(Json);
        var variables = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

        _renderer.Render(JsonTemplate, variables).ShouldBe(JsonExpected);
    }

    [Fact]
    public void Renders_json_node_variables_parsed_from_json_text()
    {
        var root = JsonNode.Parse(Json)!.AsObject();
        var variables = root.ToDictionary(p => p.Key, p => (object?)p.Value);

        _renderer.Render(JsonTemplate, variables).ShouldBe(JsonExpected);
    }

    [Fact]
    public void Renders_a_whole_json_document_passed_as_one_variable()
    {
        using var document = JsonDocument.Parse(Json);

        Render("{{data.user.name}} {{data.sources.length}}", ("data", document.RootElement.Clone())).ShouldBe("Ann 2");
        Render("{{data.user.address.city}}", ("data", JsonNode.Parse(Json))).ShouldBe("Prague");
    }

    [Fact]
    public void Renders_json_values_created_from_clr_values()
    {
        Render(
            "{{n}} {{d}} {{s}} {{b}} {{g}}",
            ("n", JsonValue.Create(42)),
            ("d", JsonValue.Create(1.25)),
            ("s", JsonValue.Create("text")),
            ("b", JsonValue.Create(false)),
            ("g", JsonValue.Create(Guid.Empty))).ShouldBe("42 1.25 text false 00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public void Empty_arrays_are_falsy()
    {
        using var document = JsonDocument.Parse("[]");
        object?[] emptyValues = [new List<string>(), Array.Empty<int>(), document.RootElement.Clone(), new JsonArray(), Enumerable.Empty<object>()];

        foreach (var value in emptyValues)
        {
            Render("{{#if items}}yes{{else}}no{{/if}}", ("items", value)).ShouldBe("no", $"for {value!.GetType().Name}");
        }

        Render("{{#if items}}yes{{else}}no{{/if}}", ("items", new[] { 1 })).ShouldBe("yes");
    }

    [Theory]
    [InlineData("{{#if name}}never closed")]
    [InlineData("{{#if name}}x{{/each}}")]
    [InlineData("{{name")]
    [InlineData("{{{name}}")]
    [InlineData("{{#each}}x{{/each}}")]
    [InlineData("{{#if}}x{{/if}}")]
    [InlineData("{{#if}}")]
    [InlineData("{{#unless}}x")]
    [InlineData("{{#if name}}x{{else if}}y{{/if}}")]
    public void Syntax_errors_throw_prompt_render_exception(string template)
    {
        var exception = Should.Throw<PromptRenderException>(() => Render(template, ("name", "x")));

        exception.Message.ShouldStartWith("Template syntax error:");
        exception.Message.ShouldNotContain("\n");
    }

    [Fact]
    public void Block_helpers_without_an_argument_get_a_helpful_message()
    {
        Should.Throw<PromptRenderException>(() => Render("{{#if}}x{{/if}}")).Message
            .ShouldBe("Template syntax error: '{{#if}}' needs an argument, for example {{#if value}}.");
        TemplateSyntax.FindProblem("{{#if x includeZero=true}}{{/if}} {{#each items as |item|}}{{/each}} {{else if y}}").ShouldBeNull();
    }

    [Fact]
    public void Unknown_helpers_throw_prompt_render_exception()
    {
        var exception = Should.Throw<PromptRenderException>(() => Render("{{shout name}}", ("name", "x")));

        exception.Message.ShouldContain("shout");
    }

    [Fact]
    public void Output_does_not_depend_on_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Render(
                    "{{n}}|{{date}}|{{moment}}|{{utc}}|{{flag}}",
                    ("n", 1.5),
                    ("date", new DateOnly(2026, 10, 22)),
                    ("moment", new DateTimeOffset(2026, 10, 22, 13, 5, 0, TimeSpan.FromHours(2))),
                    ("utc", new DateTime(2026, 10, 22, 13, 5, 0, DateTimeKind.Utc)),
                    ("flag", false))
                .ShouldBe("1.5|2026-10-22|2026-10-22T13:05:00.0000000+02:00|2026-10-22T13:05:00.0000000Z|false");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Booleans_render_in_lower_case()
    {
        Render("{{yes}} {{no}} {{#each list}}{{this}} {{/each}}", ("yes", true), ("no", false), ("list", new[] { true, false }))
            .ShouldBe("true false true false ");
    }

    [Fact]
    public void Lists_render_comma_separated_and_expose_length()
    {
        Render("{{items}} ({{items.length}})", ("items", new[] { "a", "b", "c" })).ShouldBe("a,b,c (3)");
    }

    [Fact]
    public void Standalone_block_lines_leave_no_blank_lines()
    {
        const string template = "Header\n{{#if show}}\nShown\n{{/if}}\n{{#each items}}\n- {{this}}\n{{/each}}\nFooter";

        Render(template, ("show", true), ("items", new[] { "a", "b" })).ShouldBe("Header\nShown\n- a\n- b\nFooter");
        Render(template, ("show", false), ("items", Array.Empty<string>())).ShouldBe("Header\nFooter");
    }

    [Fact]
    public void Reference_cycles_are_cut_instead_of_overflowing()
    {
        var root = new TreeNode("root");
        root.Children.Add(new TreeNode("child") { Parent = root });

        Render("{{node.name}}:{{#each node.children}}{{name}}<{{parent.name}}>{{/each}}", ("node", root)).ShouldBe("root:child<>");
    }

    [Fact]
    public void Compiled_templates_are_cached_in_a_bounded_cache()
    {
        var renderer = new HandlebarsPromptRenderer(TimeProvider.System, cacheCapacity: 2);
        var variables = new Dictionary<string, object?> { ["x"] = 1 };

        renderer.Render("a{{x}}", variables).ShouldBe("a1");
        renderer.Render("a{{x}}", variables).ShouldBe("a1");
        renderer.CachedTemplateCount.ShouldBe(1);

        renderer.Render("b{{x}}", variables);
        renderer.Render("c{{x}}", variables);
        renderer.CachedTemplateCount.ShouldBe(2);
        renderer.Render("a{{x}}", variables).ShouldBe("a1");
    }

    [Fact]
    public void Renders_concurrently()
    {
        const string template = "{{n}}-{{#each items}}{{this}}{{/each}}";

        Parallel.For(0, 200, i =>
            _renderer.Render(template, new Dictionary<string, object?> { ["n"] = i, ["items"] = new[] { i, i } }).ShouldBe($"{i}-{i}{i}"));
    }

    [Fact]
    public void Compiles_distinct_templates_concurrently()
    {
        var renderer = new HandlebarsPromptRenderer(TimeProvider.System, cacheCapacity: 32);

        Parallel.For(0, 300, i =>
            renderer.Render($"#{i % 97}: {{{{#if flag}}}}{{{{upper name}}}}{{{{/if}}}}", new Dictionary<string, object?> { ["flag"] = true, ["name"] = "x" })
                .ShouldBe($"#{i % 97}: X"));

        renderer.CachedTemplateCount.ShouldBeLessThanOrEqualTo(32);
    }

    [Fact]
    public void Empty_template_renders_empty() => Render(string.Empty, ("x", 1)).ShouldBe(string.Empty);

    private string Render(string template, params (string Key, object? Value)[] variables) =>
        _renderer.Render(template, variables.ToDictionary(v => v.Key, v => v.Value));

    private sealed record SourceInfo(string Title, Uri Url);

    private sealed class TreeNode(string name)
    {
        public string Name { get; } = name;

        public TreeNode? Parent { get; init; }

        public List<TreeNode> Children { get; } = [];
    }
}
