using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Sources.Extraction;

public sealed class HtmlTextExtractorTests
{
    private static readonly Uri ArticleUri = new("https://blog.example.com/vector-databases");
    private readonly HtmlTextExtractor _extractor = new(NullLogger<HtmlTextExtractor>.Instance);

    [Fact]
    public void Extracts_title_site_name_and_main_content()
    {
        var document = _extractor.Extract(TestDocuments.ArticleHtml, ArticleUri);

        document.Kind.ShouldBe(ContentKind.Html);
        document.PageCount.ShouldBeNull();
        document.Title.ShouldBe("Understanding Vector Databases");
        document.SiteName.ShouldBe("Example Engineering");
        document.Text.ShouldStartWith("Understanding Vector Databases");
        document.Text.ShouldContain("Vector databases store embeddings, which are numerical representations of text, images or audio.");
        document.Text.ShouldContain("For small collections a brute force scan is often fast enough and much simpler to operate.");
    }

    [Theory]
    [InlineData("TRACKING_SCRIPT_MARKER")]
    [InlineData("INLINE_SCRIPT_MARKER")]
    [InlineData("function track")]
    [InlineData("secret-style-marker")]
    [InlineData("NAVIGATION_MARKER")]
    [InlineData("COOKIE_MARKER")]
    [InlineData("SIDEBAR_MARKER")]
    [InlineData("FOOTER_MARKER")]
    public void Scripts_styles_navigation_and_boilerplate_are_removed(string marker)
    {
        _extractor.Extract(TestDocuments.ArticleHtml, ArticleUri).Text.ShouldNotContain(marker);
    }

    [Fact]
    public void Headings_paragraphs_and_list_items_stay_on_their_own_lines()
    {
        var text = _extractor.Extract(TestDocuments.ArticleHtml, ArticleUri).Text;
        var lines = text.Split('\n');

        lines.ShouldContain("How similarity search works");
        lines.ShouldContain("Choosing a database");
        lines.ShouldContain("- Cosine similarity compares directions.");
        lines.ShouldContain("- Dot product also considers magnitude.");
        text.ShouldContain("How similarity search works\n\nEach document is split into chunks");
        text.ShouldNotContain("\n\n\n");
    }

    [Fact]
    public void Pages_that_are_not_articles_fall_back_to_the_body_text()
    {
        const string html = """
            <html><head><title>Docs index</title></head>
            <body>
              <nav>Menu NAV_MARKER</nav>
              <h1>Documentation</h1>
              <ol><li>Getting started</li><li>Configuration reference</li></ol>
              <table><tr><th>Name</th><th>Value</th></tr><tr><td>Timeout</td><td>30</td></tr></table>
              <p>First line<br>Second line</p>
              <script>alert('SCRIPT_MARKER')</script>
              <footer>FOOTER_MARKER</footer>
            </body></html>
            """;

        var document = _extractor.Extract(html, new Uri("https://docs.example.com/"));

        document.Title.ShouldBe("Docs index");
        var lines = document.Text.Split('\n');
        lines.ShouldContain("Documentation");
        lines.ShouldContain("1. Getting started");
        lines.ShouldContain("2. Configuration reference");
        lines.ShouldContain("Name | Value");
        lines.ShouldContain("Timeout | 30");
        lines.ShouldContain("First line");
        lines.ShouldContain("Second line");
        document.Text.ShouldNotContain("NAV_MARKER");
        document.Text.ShouldNotContain("SCRIPT_MARKER");
        document.Text.ShouldNotContain("FOOTER_MARKER");
    }

    [Fact]
    public void First_heading_is_the_title_when_the_page_has_none()
    {
        var document = _extractor.Extract("<html><body><h1>Only <em>Heading</em></h1><p>Body text.</p></body></html>", null);

        document.Title.ShouldBe("Only Heading");
        document.Text.ShouldContain("Body text.");
    }

    [Fact]
    public void Pages_wrapped_in_a_form_keep_their_content()
    {
        const string html = """
            <html><head><title>Legacy</title></head><body>
              <form id="aspnetForm" method="post"><div class="page">
                <h1>Legacy page</h1>
                <p>Important legacy content that must be kept in the knowledge base.</p>
              </div></form>
            </body></html>
            """;

        _extractor.Extract(html, null).Text.ShouldContain("Important legacy content that must be kept");
    }

    [Fact]
    public void Hidden_elements_are_dropped()
    {
        const string html = """
            <html><body>
              <p>Visible paragraph.</p>
              <p hidden>HIDDEN_ATTRIBUTE</p>
              <div style="display: none">HIDDEN_STYLE</div>
              <span aria-hidden="true">HIDDEN_ARIA</span>
              <div class="share-buttons">Share on social SHARE_MARKER</div>
            </body></html>
            """;

        var text = _extractor.Extract(html, null).Text;

        text.ShouldContain("Visible paragraph.");
        text.ShouldNotContain("HIDDEN_ATTRIBUTE");
        text.ShouldNotContain("HIDDEN_STYLE");
        text.ShouldNotContain("HIDDEN_ARIA");
        text.ShouldNotContain("SHARE_MARKER");
    }

    [Fact]
    public void Preformatted_text_keeps_its_lines()
    {
        var text = _extractor.Extract("<html><body><p>Example:</p><pre>line one\nline two\n\nline four</pre></body></html>", null).Text;

        text.ShouldContain("line one\nline two\n\nline four");
    }

    [Fact]
    public void Deeply_nested_markup_is_extracted_without_readability_or_stack_overflow()
    {
        const int depth = 20_000;
        var html = "<html><head><title>Deep</title></head><body>"
            + string.Concat(Enumerable.Repeat("<div>", depth))
            + "<p>Deeply nested text survives.</p>"
            + string.Concat(Enumerable.Repeat("</div>", depth))
            + "</body></html>";
        string? text = null;

        // A 1 MB stack, like a thread-pool thread: recursion per nesting level would overflow it.
        var thread = new Thread(() => text = _extractor.Extract(html, null).Text, 1024 * 1024);
        thread.Start();
        thread.Join();

        text.ShouldBe("Deeply nested text survives.");
    }
}
