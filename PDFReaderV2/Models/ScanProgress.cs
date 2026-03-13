namespace PDFReaderV2.Models;

public class ScanProgress
{
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public string StepText { get; set; } = "";
    public string EtaText { get; set; } = "";
    public string AvgPerPageText { get; set; } = "";
    public int FoundCount { get; set; }
}
