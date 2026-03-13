using DevExpress.Pdf;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraPdfViewer;
using PDFReaderV2.Helpers;
using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;
using PDFReaderV2.Services;
using System.ComponentModel;
using System.Diagnostics;
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
        private Button btnPause;
        private DevExpress.XtraGrid.GridControl gridResults;
        private DevExpress.XtraGrid.Views.Grid.GridView gridView;
        private PdfViewer pdfViewer;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusLabel;
        private MenuStrip menuStrip;
        private DevExpress.XtraEditors.SplitContainerControl splitContainer;
        private BindingList<DisplayRow> _gridData = new();

        private ManualResetEventSlim _pauseEvent = new(true);
        private CancellationTokenSource? _cts;
        private bool _isPaused;
        private volatile string _statusText = "";
        private Stopwatch? _stopwatch;
        private System.Windows.Forms.Timer _statusTimer = null!;

        private readonly IQrScannerService _scannerService;
        private readonly IResultExporter _resultExporter;

        public Form1()
        {
            _scannerService = new QrScannerService(new LabelFinder());
            _resultExporter = new CsvResultExporter();

            InitializeComponent();
            InitializeControls();
            SetupEventHandlers();
            AddMenuBar();
        }

        private void InitializeControls()
        {
            this.Size = new Size(1200, 800);
            this.Text = "PDF Reader V2 - QR Code + Label Scanner";

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready");
            statusStrip.Items.Add(statusLabel);

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statusTimer.Tick += (_, _) =>
            {
                if (_stopwatch == null) return;
                string elapsed = TimeFormatter.Format(_stopwatch.Elapsed.TotalSeconds);
                string text = string.IsNullOrEmpty(_statusText) ? $"Elapsed: {elapsed}" : $"{_statusText} | Elapsed: {elapsed}";
                statusLabel.Text = _isPaused ? $"PAUSED | {text}" : text;
            };

            txtPdfPath = new TextBox
            {
                Location = new Point(10, 40), Width = 350,
                ReadOnly = true, Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            btnBrowse = new Button
            {
                Location = new Point(370, 39), Text = "Browse",
                Width = 80, Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            btnProcess = new Button
            {
                Location = new Point(460, 39), Text = "Process",
                Width = 80, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            btnPause = new Button
            {
                Location = new Point(550, 39), Text = "Pause",
                Width = 80, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            gridResults = new DevExpress.XtraGrid.GridControl { Dock = DockStyle.Fill };
            gridView = new DevExpress.XtraGrid.Views.Grid.GridView(gridResults);
            gridResults.MainView = gridView;

            gridView.OptionsView.ShowGroupPanel = false;
            gridView.OptionsCustomization.AllowSort = true;
            gridView.OptionsSelection.EnableAppearanceFocusedRow = true;

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
                { FieldName = "No", Caption = "No", Visible = true, Width = 50, VisibleIndex = 0 });
            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
                { FieldName = "Page", Caption = "Page", Visible = true, Width = 60 });
            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
                { FieldName = "QRContent", Caption = "QR Content", Visible = true, Width = 250 });
            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
                { FieldName = "Label", Caption = "Label (Number Below)", Visible = true, Width = 150 });

            splitContainer = new DevExpress.XtraEditors.SplitContainerControl
            {
                Location = new Point(10, 100), Size = new Size(1170, 630),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                FixedPanel = DevExpress.XtraEditors.SplitFixedPanel.None,
                SplitterPosition = 550
            };

            pdfViewer = new PdfViewer { Dock = DockStyle.Fill };
            pdfViewer.ZoomMode = PdfZoomMode.FitToWidth;

            splitContainer.Panel1.Controls.Add(pdfViewer);
            splitContainer.Panel2.Controls.Add(gridResults);

            this.Controls.AddRange([txtPdfPath, btnBrowse, btnProcess, btnPause, splitContainer, statusStrip]);
        }

        private void SetupEventHandlers()
        {
            btnBrowse.Click += BtnBrowse_Click;
            btnProcess.Click += BtnProcess_Click;
            btnPause.Click += BtnPause_Click;
            gridView.RowClick += GridView_RowClick;
        }

        private void SetProcessingMode(bool isProcessing)
        {
            btnBrowse.Enabled = !isProcessing;
            btnProcess.Enabled = !isProcessing && !string.IsNullOrEmpty(currentPdfPath);
            btnPause.Enabled = isProcessing;
            menuStrip.Enabled = !isProcessing;
        }

        private void BtnPause_Click(object? sender, EventArgs e)
        {
            if (_isPaused)
            {
                _isPaused = false;
                _pauseEvent.Set();
                btnPause.Text = "Pause";
            }
            else
            {
                _isPaused = true;
                _pauseEvent.Reset();
                btnPause.Text = "Resume";
            }
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var openFileDialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf" };
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                currentPdfPath = openFileDialog.FileName;
                txtPdfPath.Text = currentPdfPath;
                pdfViewer.LoadDocument(currentPdfPath);
                btnProcess.Enabled = true;
                statusLabel.Text = $"Loaded: {Path.GetFileName(currentPdfPath)}";
            }
        }

        private async void BtnProcess_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentPdfPath)) return;

            SetProcessingMode(true);
            _isPaused = false;
            _pauseEvent.Set();
            btnPause.Text = "Pause";
            _gridData = new BindingList<DisplayRow>();
            gridResults.DataSource = _gridData;
            _statusText = "Starting...";
            statusLabel.Text = "Starting...";

            _cts = new CancellationTokenSource();
            _stopwatch = Stopwatch.StartNew();
            _statusTimer.Start();

            try
            {
                await _scannerService.ScanAsync(
                    currentPdfPath,
                    onResultFound: result =>
                    {
                        this.Invoke(() =>
                        {
                            _gridData.Add(new DisplayRow
                            {
                                No = _gridData.Count + 1,
                                Page = result.Page,
                                QRContent = result.QRContent,
                                Label = result.Label
                            });
                            gridView.FocusedRowHandle = gridView.DataRowCount - 1;
                            gridView.MakeRowVisible(gridView.FocusedRowHandle);
                        });
                    },
                    onProgressChanged: progress =>
                    {
                        if (progress.CurrentPage > 0 && progress.TotalPages > 0)
                        {
                            int pct = (int)((double)progress.CurrentPage / progress.TotalPages * 100);
                            _statusText = $"{progress.StepText} {progress.CurrentPage} / {progress.TotalPages} ({pct}%) | Found: {progress.FoundCount} QR codes | Avg: {progress.AvgPerPageText}/page | ETA: {progress.EtaText}";
                        }
                        else
                        {
                            _statusText = progress.StepText;
                        }
                    },
                    _pauseEvent,
                    _cts.Token);

                _stopwatch.Stop();
                _statusTimer.Stop();
                statusLabel.Text = $"Scan completed. Found {_gridData.Count} results. Time: {TimeFormatter.Format(_stopwatch.Elapsed.TotalSeconds)}";
            }
            catch (OperationCanceledException)
            {
                _stopwatch.Stop();
                _statusTimer.Stop();
                statusLabel.Text = "Scan cancelled";
            }
            catch (Exception ex)
            {
                _stopwatch.Stop();
                _statusTimer.Stop();
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Processing failed";
            }
            finally
            {
                SetProcessingMode(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void GridView_RowClick(object sender, RowClickEventArgs e)
        {
            if (e.RowHandle >= 0)
            {
                int pageNumber = Convert.ToInt32(gridView.GetRowCellValue(e.RowHandle, "Page"));
                string? searchText = gridView.GetRowCellValue(e.RowHandle, "Label")?.ToString();
                pdfViewer.CurrentPageNumber = pageNumber;
                if (!string.IsNullOrEmpty(searchText))
                    pdfViewer.FindText(searchText);
                statusLabel.Text = $"Showing page {pageNumber}";
            }
        }

        private void AddMenuBar()
        {
            menuStrip = new MenuStrip();

            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.AddRange([
                new ToolStripMenuItem("Open", null, BtnBrowse_Click),
                new ToolStripMenuItem("Save Results", null, SaveResults),
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s, e) => Close())
            ]);

            var viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.AddRange([
                new ToolStripMenuItem("Zoom In", null, (s, e) => { pdfViewer.ZoomFactor *= 1.2f; }),
                new ToolStripMenuItem("Zoom Out", null, (s, e) => { pdfViewer.ZoomFactor *= 0.8f; }),
                new ToolStripMenuItem("Fit to Width", null, (s, e) => { pdfViewer.ZoomMode = PdfZoomMode.FitToWidth; })
            ]);

            menuStrip.Items.AddRange([fileMenu, viewMenu]);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);
        }

        private void SaveResults(object? sender, EventArgs e)
        {
            try
            {
                using var saveFileDialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv" };
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    _resultExporter.ExportToCsv(saveFileDialog.FileName, _gridData);
                    statusLabel.Text = "Results saved successfully";
                    MessageBox.Show("Results saved successfully!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
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
