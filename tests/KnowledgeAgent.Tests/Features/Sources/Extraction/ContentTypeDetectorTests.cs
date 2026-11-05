using System.Text;
using KnowledgeAgent.Features.Sources.Extraction;
using KnowledgeAgent.Features.Sources.Ingestion;
using KnowledgeAgent.Tests.Features.Sources.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeAgent.Tests.Features.Sources.Extraction;

public sealed class ContentTypeDetectorTests
{
    [Fact]
    public void Pdf_magic_bytes_win_over_headers_and_extensions()
    {
        var pdf = TestDocuments.QuarterlyReportPdf();

        ContentTypeDetector.Detect(pdf, "text/html", "page.html").ToString().ShouldBe("Pdf");
        ContentTypeDetector.LooksLikePdf(pdf).ShouldBeTrue();
        ContentTypeDetector.LooksLikePdf([.. "garbage\n"u8, .. pdf]).ShouldBeTrue();
        ContentTypeDetector.LooksLikePdf("hello"u8).ShouldBeFalse();
    }

    [Theory]
    [InlineData("text/html; charset=utf-8", null, "Html")]
    [InlineData("application/xhtml+xml", null, "Html")]
    [InlineData("text/markdown", null, "Markdown")]
    [InlineData("text/x-markdown", "notes.txt", "Markdown")]
    [InlineData("text/plain", "/docs/readme.md", "Markdown")]
    [InlineData("text/plain", "/raw/page.html", "Html")]
    [InlineData("text/plain", "notes.txt", "PlainText")]
    [InlineData("text/csv", null, "PlainText")]
    [InlineData("application/octet-stream", "guide.markdown", "Markdown")]
    [InlineData(null, "page.htm", "Html")]
    [InlineData(null, "document.pdf", "Pdf")]
    [InlineData("application/pdf", null, "Pdf")]
    [InlineData("image/png", "picture.txt", "Unknown")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "report.docx", "Unknown")]
    public void Kind_comes_from_media_type_then_extension(string? mediaType, string? fileName, string expected)
    {
        ContentTypeDetector.Detect("plain words only"u8, mediaType, fileName).ToString().ShouldBe(expected);
    }

    [Fact]
    public void Content_is_sniffed_when_nothing_else_is_known()
    {
        ContentTypeDetector.Detect("  <!DOCTYPE html><html><body>x</body></html>"u8, null, null).ToString().ShouldBe("Html");
        ContentTypeDetector.Detect("<div>fragment</div><body>"u8, "application/octet-stream", "/download").ToString().ShouldBe("Html");
        ContentTypeDetector.Detect("Just some notes."u8, null, null).ToString().ShouldBe("PlainText");
        ContentTypeDetector.Detect([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x08], null, null).ToString().ShouldBe("Unknown");
    }

    [Theory]
    [InlineData("text/html; charset=UTF-8", "text/html")]
    [InlineData("  TEXT/Plain ", "text/plain")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not a media type", null)]
    public void Media_types_are_normalized(string? contentType, string? expected)
    {
        ContentTypeDetector.NormalizeMediaType(contentType).ShouldBe(expected);
    }

    [Fact]
    public void Utf8_bom_is_stripped()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("héllo")];

        TextDecoding.Decode(bytes, null).ShouldBe("héllo");
    }

    [Fact]
    public void Utf16_with_bom_is_decoded_and_counts_as_text()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("wide text")).ToArray();

        ContentTypeDetector.LooksLikeText(bytes).ShouldBeTrue();
        TextDecoding.Decode(bytes, null).ShouldBe("wide text");
    }

    [Fact]
    public void Declared_charset_is_honoured()
    {
        var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        TextDecoding.Decode(windows1252.GetBytes("café – crème"), "windows-1252").ShouldBe("café – crème");
    }

    [Fact]
    public void Invalid_utf8_falls_back_to_windows_1252()
    {
        TextDecoding.Decode([0x63, 0x61, 0x66, 0xE9], null).ShouldBe("café");
    }

    [Fact]
    public void Html_meta_charset_is_used_when_the_header_has_none()
    {
        var latin1 = Encoding.Latin1.GetBytes("<html><head><meta charset=\"iso-8859-1\"></head><body>Über</body></html>");

        TextDecoding.DecodeHtml(latin1, null).ShouldContain("Über");
    }

    [Fact]
    public void Binary_payloads_are_not_text()
    {
        ContentTypeDetector.LooksLikeText([0x01, 0x00, 0x02, 0x03]).ShouldBeFalse();
        ContentTypeDetector.LooksLikeText(TestDocuments.QuarterlyReportPdf()).ShouldBeFalse();
        ContentTypeDetector.LooksLikeText("plain"u8).ShouldBeTrue();
    }

    [Fact]
    public void Extractor_rejects_unsupported_content_permanently()
    {
        var extractor = new ContentExtractor(
            new HtmlTextExtractor(NullLogger<HtmlTextExtractor>.Instance),
            new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance));

        var ex = Should.Throw<IngestionException>(() => extractor.Extract([0x89, 0x50, 0x4E, 0x47, 0x00], new ContentDescriptor("image/png"), TestContext.Current.CancellationToken));

        ex.IsPermanent.ShouldBeTrue();
        ex.Message.ShouldContain("image/png");
    }

    [Fact]
    public void Extractor_decodes_text_and_markdown_preserving_indentation()
    {
        var extractor = new ContentExtractor(
            new HtmlTextExtractor(NullLogger<HtmlTextExtractor>.Instance),
            new PdfTextExtractor(NullLogger<PdfTextExtractor>.Instance));
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Title\r\n\r\n\r\n    code  block\n")];

        var document = extractor.Extract(bytes, new ContentDescriptor(FileName: "notes.md"), TestContext.Current.CancellationToken);

        document.Kind.ShouldBe(ContentKind.Markdown);
        document.Text.ShouldBe("# Title\n\n    code block");
        document.PageCount.ShouldBeNull();
    }
}
