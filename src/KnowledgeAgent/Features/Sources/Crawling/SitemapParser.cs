using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace KnowledgeAgent.Features.Sources.Crawling;

internal sealed record SitemapEntry(string Url, DateTimeOffset? LastModified);

internal sealed record Sitemap(IReadOnlyList<SitemapEntry> Pages, IReadOnlyList<string> Sitemaps);

// Reads a urlset, a sitemap index or a plain list of URLs, gzipped or not. DTDs are refused, so entity tricks
// (billion laughs, external entities) cannot run.
internal static class SitemapParser
{
    public const int MaxEntries = 50_000;

    public static Sitemap Parse(byte[] content, long maxBytes)
    {
        var bytes = IsGzip(content) ? Decompress(content, maxBytes) : content;
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith('<') ? ParseXml(text) : ParseText(text);
    }

    public static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;

    private static Sitemap ParseXml(string xml)
    {
        var pages = new List<SitemapEntry>();
        var sitemaps = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        string? loc = null;
        string? lastmod = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            reader.MoveToContent();
            while (!reader.EOF && pages.Count + sitemaps.Count < MaxEntries)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "loc" or "lastmod")
                {
                    // Reading the content moves past the end tag, so the loop must not Read again.
                    var name = reader.LocalName;
                    var value = reader.ReadElementContentAsString().Trim();
                    if (name == "loc")
                    {
                        loc = value;
                    }
                    else
                    {
                        lastmod = value;
                    }

                    continue;
                }

                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "url" or "sitemap")
                {
                    loc = null;
                    lastmod = null;
                }
                else if (reader.NodeType == XmlNodeType.EndElement && loc is { Length: > 0 })
                {
                    if (reader.LocalName == "url")
                    {
                        pages.Add(new SitemapEntry(loc, ParseDate(lastmod)));
                        loc = null;
                    }
                    else if (reader.LocalName == "sitemap")
                    {
                        sitemaps.Add(loc);
                        loc = null;
                    }
                }

                reader.Read();
            }
        }
        catch (XmlException)
        {
            // A broken sitemap still yields the entries read before the error.
        }

        return new Sitemap(pages, sitemaps);
    }

    private static Sitemap ParseText(string text)
    {
        var pages = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Take(MaxEntries)
            .Select(line => new SitemapEntry(line, null))
            .ToList();
        return new Sitemap(pages, []);
    }

    private static bool IsGzip(byte[] content) => content.Length > 2 && content[0] == 0x1F && content[1] == 0x8B;

    private static byte[] Decompress(byte[] content, long maxBytes)
    {
        using var input = new GZipStream(new MemoryStream(content), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            if (output.Length + read > maxBytes)
            {
                break;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
