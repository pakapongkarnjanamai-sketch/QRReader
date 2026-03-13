using DevExpress.Pdf;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraPdfViewer;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using Button = System.Windows.Forms.Button;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace PDFReaderV2
{
    public partial class Form1 : Form
    {
        private string? currentPdfPath;
        private TextBox txtPdfPath;
        private Button btnBrowse;
        private Button btnProcess;
        private DevExpress.XtraGrid.GridControl gridResults;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private PdfViewer pdfViewer;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private MenuStrip menuStrip;
        private DevExpress.XtraEditors.SplitContainerControl splitContainer;
        private SkeletonPanel skeletonPanel;
        private HashSet<string> scannedQRCodes = new HashSet<string>();

        public Form1()
        {
            InitializeComponent();
            InitializeControls();
            SetupEventHandlers();
            AddMenuBar();
        }

        private void InitializeControls()
        {
            this.Size = new Size(1200, 800);
            this.Text = "PDF Reader V2 - QR Code + Label Scanner";

            // Status Strip
            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready");
            statusStrip.Items.Add(statusLabel);

            // File Selection Controls
            txtPdfPath = new TextBox
            {
                Location = new Point(10, 40),
                Width = 350,
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            btnBrowse = new Button
            {
                Location = new Point(370, 39),
                Text = "Browse",
                Width = 80,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            btnProcess = new Button
            {
                Location = new Point(460, 39),
                Text = "Process",
                Width = 80,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            // Grid Results
            gridResults = new DevExpress.XtraGrid.GridControl
            {
                Dock = DockStyle.Fill
            };

            gridView = new DevExpress.XtraGrid.Views.Grid.GridView(gridResults);
            gridResults.MainView = gridView;

            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsCustomization.AllowSort = true;
            gridView.OptionsSelection.EnableAppearanceFocusedRow = true;

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "No",
                Caption = "No",
                Visible = true,
                Width = 50,
                VisibleIndex = 0,
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Page",
                Caption = "Page",
                Visible = true,
                Width = 60
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "QRContent",
                Caption = "QR Content",
                Visible = true,
                Width = 250
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Label",
                Caption = "Label (Number Below)",
                Visible = true,
                Width = 150
            });

            // Split Container
            splitContainer = new DevExpress.XtraEditors.SplitContainerControl
            {
                Location = new Point(10, 100),
                Size = new Size(1170, 630),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                FixedPanel = DevExpress.XtraEditors.SplitFixedPanel.None,
                SplitterPosition = 550
            };

            // PDF Viewer
            pdfViewer = new PdfViewer
            {
                Dock = DockStyle.Fill
            };
            pdfViewer.ZoomMode = PdfZoomMode.FitToWidth;

            // Skeleton Loader (overlays on grid area)
            skeletonPanel = new SkeletonPanel
            {
                Dock = DockStyle.Fill,
                Visible = false
            };

            splitContainer.Panel1.Controls.Add(skeletonPanel);
            splitContainer.Panel1.Controls.Add(gridResults);
            skeletonPanel.BringToFront();
            splitContainer.Panel2.Controls.Add(pdfViewer);

            this.Controls.AddRange(new Control[] {
                txtPdfPath, btnBrowse, btnProcess,
                splitContainer,
                statusStrip
            });
        }

        private void SetupEventHandlers()
        {
            btnBrowse.Click += BtnBrowse_Click;
            btnProcess.Click += BtnProcess_Click;
            gridView.RowClick += GridView_RowClick;
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "PDF files (*.pdf)|*.pdf";
                openFileDialog.FilterIndex = 1;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentPdfPath = openFileDialog.FileName;
                    txtPdfPath.Text = currentPdfPath;
                    pdfViewer.LoadDocument(currentPdfPath);
                    statusLabel.Text = $"Loaded: {Path.GetFileName(currentPdfPath)}";
                }
            }
        }

        private async void BtnProcess_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentPdfPath))
            {
                MessageBox.Show("Please select a PDF file first.");
                return;
            }

            btnProcess.Enabled = false;
            scannedQRCodes.Clear();
            gridResults.DataSource = null;
            statusLabel.Text = "Processing...";

            // Show skeleton loader
            skeletonPanel.ResetProgress();
            skeletonPanel.Visible = true;
            skeletonPanel.BringToFront();
            skeletonPanel.StartAnimation();

            try
            {
                await ScanQRCodeWithLabels();
                var dataSource = gridResults.DataSource as List<object>;
                statusLabel.Text = $"Scan completed. Found {dataSource?.Count ?? 0} results.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Processing failed";
            }
            finally
            {
                // Hide skeleton loader
                skeletonPanel.StopAnimation();
                skeletonPanel.Visible = false;

                btnProcess.Enabled = true;
            }
        }

        private record WordInfo(string Text, int PageNumber, double Left, double Top, double Width, double Height);

        private List<WordInfo> ExtractAllWords(PdfDocumentProcessor processor)
        {
            var words = new List<WordInfo>();
            PdfPageWord currentWord = processor.NextWord();
            while (currentWord != null)
            {
                string wordText = string.Join("", currentWord.Segments.Select(s => s.Text));
                if (currentWord.Rectangles.Count > 0)
                {
                    var rect = currentWord.Rectangles[0];
                    // PdfOrientedRectangle: Left = from left edge, Top = from top edge of page
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

        private async Task ScanQRCodeWithLabels()
        {
            await Task.Run(() =>
            {
                var results = new List<object>();
                int rowNo = 1;

                using (PdfDocumentProcessor processor = new PdfDocumentProcessor())
                {
                    processor.LoadDocument(currentPdfPath!);

                    // Step 1: Extract all words with coordinates from the entire document
                    var allWords = ExtractAllWords(processor);

                    var reader = new BarcodeReader<Bitmap>(
                        (bitmap) =>
                        {
                            var width = bitmap.Width;
                            var height = bitmap.Height;
                            var pixels = new byte[width * height * 4];
                            var bitmapData = bitmap.LockBits(
                                new Rectangle(0, 0, width, height),
                                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
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

                    int totalPages = processor.Document.Pages.Count;

                    for (int page = 1; page <= totalPages; page++)
                    {
                        int currentPage = page;
                        this.Invoke((MethodInvoker)delegate
                        {
                            statusLabel.Text = $"Scanning page {currentPage} of {totalPages}...";
                            skeletonPanel.UpdateProgress(currentPage, totalPages, scannedQRCodes.Count);
                        });

                        PdfPage pdfPage = processor.Document.Pages[page - 1];
                        double pageHeight = pdfPage.CropBox.Height;
                        double pageWidth = pdfPage.CropBox.Width;

                        // Get numeric words for this page
                        var pageWords = allWords
                            .Where(w => w.PageNumber == page && Regex.IsMatch(w.Text, @"^\d+$"))
                            .ToList();

                        using (var originalImage = processor.CreateBitmap(page, 2400))
                        {
                            double scaleX = originalImage.Width / pageWidth;
                            double scaleY = originalImage.Height / pageHeight;

                            // Use DecodeMultiple to find ALL QR codes in the full page at once
                            var qrResults = reader.DecodeMultiple(originalImage);

                            if (qrResults != null)
                            {
                                foreach (var qrResult in qrResults)
                                {
                                    string qrText = qrResult.Text;
                                    if (string.IsNullOrEmpty(qrText) || scannedQRCodes.Contains(qrText))
                                        continue;

                                    scannedQRCodes.Add(qrText);

                                    // Get QR code position from result points
                                    string labelText = "";
                                    var points = qrResult.ResultPoints;
                                    if (points != null && points.Length >= 2)
                                    {
                                        // Calculate bounding box of QR code in pixels
                                        float minX = points.Min(p => p.X);
                                        float maxX = points.Max(p => p.X);
                                        float minY = points.Min(p => p.Y);
                                        float maxY = points.Max(p => p.Y);

                                        // Estimate the sticker area (QR + label below)
                                        float qrWidth = maxX - minX;
                                        float qrHeight = maxY - minY;
                                        float stickerLeft = minX - qrWidth * 0.3f;
                                        float stickerRight = maxX + qrWidth * 0.3f;
                                        float stickerTop = minY - qrHeight * 0.3f;
                                        float stickerBottom = maxY + qrHeight * 0.8f;

                                        // Convert to PDF coords
                                        double cellPdfLeft = stickerLeft / scaleX;
                                        double cellPdfTop = stickerTop / scaleY;
                                        double cellPdfRight = stickerRight / scaleX;
                                        double cellPdfBottom = stickerBottom / scaleY;

                                        labelText = FindLabelInCell(pageWords,
                                            cellPdfLeft, cellPdfTop, cellPdfRight, cellPdfBottom);
                                    }

                                    int currentNo = rowNo++;
                                    string currentQr = qrText;
                                    string currentLabel = labelText;

                                    this.Invoke((MethodInvoker)delegate
                                    {
                                        results.Add(new
                                        {
                                            No = currentNo,
                                            Page = currentPage,
                                            QRContent = currentQr,
                                            Label = currentLabel
                                        });
                                        gridResults.DataSource = null;
                                        gridResults.DataSource = results;
                                        skeletonPanel.UpdateProgress(currentPage, totalPages, scannedQRCodes.Count);
                                    });
                                }
                            }

                            // If DecodeMultiple found fewer than expected, fallback to grid scanning
                            int foundOnPage = results.Count(r => ((dynamic)r).Page == page);
                            if (foundOnPage < 56)
                            {
                                // Grid-based fallback scan for missed QR codes
                                int gridRows = 8;
                                int gridCols = 7;
                                int cellWidth = originalImage.Width / gridCols;
                                int cellHeight = originalImage.Height / gridRows;

                                for (int row = 0; row < gridRows; row++)
                                {
                                    for (int col = 0; col < gridCols; col++)
                                    {
                                        Rectangle cropRect = new Rectangle(
                                            col * cellWidth,
                                            row * cellHeight,
                                            cellWidth,
                                            cellHeight
                                        );

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
                                                    cropRect,
                                                    GraphicsUnit.Pixel);
                                            }

                                            var result = reader.Decode(croppedImage);
                                            if (result != null && !scannedQRCodes.Contains(result.Text))
                                            {
                                                scannedQRCodes.Add(result.Text);

                                                double cellPdfLeft = cropRect.X / scaleX;
                                                double cellPdfTop = cropRect.Y / scaleY;
                                                double cellPdfRight = (cropRect.X + cropRect.Width) / scaleX;
                                                double cellPdfBottom = (cropRect.Y + cropRect.Height) / scaleY;

                                                string labelText = FindLabelInCell(pageWords,
                                                    cellPdfLeft, cellPdfTop, cellPdfRight, cellPdfBottom);

                                                int currentNo = rowNo++;
                                                string currentQr = result.Text;
                                                string currentLabel = labelText;

                                                this.Invoke((MethodInvoker)delegate
                                                {
                                                    results.Add(new
                                                    {
                                                        No = currentNo,
                                                        Page = currentPage,
                                                        QRContent = currentQr,
                                                        Label = currentLabel
                                                    });
                                                    gridResults.DataSource = null;
                                                    gridResults.DataSource = results;
                                                    skeletonPanel.UpdateProgress(currentPage, totalPages, scannedQRCodes.Count);
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        private string FindLabelInCell(List<WordInfo> pageWords,
            double cellLeft, double cellTop, double cellRight, double cellBottom)
        {
            // Both cell coords and word coords use the same system:
            // Left = distance from left edge, Top = distance from top edge
            // Find numeric words whose center falls within the cell bounds
            var candidates = pageWords
                .Where(w =>
                {
                    double wordCenterX = w.Left + w.Width / 2;
                    double wordCenterY = w.Top + w.Height / 2;

                    return wordCenterX >= cellLeft && wordCenterX <= cellRight &&
                           wordCenterY >= cellTop && wordCenterY <= cellBottom;
                })
                .OrderBy(w => w.Top)
                .ThenBy(w => w.Left)
                .ToList();

            if (candidates.Count > 0)
            {
                return string.Join("", candidates.Select(c => c.Text));
            }

            // Fallback: expand search area by 20%
            double expandX = (cellRight - cellLeft) * 0.2;
            double expandY = (cellBottom - cellTop) * 0.2;

            var extendedCandidates = pageWords
                .Where(w =>
                {
                    double wordCenterX = w.Left + w.Width / 2;
                    double wordCenterY = w.Top + w.Height / 2;

                    return wordCenterX >= (cellLeft - expandX) && wordCenterX <= (cellRight + expandX) &&
                           wordCenterY >= (cellTop - expandY) && wordCenterY <= (cellBottom + expandY);
                })
                .OrderBy(w => w.Top)
                .ThenBy(w => w.Left)
                .ToList();

            return string.Join("", extendedCandidates.Select(c => c.Text));
        }

        private Bitmap RotateImage(Bitmap bmp, float angle)
        {
            Bitmap rotated = new Bitmap(bmp.Width, bmp.Height);
            using (Graphics g = Graphics.FromImage(rotated))
            {
                g.TranslateTransform(bmp.Width / 2, bmp.Height / 2);
                g.RotateTransform(angle);
                g.TranslateTransform(-bmp.Width / 2, -bmp.Height / 2);
                g.DrawImage(bmp, Point.Empty);
            }
            return rotated;
        }

        private void GridView_RowClick(object sender, RowClickEventArgs e)
        {
            if (e.RowHandle >= 0)
            {
                int pageNumber = Convert.ToInt32(gridView.GetRowCellValue(e.RowHandle, "Page"));
                string? searchText = gridView.GetRowCellValue(e.RowHandle, "Label")?.ToString();
                pdfViewer.CurrentPageNumber = pageNumber;
                if (!string.IsNullOrEmpty(searchText))
                {
                    pdfViewer.FindText(searchText);
                }
                statusLabel.Text = $"Showing page {pageNumber}";
            }
        }

        private void AddMenuBar()
        {
            menuStrip = new MenuStrip();

            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Open", null, BtnBrowse_Click),
                new ToolStripMenuItem("Save Results", null, SaveResults),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s, e) => Close())
            });

            var viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Zoom In", null, (s, e) => { pdfViewer.ZoomFactor *= 1.2f; }),
                new ToolStripMenuItem("Zoom Out", null, (s, e) => { pdfViewer.ZoomFactor *= 0.8f; }),
                new ToolStripMenuItem("Fit to Width", null, (s, e) => { pdfViewer.ZoomMode = PdfZoomMode.FitToWidth; })
            });

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void SaveResults(object? sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "CSV files (*.csv)|*.csv";
                    saveFileDialog.FilterIndex = 1;

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        using (StreamWriter sw = new StreamWriter(saveFileDialog.FileName))
                        {
                            sw.WriteLine("No,Page,QRContent,Label");

                            var gv = gridResults.MainView as DevExpress.XtraGrid.Views.Grid.GridView;
                            if (gv != null)
                            {
                                for (int i = 0; i < gv.DataRowCount; i++)
                                {
                                    var no = gv.GetRowCellValue(i, "No");
                                    var pg = gv.GetRowCellValue(i, "Page");
                                    var qr = gv.GetRowCellValue(i, "QRContent");
                                    var lbl = gv.GetRowCellValue(i, "Label");
                                    sw.WriteLine($"{no},{pg},\"{qr}\",\"{lbl}\"");
                                }
                            }
                        }

                        statusLabel.Text = "Results saved successfully";
                        MessageBox.Show("Results saved successfully!", "Success",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving results: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Error saving results";
            }
        }
    }
}
