using System.ComponentModel;

namespace PDFReaderV2.Models;

public class DisplayRow
{
    public int No { get; set; }
    public int Page { get; set; }
    public string QRContent { get; set; } = "";
    public string Label { get; set; } = "";
}
