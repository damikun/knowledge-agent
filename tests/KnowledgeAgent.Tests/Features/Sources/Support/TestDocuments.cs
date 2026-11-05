using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KnowledgeAgent.Tests.Features.Sources.Support;

internal static class TestDocuments
{
    public static byte[] Pdf(string? title, params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        if (title is not null)
        {
            builder.DocumentInformation.Title = title;
        }

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780d;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                {
                    page.AddText(line, 12, new PdfPoint(50, y), font);
                }

                y -= line.Length == 0 ? 36 : 16;
            }
        }

        return builder.Build();
    }

    public static byte[] QuarterlyReportPdf(string? title = "Quarterly Report") => Pdf(
        title,
        ["Quarterly report overview", "", "Revenue grew by twelve percent compared to the previous quarter.", "Most of the growth came from the subscription business."],
        ["Expenses and hiring", "", "Operating expenses stayed flat while the team hired four engineers.", "Hiring will continue in the next quarter."]);

    public const string ArticleHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Understanding Vector Databases | Example Engineering Blog</title>
          <meta property="og:site_name" content="Example Engineering">
          <meta property="og:title" content="Understanding Vector Databases">
          <style>body { font-family: sans-serif; } .secret-style-marker { color: red; }</style>
          <script>window.trackingMarker = 'TRACKING_SCRIPT_MARKER'; function track() { console.log('tracking'); }</script>
        </head>
        <body class="has-sidebar">
          <header class="site-header">
            <a href="/">Example</a>
            <nav><ul><li><a href="/blog">Blog</a></li><li><a href="/about">About us NAVIGATION_MARKER</a></li></ul></nav>
          </header>
          <div id="cookie-banner">We use cookies to improve your experience. COOKIE_MARKER Accept all cookies?</div>
          <main>
            <article>
              <h1>Understanding Vector Databases</h1>
              <p class="byline">By Jane Doe</p>
              <p>Vector databases store embeddings, which are numerical representations of text, images or audio. They make it possible to search by meaning instead of by exact keywords, which is the foundation of retrieval augmented generation.</p>
              <h2>How similarity search works</h2>
              <p>Each document is split into chunks and every chunk is converted into a vector by an embedding model. At query time the question is embedded with the same model and the database returns the nearest vectors using cosine similarity.</p>
              <ul><li>Cosine similarity compares directions.</li><li>Dot product also considers magnitude.</li></ul>
              <script>track('INLINE_SCRIPT_MARKER');</script>
              <h2>Choosing a database</h2>
              <p>ClickHouse, PostgreSQL with pgvector and dedicated engines all support approximate nearest neighbour indexes. For small collections a brute force scan is often fast enough and much simpler to operate.</p>
            </article>
          </main>
          <aside class="sidebar"><h3>Related posts</h3><ul><li><a href="/a">Ten tips for SQL SIDEBAR_MARKER</a></li></ul></aside>
          <footer><p>Copyright 2026 Example Inc. FOOTER_MARKER All rights reserved.</p></footer>
        </body>
        </html>
        """;

    public static string Prose(int paragraphs, int sentencesPerParagraph = 6, string topic = "knowledge")
    {
        var builder = new StringBuilder();
        for (var p = 0; p < paragraphs; p++)
        {
            if (p > 0)
            {
                builder.Append("\n\n");
            }

            for (var s = 0; s < sentencesPerParagraph; s++)
            {
                if (s > 0)
                {
                    builder.Append(' ');
                }

                builder.Append($"Paragraph {p} sentence {s} explains how the {topic} base keeps answers grounded in sources.");
            }
        }

        return builder.ToString();
    }
}
