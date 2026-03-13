using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DevExpress.Pdf;
using PDFReaderV2.Helpers;
using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;
using ZXing;
using ZXing.Common;

namespace PDFReaderV2.Services;

public class QrScannerService : IQrScannerService
{
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
        var scannedQRCodes = new HashSet<string>();

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

            var reader = CreateBarcodeReader();

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

                using (var originalImage = processor.CreateBitmap(page, 2400))
                {
                    double scaleX = originalImage.Width / pageWidth;
                    double scaleY = originalImage.Height / pageHeight;

                    int foundOnPage = 0;

                    var qrResults = reader.DecodeMultiple(originalImage);
                    if (qrResults != null)
                    {
                        foreach (var qrResult in qrResults)
                        {
                            string qrText = qrResult.Text;
                            if (string.IsNullOrEmpty(qrText) || !scannedQRCodes.Add(qrText))
                                continue;

                            string labelText = FindLabelFromQrResult(qrResult, scaleX, scaleY, pageWords);

                            onResultFound(new ScanResult(page, qrText, labelText));
                            foundOnPage++;
                        }
                    }

                    if (foundOnPage < 56)
                    {
                        ScanWithGridFallback(originalImage, reader, scannedQRCodes,
                            pageWords, scaleX, scaleY, page, onResultFound);
                    }
                }

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

    private string FindLabelFromQrResult(Result qrResult, double scaleX, double scaleY, List<WordInfo> pageWords)
    {
        var points = qrResult.ResultPoints;
        if (points == null || points.Length < 2)
            return "";

        float minX = points.Min(p => p.X);
        float maxX = points.Max(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxY = points.Max(p => p.Y);

        float qrWidth = maxX - minX;
        float qrHeight = maxY - minY;
        float stickerLeft = minX - qrWidth * 0.3f;
        float stickerRight = maxX + qrWidth * 0.3f;
        float stickerTop = minY - qrHeight * 0.3f;
        float stickerBottom = maxY + qrHeight * 0.8f;

        return _labelFinder.FindLabel(pageWords,
            stickerLeft / scaleX, stickerTop / scaleY,
            stickerRight / scaleX, stickerBottom / scaleY);
    }

    private void ScanWithGridFallback(Bitmap originalImage, BarcodeReader<Bitmap> reader,
        HashSet<string> scannedQRCodes, List<WordInfo> pageWords,
        double scaleX, double scaleY, int page, Action<ScanResult> onResultFound)
    {
        int gridRows = 8;
        int gridCols = 7;
        int cellWidth = originalImage.Width / gridCols;
        int cellHeight = originalImage.Height / gridRows;

        for (int row = 0; row < gridRows; row++)
        {
            for (int col = 0; col < gridCols; col++)
            {
                Rectangle cropRect = new Rectangle(
                    col * cellWidth, row * cellHeight, cellWidth, cellHeight);

                if (cropRect.Right > originalImage.Width)
                    cropRect.Width = originalImage.Width - cropRect.X;
                if (cropRect.Bottom > originalImage.Height)
                    cropRect.Height = originalImage.Height - cropRect.Y;

                using (Bitmap croppedImage = new Bitmap(cropRect.Width, cropRect.Height))
                {
                    using (Graphics g = Graphics.FromImage(croppedImage))
                    {
                        g.DrawImage(originalImage,
                            new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                            cropRect, GraphicsUnit.Pixel);
                    }

                    var result = reader.Decode(croppedImage);
                    if (result != null && scannedQRCodes.Add(result.Text))
                    {
                        string labelText = _labelFinder.FindLabel(pageWords,
                            cropRect.X / scaleX, cropRect.Y / scaleY,
                            (cropRect.X + cropRect.Width) / scaleX,
                            (cropRect.Y + cropRect.Height) / scaleY);

                        onResultFound(new ScanResult(page, result.Text, labelText));
                    }
                }
            }
        }
    }

    private static BarcodeReader<Bitmap> CreateBarcodeReader()
    {
        var reader = new BarcodeReader<Bitmap>(
            (bitmap) =>
            {
                var width = bitmap.Width;
                var height = bitmap.Height;
                var pixels = new byte[width * height * 4];
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }
                return new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
            });

        reader.Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            CharacterSet = "UTF-8",
            TryInverted = true
        };

        return reader;
    }
}
