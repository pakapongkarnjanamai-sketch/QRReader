using PDFReaderV2.Models;

namespace PDFReaderV2.Interfaces;

public interface IResultExporter
{
    void ExportToCsv(string filePath, IEnumerable<DisplayRow> rows);
}
