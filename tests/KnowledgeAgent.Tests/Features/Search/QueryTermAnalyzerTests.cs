using KnowledgeAgent.Features.Search;

namespace KnowledgeAgent.Tests.Features.Search;

public sealed class QueryTermAnalyzerTests
{
    [Fact]
    public void Drops_stopwords_question_words_and_single_characters()
    {
        QueryTermAnalyzer.Terms("What is the maximum payload of the OW-X7 arm? A 1 kg test?")
            .ShouldBe(["maximum", "payload", "ow-x7", "arm", "kg", "test"]);
    }

    [Fact]
    public void Keeps_identifiers_whole_and_adds_their_word_parts()
    {
        QueryTermAnalyzer.Terms("Error E-214 after firmware 3.3.1 in room B2.17: check X-Orbitwell-Signature.")
            .ShouldBe(["error", "e-214", "firmwar", "3.3.1", "room", "b2.17", "check", "x-orbitwell-signature", "orbitwell", "signatur"]);
    }

    [Theory]
    [InlineData("policies", "polic")]
    [InlineData("policy", "polic")]
    [InlineData("approved", "approv")]
    [InlineData("approves", "approv")]
    [InlineData("approve", "approv")]
    [InlineData("days", "day")]
    [InlineData("boxes", "box")]
    [InlineData("matches", "match")]
    [InlineData("charging", "charg")]
    [InlineData("employees", "employe")]
    [InlineData("glass", "glass")]
    [InlineData("status", "status")]
    [InlineData("analysis", "analysis")]
    [InlineData("arm", "arm")]
    public void Stems_are_prefixes_of_the_inflections(string word, string stem) => QueryTermAnalyzer.Stem(word).ShouldBe(stem);

    [Fact]
    public void Normalizes_case_and_unicode_compatibility_forms()
    {
        QueryTermAnalyzer.Terms("PRÍRUČKA pre ZAMESTNANCOV: ﬁles").ShouldBe(["príručka", "pre", "zamestnancov", "file"]);
    }

    [Fact]
    public void Removes_duplicates_and_keeps_at_most_sixteen_terms()
    {
        var query = "vacation vacations " + string.Join(' ', Enumerable.Range(0, 30).Select(i => "term" + i));

        var terms = QueryTermAnalyzer.Terms(query);

        terms.Count.ShouldBe(QueryTermAnalyzer.MaxTerms);
        terms[0].ShouldBe("vacation");
        terms.Distinct().Count().ShouldBe(terms.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("What is it? How?")]
    public void Questions_without_content_words_have_no_terms(string query) => QueryTermAnalyzer.Terms(query).ShouldBeEmpty();
}
