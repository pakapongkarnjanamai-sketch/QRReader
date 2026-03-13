using PDFReaderV2.Models;

namespace PDFReaderV2.Interfaces;

public interface IQrScannerService
{
    Task ScanAsync(
        string pdfPath,
        Action<ScanResult> onResultFound,
        Action<ScanProgress> onProgressChanged,
        ManualResetEventSlim pauseEvent,
        CancellationToken cancellationToken);
}
