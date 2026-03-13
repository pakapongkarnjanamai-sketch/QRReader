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
        PdfPageWord currentWord = processor.NextWord();
        while (currentWord != null)
        {
            string wordText = string.Join("", currentWord.Segments.Select(s => s.Text));
            if (currentWord.Rectangles.Count > 0)
            {
                var rect = currentWord.Rectangles[0];
                words.Add(new WordInfo(
                    wordText,
                    currentWord.PageNumber,
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    rect.Height
                ));
            }
            currentWord = processor.NextWord();
        }
        return words;
    }
}
