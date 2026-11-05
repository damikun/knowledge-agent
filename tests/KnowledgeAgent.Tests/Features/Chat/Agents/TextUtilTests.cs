using KnowledgeAgent.Features.Chat.Agents;

namespace KnowledgeAgent.Tests.Features.Chat.Agents;

public sealed class TextUtilTests
{
    [Fact]
    public void Truncates_tool_output_with_a_marker_within_the_limit()
    {
        var text = new string('a', 5000);

        var truncated = TextUtil.TruncateToolOutput(text, 1000);

        truncated.Length.ShouldBeLessThanOrEqualTo(1000);
        truncated.ShouldEndWith("characters]");
        truncated.ShouldContain("output truncated: 4064 more characters");
        TextUtil.TruncateToolOutput("short", 1000).ShouldBe("short");
    }

    [Fact]
    public void Shortens_and_collapses_text()
    {
        TextUtil.Shorten("abcdef", 4).ShouldBe("abc…");
        TextUtil.Shorten("abc", 4).ShouldBe("abc");
        TextUtil.CollapseWhitespace("  a \n\t b  ").ShouldBe("a b");
        TextUtil.Clip("abcdef", 3).ShouldBe("abc…");
    }
}
