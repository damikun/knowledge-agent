using System.Net;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KnowledgeAgent.Api.Tests.Support;

public static class TestDocuments
{
    public static string HtmlArticle(string title, IEnumerable<string> paragraphs)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var body = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            body.Append("      <p>").Append(WebUtility.HtmlEncode(paragraph)).AppendLine("</p>");
        }

        return $$"""
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                <title>{{encodedTitle}}</title>
                <meta property="og:title" content="{{encodedTitle}}">
              </head>
              <body>
                <nav><a href="/">Home</a> | <a href="/about">About</a></nav>
                <main>
                  <article>
                    <h1>{{encodedTitle}}</h1>
            {{body}}
                  </article>
                </main>
                <footer>Copyright Test Site</footer>
              </body>
            </html>
            """;
    }

    public static byte[] Pdf(params string[] pageTexts)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 790.0;
            foreach (var line in Wrap(text, 90))
            {
                page.AddText(line, 11, new PdfPoint(40, y), font);
                y -= 15;
                if (y < 40)
                {
                    break;
                }
            }
        }

        return builder.Build();
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > width)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
