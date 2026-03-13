using System.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DevExpress.Pdf;
using PDFReaderV2.Helpers;
using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;
using ZXingCpp;

namespace PDFReaderV2.Services;

public class QrScannerService : IQrScannerService
{
    private const int GridRows = 8;
    private const int GridCols = 7;
    private const int RenderSize = 2400;
    private static readonly Regex NumericOnly = new(@"^\d+$", RegexOptions.Compiled);

    private readonly ILabelFinder _labelFinder;

    public QrScannerService(ILabelFinder labelFinder)
    {
        _labelFinder = labelFinder;
    }

    public async Task ScanAsync(
        string pdfPath,
        Action<ScanResult> onResultFound,
        Action<ScanProgress> onProgressChanged,
        ManualResetEventSlim pauseEvent,
        CancellationToken cancellationToken)
    {
        var progress = new ScanProgress();
        var scannedQRCodes = new ConcurrentDictionary<string, byte>();

        await Task.Run(() =>
        {
            progress.StepText = "Loading PDF document...";
            onProgressChanged(progress);

            using var processor = new PdfDocumentProcessor();
            processor.LoadDocument(pdfPath);
            progress.TotalPages = processor.Document.Pages.Count;

            progress.StepText = "Extracting text...";
            onProgressChanged(progress);

            var allWords = PdfTextExtractor.ExtractWords(processor);

            var numericRegex = new Regex(@"^\d+$", RegexOptions.Compiled);
            var wordsByPage = allWords
                .Where(w => numericRegex.IsMatch(w.Text))
                .GroupBy(w => w.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            progress.StepText = "Scanning page";
            var scanStopwatch = Stopwatch.StartNew();

            for (int page = 1; page <= progress.TotalPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pauseEvent.Wait(cancellationToken);

                progress.CurrentPage = page;

                var pdfPage = processor.Document.Pages[page - 1];
                double pageHeight = pdfPage.CropBox.Height;
                double pageWidth = pdfPage.CropBox.Width;

                wordsByPage.TryGetValue(page, out var pageWords);
                pageWords ??= [];

                var swRender = Stopwatch.StartNew();
                using var originalImage = processor.CreateBitmap(page, RenderSize);
                swRender.Stop();

                double scaleX = originalImage.Width / pageWidth;
                double scaleY = originalImage.Height / pageHeight;

                var swDecode = Stopwatch.StartNew();

                // Full-page decode with ZXingCpp (native C++, 10-50x faster than ZXing.Net)
                var barcodes = ReadBarcodesFromBitmap(originalImage);

                int foundOnPage = 0;
                foreach (var barcode in barcodes)
                {
                    string qrText = barcode.Text?.Trim()!;
                    if (string.IsNullOrEmpty(qrText) || !barcode.IsValid
                        || NumericOnly.IsMatch(qrText))
                        continue;

                    if (scannedQRCodes.TryAdd(qrText, 0))
                    {
                        string labelText = FindLabelFromBarcode(barcode, scaleX, scaleY, pageWords);
                        onResultFound(new ScanResult(page, qrText, labelText));
                    }
                    foundOnPage++;
                }

                // Parallel grid fallback for missed QR codes
                if (foundOnPage < 56)
                {
                    var cells = PrepareCells(originalImage);

                    Parallel.ForEach(cells, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        CancellationToken = cancellationToken
                    },
                    (cell) =>
                    {
                        var cellResults = ReadBarcodesFromBitmap(cell.Bitmap);
                        foreach (var result in cellResults)
                        {
                            string? text = result.Text?.Trim();
                            if (string.IsNullOrEmpty(text) || !result.IsValid
                                || NumericOnly.IsMatch(text))
                                continue;

                            if (scannedQRCodes.TryAdd(text, 0))
                            {
                                string labelText = _labelFinder.FindLabel(pageWords,
                                    cell.PdfLeft(scaleX), cell.PdfTop(scaleY),
                                    cell.PdfRight(scaleX), cell.PdfBottom(scaleY));

                                onResultFound(new ScanResult(page, text, labelText));
                            }
                        }
                    });

                    foreach (var cell in cells)
                        cell.Bitmap.Dispose();
                }

                swDecode.Stop();

                Debug.WriteLine($"[Page {page}] Render: {swRender.ElapsedMilliseconds}ms | Decode: {swDecode.ElapsedMilliseconds}ms | Found: {scannedQRCodes.Count}");

                progress.FoundCount = scannedQRCodes.Count;
                double scanElapsed = scanStopwatch.Elapsed.TotalSeconds;
                double avgPerPage = scanElapsed / page;
                int remaining = progress.TotalPages - page;
                progress.EtaText = TimeFormatter.Format(avgPerPage * remaining);
                progress.AvgPerPageText = $"{avgPerPage:F1}s";
                onProgressChanged(progress);
            }
        }, cancellationToken);
    }

    private static Barcode[] ReadBarcodesFromBitmap(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = bitmapData.Stride * bitmapData.Height;
            var pixels = new byte[byteCount];
            Marshal.Copy(bitmapData.Scan0, pixels, 0, byteCount);

            var iv = new ImageView(
                pixels,
                bitmapData.Width,
                bitmapData.Height,
                ZXingCpp.ImageFormat.BGRA,
                bitmapData.Stride);

            var reader = new BarcodeReader()
            {
                Formats = BarcodeFormat.QRCode,
                TryInvert = true,
                TextMode = TextMode.Plain,
                MaxNumberOfSymbols = 64
            };

            return reader.From(iv);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private string FindLabelFromBarcode(Barcode barcode, double scaleX, double scaleY, List<WordInfo> pageWords)
    {
        var pos = barcode.Position;

        float minX = Math.Min(Math.Min(pos.TopLeft.X, pos.TopRight.X), Math.Min(pos.BottomLeft.X, pos.BottomRight.X));
        float maxX = Math.Max(Math.Max(pos.TopLeft.X, pos.TopRight.X), Math.Max(pos.BottomLeft.X, pos.BottomRight.X));
        float minY = Math.Min(Math.Min(pos.TopLeft.Y, pos.TopRight.Y), Math.Min(pos.BottomLeft.Y, pos.BottomRight.Y));
        float maxY = Math.Max(Math.Max(pos.TopLeft.Y, pos.TopRight.Y), Math.Max(pos.BottomLeft.Y, pos.BottomRight.Y));

        float qrWidth = maxX - minX;
        float qrHeight = maxY - minY;
        float stickerLeft = minX - qrWidth * 0.3f;
        float stickerRight = maxX + qrWidth * 0.3f;
        float stickerTop = maxY;                    // label area starts at bottom of QR
        float stickerBottom = maxY + qrHeight * 0.8f; // label area extends below QR

        return _labelFinder.FindLabel(pageWords,
            stickerLeft / scaleX, stickerTop / scaleY,
            stickerRight / scaleX, stickerBottom / scaleY);
    }

    private record CellInfo(Bitmap Bitmap, int X, int Y, int Width, int Height)
    {
        public double PdfLeft(double scaleX) => X / scaleX;
        public double PdfTop(double scaleY) => Y / scaleY;
        public double PdfRight(double scaleX) => (X + Width) / scaleX;
        public double PdfBottom(double scaleY) => (Y + Height) / scaleY;
    }

    private static List<CellInfo> PrepareCells(Bitmap originalImage)
    {
        int cellWidth = originalImage.Width / GridCols;
        int cellHeight = originalImage.Height / GridRows;
        var cells = new List<CellInfo>(GridRows * GridCols * 2);

        AddGridCells(cells, originalImage, cellWidth, cellHeight, 0, 0);

        int offsetX = cellWidth / 2;
        int offsetY = cellHeight / 2;
        AddGridCells(cells, originalImage, cellWidth, cellHeight, offsetX, offsetY);

        return cells;
    }

    private static void AddGridCells(List<CellInfo> cells, Bitmap originalImage,
        int cellWidth, int cellHeight, int offsetX, int offsetY)
    {
        int imgW = originalImage.Width;
        int imgH = originalImage.Height;

        for (int y = offsetY; y < imgH; y += cellHeight)
        {
            for (int x = offsetX; x < imgW; x += cellWidth)
            {
                int w = Math.Min(cellWidth, imgW - x);
                int h = Math.Min(cellHeight, imgH - y);

                if (w < cellWidth / 4 || h < cellHeight / 4)
                    continue;

                var cropped = new Bitmap(w, h);
                using (var g = Graphics.FromImage(cropped))
                {
                    g.DrawImage(originalImage,
                        new Rectangle(0, 0, w, h),
                        new Rectangle(x, y, w, h),
                        GraphicsUnit.Pixel);
                }

                cells.Add(new CellInfo(cropped, x, y, w, h));
            }
        }
    }
}
