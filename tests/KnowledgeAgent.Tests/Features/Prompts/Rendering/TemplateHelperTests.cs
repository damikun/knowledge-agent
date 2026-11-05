using System.Globalization;
using System.Text.Json;
using KnowledgeAgent.Features.Prompts;
using KnowledgeAgent.Features.Prompts.Rendering;
using Microsoft.Extensions.Time.Testing;

namespace KnowledgeAgent.Tests.Features.Prompts.Rendering;

public sealed class TemplateHelperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    private readonly HandlebarsPromptRenderer _renderer = new(new FakeTimeProvider(Now));

    [Theory]
    [InlineData("{{eq a b}}", "x", "x", "true")]
    [InlineData("{{eq a b}}", "x", "X", "false")]
    [InlineData("{{ne a b}}", "x", "y", "true")]
    [InlineData("{{ne a b}}", "x", "x", "false")]
    [InlineData("{{gt a b}}", 3, 2, "true")]
    [InlineData("{{gt a b}}", 2, 3, "false")]
    [InlineData("{{gte a b}}", 3, 3, "true")]
    [InlineData("{{lt a b}}", 2.5, 3, "true")]
    [InlineData("{{lte a b}}", 3, 3, "true")]
    [InlineData("{{lte a b}}", 4, 3, "false")]
    [InlineData("{{gt a b}}", "b", "a", "true")]
    [InlineData("{{gt a b}}", 10, "9", "true")]
    [InlineData("{{lt a b}}", null, 1, "false")]
    public void Comparison_helpers_work_inline(string template, object? a, object? b, string expected)
    {
        Render(template, ("a", a), ("b", b)).ShouldBe(expected);
    }

    [Fact]
    public void Eq_compares_numbers_numerically_across_types()
    {
        Render("{{eq a 3}} {{eq b 3}} {{eq c 3}}", ("a", 3L), ("b", 3.0), ("c", 3m)).ShouldBe("true true true");
    }

    [Fact]
    public void Eq_treats_missing_and_null_alike_and_matches_enum_names()
    {
        Render("{{eq missing nothing}} {{eq day \"monday\"}}", ("nothing", null), ("day", DayOfWeek.Monday)).ShouldBe("true true");
    }

    [Fact]
    public void Comparisons_order_dates()
    {
        Render("{{gt later earlier}}", ("later", new DateTime(2026, 1, 2)), ("earlier", "2026-01-01")).ShouldBe("true");
    }

    [Fact]
    public void Helpers_work_in_subexpressions()
    {
        const string template = "{{#if (eq tone \"formal\")}}Dear user{{else}}Hi{{/if}}";

        Render(template, ("tone", "formal")).ShouldBe("Dear user");
        Render(template, ("tone", "casual")).ShouldBe("Hi");
        Render(template).ShouldBe("Hi");
    }

    [Fact]
    public void Subexpressions_can_be_nested()
    {
        const string template = "{{#if (or (eq tone \"formal\") (and (gt level 2) (not muted)))}}yes{{else}}no{{/if}}";

        Render(template, ("tone", "formal")).ShouldBe("yes");
        Render(template, ("level", 3), ("muted", false)).ShouldBe("yes");
        Render(template, ("level", 3), ("muted", true)).ShouldBe("no");
        Render(template, ("level", 1)).ShouldBe("no");
    }

    [Fact]
    public void Logic_helpers_use_if_truthiness()
    {
        Render("{{and a b}} {{and a c}} {{or c d}} {{or c a}} {{not c}} {{not a}}", ("a", "x"), ("b", 1), ("c", Array.Empty<int>()), ("d", ""))
            .ShouldBe("true false false true true false");
    }

    [Theory]
    [InlineData(null, "English")]
    [InlineData("", "English")]
    [InlineData("   ", "English")]
    [InlineData("Czech", "Czech")]
    public void Default_returns_the_fallback_for_missing_values(string? language, string expected)
    {
        Render("{{default language \"English\"}}", ("language", language)).ShouldBe(expected);
    }

    [Fact]
    public void Default_handles_missing_variables_empty_lists_and_chains()
    {
        Render("{{default missing \"x\"}}|{{default items \"none\"}}|{{default a b \"last\"}}|{{default zero 5}}", ("items", Array.Empty<string>()), ("b", "B"), ("zero", 0))
            .ShouldBe("x|none|B|0");
    }

    [Fact]
    public void Default_can_feed_other_helpers()
    {
        Render("{{upper (default name \"guest\")}}").ShouldBe("GUEST");
    }

    [Fact]
    public void Json_serializes_values_without_html_escaping()
    {
        var user = new { name = "Zoë <admin>", tags = new[] { "a", "b" }, active = true, score = 1.5 };

        Render("{{json user}}", ("user", user)).ShouldBe("""{"name":"Zoë <admin>","tags":["a","b"],"active":true,"score":1.5}""");
        Render("{{json missing}}").ShouldBe("null");
        Render("{{json text}}", ("text", "say \"hi\"")).ShouldBe("\"say \\\"hi\\\"\"");
    }

    [Fact]
    public void Json_can_indent_and_serializes_json_variables()
    {
        using var document = JsonDocument.Parse("""{"a":1,"b":[true,null]}""");

        Render("{{json data true}}", ("data", document.RootElement.Clone()))
            .ReplaceLineEndings("\n")
            .ShouldBe("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null\n  ]\n}");
    }

    [Fact]
    public void Upper_and_lower_use_invariant_casing()
    {
        Render("{{upper a}} {{lower b}} [{{upper missing}}]", ("a", "istanbul"), ("b", "ÁBC")).ShouldBe("ISTANBUL ábc []");
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]
    [InlineData("The quick brown fox", 9, "The quick…")]
    [InlineData("The quick brown fox", 10, "The quick…")]
    [InlineData("abc", 0, "…")]
    public void Truncate_shortens_long_text(string text, int length, string expected)
    {
        Render("{{truncate text n}}", ("text", text), ("n", length)).ShouldBe(expected);
    }

    [Fact]
    public void Truncate_accepts_a_custom_suffix_and_literal_lengths()
    {
        Render("{{truncate text 5 \"...\"}}|{{truncate missing 3}}", ("text", "abcdefgh")).ShouldBe("abcde...|");
    }

    [Fact]
    public void Truncate_rejects_a_non_numeric_length()
    {
        Should.Throw<PromptRenderException>(() => Render("{{truncate text \"many\"}}", ("text", "abc")))
            .Message.ShouldContain("truncate");
    }

    [Fact]
    public void Join_concatenates_lists()
    {
        Render("{{join tags}}|{{join tags \" / \"}}|{{join mixed \",\"}}|{{join text}}|[{{join missing}}]", ("tags", new[] { "a", "b", "c" }), ("mixed", new object?[] { 1, null, true, 2.5 }), ("text", "single"))
            .ShouldBe("a, b, c|a / b / c|1,true,2.5|single|[]");
    }

    [Fact]
    public void Join_works_on_json_arrays()
    {
        using var document = JsonDocument.Parse("""["x","y"]""");

        Render("{{join items \"+\"}}", ("items", document.RootElement.Clone())).ShouldBe("x+y");
    }

    [Fact]
    public void Date_formats_the_current_time_by_default()
    {
        Render("{{date}}").ShouldBe("2026-10-22");
        Render("{{date \"yyyy/MM/dd HH:mm\"}}").ShouldBe("2026/10/22 09:30");
        Render("{{date \"now\"}}").ShouldBe("2026-10-22");
        Render("{{date \"now\" \"dddd\"}}").ShouldBe(Now.ToString("dddd", CultureInfo.InvariantCulture));
        Render("{{date \"HH:mm\" \"now\"}}").ShouldBe("09:30");
    }

    [Fact]
    public void Date_formats_values_in_either_argument_order()
    {
        object[] values =
        [
            new DateTime(2025, 3, 5, 14, 0, 0, DateTimeKind.Utc),
            new DateTimeOffset(2025, 3, 5, 14, 0, 0, TimeSpan.Zero),
            new DateOnly(2025, 3, 5),
            "2025-03-05T14:00:00Z",
            JsonDocument.Parse("\"2025-03-05\"").RootElement.Clone(),
        ];

        foreach (var value in values)
        {
            Render("{{date published \"MMMM d, yyyy\"}}", ("published", value)).ShouldBe("March 5, 2025", $"for {value.GetType().Name}");
            Render("{{date \"yyyy\" published}}", ("published", value)).ShouldBe("2025", $"for {value.GetType().Name}");
            Render("{{date published}}", ("published", value)).ShouldBe("2025-03-05", $"for {value.GetType().Name}");
        }
    }

    [Fact]
    public void Date_renders_nothing_for_missing_values()
    {
        Render("[{{date published \"yyyy\"}}][{{date missing}}]", ("published", null)).ShouldBe("[][]");
    }

    [Fact]
    public void Date_rejects_invalid_formats()
    {
        Should.Throw<PromptRenderException>(() => Render("{{date \"now\" \"%\"}}")).Message.ShouldContain("date");
    }

    [Theory]
    [InlineData("{{eq a}}", "eq")]
    [InlineData("{{not a b}}", "not")]
    [InlineData("{{default a}}", "default")]
    [InlineData("{{upper}}", "upper")]
    [InlineData("{{#if (gt a)}}x{{/if}}", "gt")]
    public void Wrong_argument_counts_name_the_helper(string template, string helper)
    {
        var exception = Should.Throw<PromptRenderException>(() => Render(template, ("a", 1), ("b", 2)));

        exception.Message.ShouldContain($"'{helper}'");
    }

    private string Render(string template, params (string Key, object? Value)[] variables) =>
        _renderer.Render(template, variables.ToDictionary(v => v.Key, v => v.Value));
}
