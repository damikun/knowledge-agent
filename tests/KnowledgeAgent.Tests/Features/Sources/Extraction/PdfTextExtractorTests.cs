using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Sources.Extraction;

public sealed class PdfTextExtractorTests
{
    private readonly PdfTextExtractor _extractor = new(NullLogger<PdfTextExtractor>.Instance);

    [Fact]
    public void Extracts_page_texts_and_count_and_joins_pages_with_form_feeds()
    {
        var document = _extractor.Extract(TestDocuments.QuarterlyReportPdf(), TestContext.Current.CancellationToken);

        document.Kind.ShouldBe(ContentKind.Pdf);
        document.HasPages.ShouldBeTrue();
        document.PageCount.ShouldBe(2);
        var pages = document.Text.Split('\f');
        pages.Length.ShouldBe(2);
        pages[0].ShouldContain("Revenue grew by twelve percent compared to the previous quarter.");
        pages[0].ShouldNotContain("Operating expenses");
        pages[1].ShouldContain("Operating expenses stayed flat while the team hired four engineers.");
        pages[1].ShouldNotContain("Revenue grew");
    }

    [Fact]
    public void Text_within_a_page_follows_reading_order()
    {
        var firstPage = _extractor.Extract(TestDocuments.QuarterlyReportPdf(), TestContext.Current.CancellationToken).Text.Split('\f')[0];

        var heading = firstPage.IndexOf("Quarterly report overview", StringComparison.Ordinal);
        var revenue = firstPage.IndexOf("Revenue grew", StringComparison.Ordinal);
        var growth = firstPage.IndexOf("Most of the growth", StringComparison.Ordinal);
        heading.ShouldBeGreaterThanOrEqualTo(0);
        revenue.ShouldBeGreaterThan(heading);
        growth.ShouldBeGreaterThan(revenue);
        firstPage.ShouldStartWith("Quarterly report overview\n\n");
    }

    [Fact]
    public void Title_comes_from_the_document_information()
    {
        _extractor.Extract(TestDocuments.QuarterlyReportPdf("Quarterly Report"), TestContext.Current.CancellationToken)
            .Title.ShouldBe("Quarterly Report");
    }

    [Fact]
    public void Title_falls_back_to_the_first_line()
    {
        _extractor.Extract(TestDocuments.QuarterlyReportPdf(title: null), TestContext.Current.CancellationToken)
            .Title.ShouldBe("Quarterly report overview");
    }

    [Theory]
    [InlineData("Microsoft Word - Annual plan.docx", "Annual plan")]
    [InlineData("untitled", "Opening line of the plan")]
    [InlineData("   ", "Opening line of the plan")]
    public void Junk_document_titles_are_cleaned_or_ignored(string infoTitle, string expected)
    {
        var pdf = TestDocuments.Pdf(infoTitle, ["Opening line of the plan", "", "More text follows here."]);

        _extractor.Extract(pdf, TestContext.Current.CancellationToken).Title.ShouldBe(expected);
    }

    [Fact]
    public void Empty_pages_keep_page_numbers_aligned()
    {
        var pdf = TestDocuments.Pdf(null, ["First page text."], [], ["Third page text."]);

        var document = _extractor.Extract(pdf, TestContext.Current.CancellationToken);

        document.PageCount.ShouldBe(3);
        document.Text.Split('\f').ShouldBe(["First page text.", "", "Third page text."]);
    }

    [Fact]
    public void Invalid_bytes_are_a_permanent_failure()
    {
        var ex = Should.Throw<IngestionException>(() => _extractor.Extract("%PDF-1.7 but then garbage"u8.ToArray(), TestContext.Current.CancellationToken));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldBe("The file is not a valid PDF document.");
    }

    [Fact]
    public void Cancellation_is_observed_between_pages()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => _extractor.Extract(TestDocuments.QuarterlyReportPdf(), cancelled.Token));
    }
}
