using KnowledgeAgent.Features.Sources.Extraction;

namespace KnowledgeAgent.Tests.Features.Sources.Extraction;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" \t \n ", "")]
    [InlineData("  Hello    world \t again  \n   next   line  ", "Hello world again\nnext line")]
    [InlineData("a\r\nb\rc", "a\nb\nc")]
    [InlineData("a\n\n\n\nb\n \n\t\nc", "a\n\nb\n\nc")]
    [InlineData("\n\n  leading and trailing blank lines \n\n", "leading and trailing blank lines")]
    [InlineData("one\ftwo", "one\n\ntwo")]
    public void Normalizes_whitespace(string? input, string expected)
    {
        TextNormalizer.Normalize(input).ShouldBe(expected);
    }

    [Fact]
    public void Unicode_spaces_become_spaces_and_invisible_characters_are_removed()
    {
        var input = $"a{(char)0x00A0}b{(char)0x200B}c{(char)0xFEFF}d{(char)0x2003}{(char)0x2003}e{(char)0x00AD}f";

        TextNormalizer.Normalize(input).ShouldBe("a bcd ef");
    }

    [Fact]
    public void Unicode_line_and_paragraph_separators_become_line_breaks()
    {
        var input = $"a{(char)0x2028}b{(char)0x2029}c";

        TextNormalizer.Normalize(input).ShouldBe("a\nb\n\nc");
    }

    [Fact]
    public void Control_characters_are_replaced()
    {
        TextNormalizer.Normalize("a\u0001b\u0007 c").ShouldBe("a b c");
    }

    [Fact]
    public void Page_separators_are_kept_when_requested_and_empty_pages_survive()
    {
        TextNormalizer.Normalize("  page   one \n\n\n more \f\f page  three  ", keepPageSeparators: true)
            .ShouldBe("page one\n\nmore\f\fpage three");
    }

    [Fact]
    public void Indentation_is_preserved_when_requested()
    {
        TextNormalizer.Normalize("def f():\n    return   1  \n\n\n\tdone", preserveIndentation: true)
            .ShouldBe("def f():\n    return 1\n\n\tdone");
    }

    [Fact]
    public void Normalization_is_idempotent()
    {
        var once = TextNormalizer.Normalize("  A  b \r\n\r\n\r\n c\f d ", keepPageSeparators: true);

        TextNormalizer.Normalize(once, keepPageSeparators: true).ShouldBe(once);
    }
}
