using DevExpress.Pdf;
using DevExpress.XtraPdfViewer;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Common;
using Button = System.Windows.Forms.Button;
using Point = System.Drawing.Point;
using RadioButton = System.Windows.Forms.RadioButton;
using Size = System.Drawing.Size;
using DevExpress.XtraEditors;

namespace QRReader

{
    public partial class Form1 : Form
    {
        private string currentPdfPath;
        private TextBox txtPdfPath;
        private Button btnBrowse;
        private Button btnScan;
        private DevExpress.XtraGrid.GridControl gridResults;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private TextBox txtSearch;
        private Button btnSearch;
        private Button btnClearHighlight;
        private PdfViewer pdfViewer;
        private RadioButton rbText;
        private RadioButton rbQRCode;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private MenuStrip menuStrip;
        private float currentZoom = 1.0f;
        private HashSet<string> scannedQRCodes = new HashSet<string>();
        private Dictionary<int, int> qrCountPerPage = new Dictionary<int, int>();
        private DevExpress.XtraEditors.SplitContainerControl splitContainer;

        public Form1()
        {
            InitializeComponent();
            InitializeControls();
            SetupEventHandlers();
            AddMenuBar();
            SetupPdfViewer();
        }

        private void SetupPdfViewer()
        {
            if (pdfViewer != null)
            {
                pdfViewer.ZoomMode = DevExpress.XtraPdfViewer.PdfZoomMode.FitToWidth;
            }
        }

        private void InitializeControls()
        {
            this.Size = new Size(1200, 800);
            this.Text = "PDFScout - PDF Content Scanner";

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

            btnSearch = new Button
            {
                Location = new Point(460, 39),
                Text = "Search",
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



            // ตั้งค่า GridView
            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsCustomization.AllowSort = true;
            gridView.OptionsSelection.EnableAppearanceFocusedRow = true;
            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "No",
                Caption = "No",
                Visible = true,
                Width = 50,
                VisibleIndex = 0, // ให้แสดงเป็นคอลัมน์แรก
                //TextAlignment = DevExpress.Utils.HorzAlignment.Center
            });
            // สร้างคอลัมน์
            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Page",
                Caption = "Page",
                Visible = true,
                Width = 80
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Count",
                Caption = "Count in Page",
                Visible = true,
                Width = 100,
                //TextAlignment = DevExpress.Utils.HorzAlignment.Center
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Content",
                Caption = "Content",
                Visible = true,
                Width = 300
            });
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
            splitContainer.Panel1.Controls.Add(gridResults);
            splitContainer.Panel2.Controls.Add(pdfViewer);
            // Add all controls
            this.Controls.AddRange(new Control[] {
    txtPdfPath, btnBrowse, rbText, rbQRCode,
    txtSearch, btnSearch, btnClearHighlight,
    splitContainer,
    statusStrip
});
        }


        private void SetupEventHandlers()
        {
            btnBrowse.Click += BtnBrowse_Click;
            btnSearch.Click += BtnSearch_Click;
            gridView.RowClick += GridView_RowClick;


        }
        private void GridView_RowClick(object sender, RowClickEventArgs e)
        {
            if (e.RowHandle >= 0)
            {
                int pageNumber = Convert.ToInt32(gridView.GetRowCellValue(e.RowHandle, "Page"));
                string searchText = gridView.GetRowCellValue(e.RowHandle, "Content").ToString();

                if (pdfViewer != null)
                {
                    pdfViewer.CurrentPageNumber = pageNumber;
                    pdfViewer.FindText(searchText);
                    statusLabel.Text = $"Showing page {pageNumber}";
                }
            }
        }
        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentPdfPath))
            {
                MessageBox.Show("Please select a PDF file first.");
                return;
            }

            btnSearch.Enabled = false;
            ClearScanResults();
            statusLabel.Text = "Scanning...";

            try
            {
                using (var waitCursor = new WaitCursor())
                {
                    await ScanQRCode();
                }
                statusLabel.Text = $"Scan completed. Found {((List<object>)gridResults.DataSource)?.Count ?? 0} results.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during scan: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Scan failed";
            }
            finally
            {
                btnSearch.Enabled = true;
            }
        }

        private void ClearScanResults()
        {
            scannedQRCodes.Clear();
            qrCountPerPage.Clear();
            gridResults.DataSource = null;
        }

        private async Task ScanQRCode()
        {
            await Task.Run(() =>
            {
                try
                {
                    qrCountPerPage.Clear();
                    var results = new List<object>();
                    int rowNo = 1;
                    using (PdfDocumentProcessor processor = new PdfDocumentProcessor())
                    {
                        processor.LoadDocument(currentPdfPath);

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

                        for (int page = 1; page <= processor.Document.Pages.Count; page++)
                        {
                            if (!qrCountPerPage.ContainsKey(page))
                            {
                                qrCountPerPage[page] = 0;
                            }

                            using (var originalImage = processor.CreateBitmap(page, 2400))
                            {
                                //string outputPath = Path.Combine(Path.GetDirectoryName(currentPdfPath),$"page_{page}_original.png");
                                //originalImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);

                                // กำหนดค่า margin
                                int marginTop = 80;    // ระยะขอบบน
                                int marginRight = 120;  // ระยะขอบขวา
                                int marginBottom = 340; // ระยะขอบล่าง
                                int marginLeft = 120;   // ระยะขอบซ้าย

                                // คำนวณขนาดพื้นที่ที่จะสแกนหลังจากหักค่า margin
                                int scanWidth = originalImage.Width - marginLeft - marginRight;
                                int scanHeight = originalImage.Height - marginTop - marginBottom;

                                int gridRows = 6;
                                int gridCols = 5;
                                int cellWidth = scanWidth / gridCols;
                                int cellHeight = scanHeight / gridRows;

                                for (int row = 0; row < gridRows; row++)
                                {
                                    for (int col = 0; col < gridCols; col++)
                                    {
                                        // สร้าง Rectangle โดยรวม margin
                                        Rectangle cropRect = new Rectangle(
                                            marginLeft + (col * cellWidth),
                                            marginTop + (row * cellHeight),
                                            cellWidth,
                                            cellHeight
                                        );
                                        // ตรวจสอบว่า cropRect ไม่เกินขอบเขตของรูป
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
                                            //string croppedOutputPath = Path.Combine(Path.GetDirectoryName(currentPdfPath),$"page_{page}_row_{row}_col_{col}_cropped.png");
                                            //croppedImage.Save(croppedOutputPath, System.Drawing.Imaging.ImageFormat.Png);

                                            var result = reader.Decode(croppedImage);
                                            if (result != null)
                                            {
                                                string qrText = result.Text;
                                                if (!scannedQRCodes.Contains(qrText))
                                                {
                                                    scannedQRCodes.Add(qrText);
                                                    qrCountPerPage[page]++;



                                                    // ในส่วนที่เพิ่มข้อมูลใหม่
                                                    this.Invoke((MethodInvoker)delegate
                                                    {
                                                        var row = new
                                                        {
                                                            No = rowNo++, // เพิ่มเลขรันนิ่ง
                                                            Page = page,
                                                            Count = qrCountPerPage[page],
                                                            Content = qrText
                                                        };
                                                        results.Add(row);
                                                        gridResults.DataSource = null;
                                                        gridResults.DataSource = results;
                                                    });

                                                }
                                            }

                                            // ทดลองหมุนรูปและสแกนอีกครั้ง
                                            for (int angle = 90; angle < 360; angle += 90)
                                            {
                                                using (var rotated = RotateImage(croppedImage, angle))
                                                {
                                                    result = reader.Decode(rotated);
                                                    if (result != null)
                                                    {
                                                        string qrText = result.Text;
                                                        if (!scannedQRCodes.Contains(qrText))
                                                        {
                                                            scannedQRCodes.Add(qrText);
                                                            qrCountPerPage[page]++;
                                                            this.Invoke((MethodInvoker)delegate
                                                            {
                                                                var row = new
                                                                {
                                                                    No = rowNo++, // ใช้ค่า rowNo และเพิ่มค่า
                                                                    Page = page,
                                                                    Count = qrCountPerPage[page],
                                                                    Content = qrText
                                                                };
                                                                results.Add(row);
                                                                gridResults.DataSource = null;
                                                                gridResults.DataSource = results;
                                                            });
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error scanning QR Code: {ex.Message}");
                }
            });
        }




        // เมธอดสำหรับหมุนรูป
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



        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "PDF files (*.pdf)|*.pdf";
                openFileDialog.FilterIndex = 1;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    currentPdfPath = openFileDialog.FileName;
                    txtPdfPath.Text = currentPdfPath;
                    LoadPdf(currentPdfPath);
                    statusLabel.Text = "PDF loaded successfully";
                }
            }
        }

        private void LoadPdf(string path)
        {
            try
            {
                pdfViewer.LoadDocument(path);
                statusLabel.Text = $"Loaded: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading PDF: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Error loading PDF";
            }
        }





        // Helper class for wait cursor
        private class WaitCursor : IDisposable
        {
            private Cursor previousCursor;

            public WaitCursor()
            {
                previousCursor = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
            }

            public void Dispose()
            {
                Cursor.Current = previousCursor;
            }
        }

        // Menu related methods
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
                new ToolStripMenuItem("Zoom In", null, (s, e) => ZoomPdf(1.2f)),
                new ToolStripMenuItem("Zoom Out", null, (s, e) => ZoomPdf(0.8f)),
                new ToolStripMenuItem("Fit to Width", null, FitToWidth)
            });



            menuStrip.Items.AddRange(new ToolStripItem[] {
                fileMenu,  viewMenu,

            });

            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void SaveResults(object sender, EventArgs e)
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
                            // เขียนหัวคอลัมน์
                            sw.WriteLine("No,Page,Count,Content");

                            // ดึงข้อมูลจาก GridView
                            var gridView = gridResults.MainView as DevExpress.XtraGrid.Views.Grid.GridView;
                            if (gridView != null)
                            {
                                int rowNo = 1; // เริ่มต้นเลขรันนิ่งที่ 1
                                for (int i = 0; i < gridView.DataRowCount; i++)
                                {
                                    var page = gridView.GetRowCellValue(i, "Page");
                                    var count = gridView.GetRowCellValue(i, "Count");
                                    var content = gridView.GetRowCellValue(i, "Content");

                                    // เขียนข้อมูลแต่ละแถว โดยใส่เครื่องหมาย " ครอบ content เพื่อป้องกันปัญหากรณีมี comma
                                    sw.WriteLine($"{rowNo},{page},{count},\"{content}\"");
                                    rowNo++; // เพิ่มเลขรันนิ่ง
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






        private void ZoomPdf(float factor)
        {
            if (pdfViewer != null)
            {
                currentZoom *= factor;
                pdfViewer.ZoomFactor = currentZoom;
                statusLabel.Text = $"Zoom: {currentZoom:P0}";
            }
        }

        private void FitToWidth(object sender, EventArgs e)
        {
            if (pdfViewer != null)
            {
                pdfViewer.ZoomMode = DevExpress.XtraPdfViewer.PdfZoomMode.FitToWidth;
                currentZoom = pdfViewer.ZoomFactor;
                statusLabel.Text = "Fit to width";
            }
        }


    }
}

