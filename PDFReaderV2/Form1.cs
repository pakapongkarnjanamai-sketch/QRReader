using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraPdfViewer;
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
            this.Text = "PDF Reader V2";

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
                Width = 80
            });

            gridView.Columns.Add(new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Content",
                Caption = "Content",
                Visible = true,
                Width = 400
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

            splitContainer.Panel1.Controls.Add(gridResults);
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
            statusLabel.Text = "Processing...";

            try
            {
                // TODO: Implement your PDF reading logic here
                await Task.Run(() =>
                {
                    // Example: read PDF content in a different format
                });

                statusLabel.Text = "Processing completed.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Processing failed";
            }
            finally
            {
                btnProcess.Enabled = true;
            }
        }

        private void GridView_RowClick(object sender, RowClickEventArgs e)
        {
            if (e.RowHandle >= 0)
            {
                int pageNumber = Convert.ToInt32(gridView.GetRowCellValue(e.RowHandle, "Page"));
                pdfViewer.CurrentPageNumber = pageNumber;
                statusLabel.Text = $"Showing page {pageNumber}";
            }
        }

        private void AddMenuBar()
        {
            menuStrip = new MenuStrip();

            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.AddRange(new ToolStripItem[] {
                new ToolStripMenuItem("Open", null, BtnBrowse_Click),
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
    }
}
