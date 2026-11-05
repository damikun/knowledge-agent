using KnowledgeAgent.Features.Evaluations.Metrics;

namespace KnowledgeAgent.Tests.Features.Evaluations;

public sealed class AnswerTextTests
{
    [Fact]
    public void Citations_understand_single_grouped_and_adjacent_tags_in_order_of_first_use()
    {
        AnswerText.Citations("Leave is 27 days [S2]. Carry-over ends in March [S1, S3][s2]; approvals take six weeks [S4; S10].")
            .ShouldBe(["S2", "S1", "S3", "S4", "S10"]);
    }

    [Theory]
    [InlineData("No citations here.")]
    [InlineData("A link [Source](http://example.com) and [S] and [SX1] are not tags.")]
    [InlineData("")]
    public void Citations_ignore_text_that_is_not_a_tag(string answer) => AnswerText.Citations(answer).ShouldBeEmpty();

    [Theory]
    [InlineData("You get 27 days of leave.", "27 days", true)]
    [InlineData("You get 27  DAYS\nof leave.", "27 days", true)]
    [InlineData("The code is E‑214.", "E-214", true)]
    [InlineData("Revenue was €48.2 M.", "€48.2", true)]
    [InlineData("It costs € 0.30 per km.", "€0.30", true)]
    [InlineData("They shipped 1,240 units.", "1240 units", true)]
    [InlineData("Parental leave lasts sixteen weeks.", "16 weeks|sixteen weeks", true)]
    [InlineData("Parental leave lasts four months.", "16 weeks|sixteen weeks", false)]
    [InlineData("You can carry over 16 days.", "6 days", false)]
    [InlineData("The limit is 1,200 requests per minute.", "200", false)]
    [InlineData("A page holds at most **200** items.", "200", true)]
    [InlineData("Uptime was 99.98% in Q2.", "99.9%", false)]
    [InlineData("Revenue grew 21% (121% of plan).", "21%", true)]
    [InlineData("", "27 days", false)]
    public void Facts_match_case_insensitively_after_normalization(string answer, string fact, bool expected) =>
        AnswerText.ContainsFact(answer, fact).ShouldBe(expected);

    [Fact]
    public void Substantive_segments_are_paragraphs_list_items_and_table_rows_without_headings_code_or_notices()
    {
        const string answer = """
            _Offline mode: no LLM is configured, so this answer is extracted from the knowledge base._

            # Vacation

            Every employee receives twenty seven vacation days per year [S1].
            Unused days roll over until March.

            - Requests longer than ten days need approval six weeks ahead [S2].
            - Short.
            | Policy | Value with several words in it |

            ```
            code block with enough words to count as a paragraph otherwise
            ```
            """;

        var segments = AnswerText.SubstantiveSegments(answer);

        segments.Count.ShouldBe(3);
        segments[0].ShouldStartWith("Every employee receives");
        segments[0].ShouldContain("Unused days roll over");
        segments[1].ShouldStartWith("- Requests longer");
        segments[2].ShouldStartWith("| Policy");
    }

    [Fact]
    public void Coverage_is_the_share_of_substantive_segments_with_a_citation()
    {
        const string answer = """
            Every employee receives twenty seven vacation days per year [S1].

            Requests longer than ten days need approval six weeks ahead.
            """;

        AnswerText.CitationCoverage(answer).ShouldBe(0.5);
        AnswerText.CitationCoverage("Too short [S1].").ShouldBeNull();
    }
}
