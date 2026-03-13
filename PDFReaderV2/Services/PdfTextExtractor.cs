using DevExpress.Pdf;
using DevExpress.Pdf;
using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;

namespace PDFReaderV2.Services;

public class PdfTextExtractor : IPdfTextExtractor
{
    public int GetPageCount(string pdfPath)
    {
        using var processor = new PdfDocumentProcessor();
        processor.LoadDocument(pdfPath);
        return processor.Document.Pages.Count;
    }

    public List<WordInfo> ExtractAllWords(string pdfPath)
    {
        using var processor = new PdfDocumentProcessor();
        processor.LoadDocument(pdfPath);
        return ExtractWords(processor);
    }

    public static List<WordInfo> ExtractWords(PdfDocumentProcessor processor)
    {
        var words = new List<WordInfo>();
        var pageHeights = new Dictionary<int, double>();
        foreach (var page in processor.Document.Pages)
        {
            int pageNum = processor.Document.Pages.IndexOf(page) + 1;
            pageHeights[pageNum] = page.CropBox.Height;
        }

        PdfPageWord currentWord = processor.NextWord();
        while (currentWord != null)
        {
            string wordText = string.Join("", currentWord.Segments.Select(s => s.Text));
            if (currentWord.Rectangles.Count > 0)
            {
                var rect = currentWord.Rectangles[0];
                double pgHeight = pageHeights.GetValueOrDefault(currentWord.PageNumber);

                // Convert from page coordinates (Y increases upward, origin bottom-left)
                // to top-down coordinates (Y increases downward, origin top-left)
                // so that WordInfo.Top matches bitmap/image coordinate direction.
                double topDown = pgHeight - rect.Top;

                words.Add(new WordInfo(
                    wordText,
                    currentWord.PageNumber,
                    rect.Left,
                    topDown,
                    rect.Width,
                    rect.Height
                ));
            }
            currentWord = processor.NextWord();
        }
        return words;
    }
}
