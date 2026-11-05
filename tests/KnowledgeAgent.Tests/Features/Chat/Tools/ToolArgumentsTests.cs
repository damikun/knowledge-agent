using System.Text.Json;
using KnowledgeAgent.Features.Chat.Tools;

namespace KnowledgeAgent.Tests.Features.Chat.Tools;

public sealed class ToolArgumentsTests
{
    private static Dictionary<string, object?> Json(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

    [Fact]
    public void Reads_typed_values_from_json_elements()
    {
        var arguments = new ToolArguments(Json("""{"query":"  hello ","top_k":5,"offset":"7","max":3.0,"ids":["a"," b ",3,""],"single":"x, y z"}"""));

        arguments.GetString("query", required: true).ShouldBe("hello");
        arguments.GetInt32("top_k").ShouldBe(5);
        arguments.GetInt32("offset").ShouldBe(7);
        arguments.GetInt32("max").ShouldBe(3);
        arguments.GetStringList("ids").ShouldBe(["a", "b"]);
        arguments.GetStringList("single").ShouldBe(["x", "y", "z"]);
        arguments.GetInt32("missing").ShouldBeNull();
        arguments.GetStringList("missing").ShouldBeEmpty();
        arguments.Errors.ShouldBeEmpty();
        Should.NotThrow(() => arguments.ThrowIfInvalid("tool"));
    }

    [Fact]
    public void Collects_all_validation_problems()
    {
        var arguments = new ToolArguments(Json("""{"query":42,"top_k":2.5,"name":"   ","list":{"a":1},"nothing":null}"""));

        arguments.GetString("query", required: true).ShouldBeNull();
        arguments.GetInt32("top_k").ShouldBeNull();
        arguments.GetString("name", required: true).ShouldBeNull();
        arguments.GetStringList("list").ShouldBeEmpty();
        arguments.GetString("nothing", required: true).ShouldBeNull();
        arguments.GetString("absent").ShouldBeNull();

        var exception = Should.Throw<ToolArgumentException>(() => arguments.ThrowIfInvalid("my_tool"));
        exception.Message.ShouldBe(
            "Invalid arguments for my_tool: 'query' must be a string. 'top_k' must be an integer. 'name' must not be empty. "
            + "'list' must be an array of strings. 'nothing' is required.");
    }

    [Fact]
    public void Accepts_plain_clr_values_and_parses_guid_lists()
    {
        var id = Guid.NewGuid();
        var arguments = new ToolArguments(new Dictionary<string, object?>
        {
            ["count"] = 4L,
            ["ids"] = new object[] { id, id.ToString(), "not-a-guid", Guid.Empty.ToString() },
            ["names"] = new List<string> { "a", "b" },
            ["QUERY"] = "case insensitive",
        });

        arguments.GetInt32("count").ShouldBe(4);
        arguments.GetGuidList("ids").ShouldBe([id]);
        arguments.GetStringList("names").ShouldBe(["a", "b"]);
        arguments.GetString("query").ShouldBe("case insensitive");
        arguments.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Serializes_arguments_to_a_json_object()
    {
        ToolArguments.ToJson(null).GetRawText().ShouldBe("{}");
        var json = ToolArguments.ToJson(new Dictionary<string, object?> { ["query"] = "x", ["top_k"] = 3, ["ids"] = new[] { "a" } });
        json.GetProperty("query").GetString().ShouldBe("x");
        json.GetProperty("top_k").GetInt32().ShouldBe(3);
        json.GetProperty("ids")[0].GetString().ShouldBe("a");
    }
}
