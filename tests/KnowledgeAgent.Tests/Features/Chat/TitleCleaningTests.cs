using KnowledgeAgent.Features.Chat;

namespace KnowledgeAgent.Tests.Features.Chat;

public sealed class TitleCleaningTests
{
    [Theory]
    [InlineData("  \"Pro plan pricing.\"  ", "Pro plan pricing")]
    [InlineData("Title: Onboarding checklist", "Onboarding checklist")]
    [InlineData("**Quarterly report summary**\nExplanation follows", "Quarterly report summary")]
    [InlineData("“Vacation   policy”!", "Vacation policy")]
    [InlineData("\n\n# Heading style title?\n", "Heading style title")]
    [InlineData("'Single quoted'", "Single quoted")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Cleans_model_output(string? raw, string expected) => TitleGenerator.Clean(raw).ShouldBe(expected);

    [Fact]
    public void Limits_titles_to_80_characters_at_a_word_boundary()
    {
        var raw = string.Join(' ', Enumerable.Repeat("word", 40));

        var title = TitleGenerator.Clean(raw);

        title.Length.ShouldBeLessThanOrEqualTo(TitleGenerator.MaxTitleLength);
        title.ShouldEndWith("word");
    }

    [Theory]
    [InlineData("Short question?", "Short question?")]
    [InlineData("  spaced\n\nout   message ", "spaced out message")]
    [InlineData("   ", "New conversation")]
    public void Falls_back_to_the_message_prefix(string message, string expected) => TitleGenerator.Fallback(message).ShouldBe(expected);

    [Fact]
    public void Fallback_keeps_the_first_60_characters()
    {
        var message = new string('a', 59) + " bcdef";

        TitleGenerator.Fallback(message).ShouldBe(new string('a', 59));
        TitleGenerator.Fallback(new string('q', 100)).ShouldBe(new string('q', 60));
    }
}
