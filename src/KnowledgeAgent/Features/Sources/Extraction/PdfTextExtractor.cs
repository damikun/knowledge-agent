using System.Text.RegularExpressions;
using KnowledgeAgent.Features.Sources.Ingestion;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace KnowledgeAgent.Features.Sources.Extraction;

internal sealed partial class PdfTextExtractor(ILogger<PdfTextExtractor> logger)
{
    private const int MaxTitleLength = 200;

    public ExtractedDocument Extract(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(content, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new IngestionException("The PDF is password protected and cannot be read.", isPermanent: true, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IngestionException("The file is not a valid PDF document.", isPermanent: true, ex);
        }

        using (document)
        {
            int pageCount;
            try
            {
                pageCount = document.NumberOfPages;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new IngestionException("The PDF document is corrupted.", isPermanent: true, ex);
            }

            var pages = new string[pageCount];
            for (var number = 1; number <= pageCount; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pages[number - 1] = ExtractPage(document, number);
            }

            var title = CleanInfoTitle(ReadInfoTitle(document)) ?? FirstLineTitle(pages);
            return new ExtractedDocument(string.Join(TextNormalizer.PageSeparator, pages), title, pageCount, ContentKind.Pdf);
        }
    }

    private string ExtractPage(PdfDocument document, int number)
    {
        try
        {
            var page = document.GetPage(number);
            var letters = DuplicateOverlappingTextProcessor.Get(page.Letters);
            if (letters.Count == 0)
            {
                return string.Empty;
            }

            string text;
            try
            {
                var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
                var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks);
                text = string.Join("\n\n", ordered.Select(block => block.Text));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLayoutAnalysisFailed(logger, ex, number);
                text = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);
            }

            return TextNormalizer.Normalize(text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One unreadable page must not fail the whole document.
            LogPageFailed(logger, ex, number);
            return string.Empty;
        }
    }

    private static string? ReadInfoTitle(PdfDocument document)
    {
        try
        {
            return document.Information.Title;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    internal static string? CleanInfoTitle(string? title)
    {
        var cleaned = TextNormalizer.Normalize(title).Replace('\n', ' ');
        cleaned = OfficePrefix().Replace(cleaned, string.Empty);
        cleaned = FileExtensionSuffix().Replace(cleaned, string.Empty).Trim();
        if (cleaned.Length < 2
            || cleaned.Equals("untitled", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("untitled ", StringComparison.OrdinalIgnoreCase)
            || !cleaned.Any(char.IsLetter))
        {
            return null;
        }

        return TextUtilities.Truncate(cleaned, MaxTitleLength);
    }

    internal static string? FirstLineTitle(IEnumerable<string> pages)
    {
        var firstPage = pages.FirstOrDefault(page => !string.IsNullOrWhiteSpace(page));
        if (firstPage is null)
        {
            return null;
        }

        var line = firstPage.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(candidate => candidate.Count(char.IsLetter) >= 3);
        return line is { Length: <= MaxTitleLength } ? line : null;
    }

    [GeneratedRegex(@"^Microsoft (Word|PowerPoint|Excel) - ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OfficePrefix();

    [GeneratedRegex(@"\.(docx?|pptx?|xlsx?|odt|pdf|rtf|txt)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileExtensionSuffix();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Layout analysis failed on PDF page {PageNumber}; using content order")]
    private static partial void LogLayoutAnalysisFailed(ILogger logger, Exception exception, int pageNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Text of PDF page {PageNumber} could not be extracted; the page is left empty")]
    private static partial void LogPageFailed(ILogger logger, Exception exception, int pageNumber);
}
