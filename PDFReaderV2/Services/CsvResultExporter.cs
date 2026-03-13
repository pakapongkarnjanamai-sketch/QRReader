using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;

namespace PDFReaderV2.Services;

public class CsvResultExporter : IResultExporter
{
    public void ExportToCsv(string filePath, IEnumerable<DisplayRow> rows)
    {
        using var sw = new StreamWriter(filePath);
        sw.WriteLine("No,Page,QRContent,Label");
        foreach (var row in rows)
        {
            sw.WriteLine($"{row.No},{row.Page},\"{row.QRContent}\",\"{row.Label}\"");
        }
    }
}
