using PDFReaderV2.Models;

namespace PDFReaderV2.Interfaces;

public interface IPdfTextExtractor
{
    List<WordInfo> ExtractAllWords(string pdfPath);
    int GetPageCount(string pdfPath);
}
