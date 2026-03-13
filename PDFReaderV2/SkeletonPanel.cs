using System.Drawing.Drawing2D;

namespace PDFReaderV2
{
    /// <summary>
    /// A skeleton loading indicator panel that displays animated placeholder bars
    /// to indicate content is being loaded.
    /// </summary>
    public class SkeletonPanel : Panel
    {
        private System.Windows.Forms.Timer _animationTimer;
        private float _shimmerOffset;
        private const int RowCount = 8;
        private const int RowHeight = 36;
        private const int RowSpacing = 8;
        private const int BarRadius = 6;

        private int _currentPage;
        private int _totalPages;
        private int _foundCount;

        /// <summary>
        /// Updates the progress information displayed on the skeleton panel.
        /// </summary>
        public void UpdateProgress(int currentPage, int totalPages, int foundCount)
        {
            _currentPage = currentPage;
            _totalPages = totalPages;
            _foundCount = foundCount;
            this.Invalidate();
        }

        /// <summary>
        /// Resets progress counters to zero.
        /// </summary>
        public void ResetProgress()
        {
            _currentPage = 0;
            _totalPages = 0;
            _foundCount = 0;
        }

        public SkeletonPanel()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.White;

            _animationTimer = new System.Windows.Forms.Timer();
            _animationTimer.Interval = 30;
            _animationTimer.Tick += (s, e) =>
            {
                _shimmerOffset += 8f;
                if (_shimmerOffset > this.Width + 300)
                    _shimmerOffset = -300;
                this.Invalidate();
            };
        }

        /// <summary>
        /// Starts the shimmer animation.
        /// </summary>
        public void StartAnimation()
        {
            _shimmerOffset = -300;
            _animationTimer.Start();
        }

        /// <summary>
        /// Stops the shimmer animation.
        /// </summary>
        public void StopAnimation()
        {
            _animationTimer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int y = 12;

            // Draw header skeleton row
            DrawSkeletonBar(g, 12, y, (int)(this.Width * 0.08) - 16, RowHeight);
            DrawSkeletonBar(g, (int)(this.Width * 0.08) + 4, y, (int)(this.Width * 0.10) - 8, RowHeight);
            DrawSkeletonBar(g, (int)(this.Width * 0.18) + 4, y, (int)(this.Width * 0.50) - 8, RowHeight);
            DrawSkeletonBar(g, (int)(this.Width * 0.68) + 4, y, (int)(this.Width * 0.30) - 16, RowHeight);

            y += RowHeight + RowSpacing + 4;

            // Draw data skeleton rows
            for (int i = 0; i < RowCount; i++)
            {
                float widthFactor = 0.6f + (float)(Math.Sin(i * 1.3) * 0.3 + 0.1);
                DrawSkeletonRow(g, y, widthFactor);
                y += RowHeight + RowSpacing;
            }

            // Draw progress information at the bottom
            if (_totalPages > 0)
            {
                int percentage = (int)((double)_currentPage / _totalPages * 100);
                int progressAreaY = this.Height - 100;

                // Progress bar background
                int barX = 20;
                int barWidth = this.Width - 40;
                int barHeight = 8;
                int barY = progressAreaY;
                var barBgRect = new Rectangle(barX, barY, barWidth, barHeight);
                using (var bgBrush = new SolidBrush(Color.FromArgb(220, 220, 220)))
                {
                    FillRoundedRectangle(g, bgBrush, barBgRect, 4);
                }

                // Progress bar fill
                int fillWidth = (int)(barWidth * (percentage / 100.0));
                if (fillWidth > 0)
                {
                    var fillRect = new Rectangle(barX, barY, fillWidth, barHeight);
                    using (var fillBrush = new SolidBrush(Color.FromArgb(0, 122, 204)))
                    {
                        FillRoundedRectangle(g, fillBrush, fillRect, 4);
                    }
                }

                // Progress text
                string progressText = $"{percentage}%";
                string pageText = $"Page {_currentPage} / {_totalPages}";
                string foundText = $"Found: {_foundCount} QR codes";

                using (var percentFont = new Font("Segoe UI", 20f, FontStyle.Bold))
                using (var detailFont = new Font("Segoe UI", 10f, FontStyle.Regular))
                using (var textBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                using (var subTextBrush = new SolidBrush(Color.FromArgb(120, 120, 120)))
                using (var accentBrush = new SolidBrush(Color.FromArgb(0, 122, 204)))
                {
                    var percentSize = g.MeasureString(progressText, percentFont);
                    var pageSize = g.MeasureString(pageText, detailFont);
                    var foundSize = g.MeasureString(foundText, detailFont);

                    int textY = barY + barHeight + 12;

                    // Percentage (centered)
                    g.DrawString(progressText, percentFont, accentBrush,
                        (this.Width - percentSize.Width) / 2, textY);

                    textY += (int)percentSize.Height + 4;

                    // Page info (centered)
                    g.DrawString(pageText, detailFont, subTextBrush,
                        (this.Width - pageSize.Width) / 2, textY);

                    textY += (int)pageSize.Height + 2;

                    // Found count (centered)
                    g.DrawString(foundText, detailFont, textBrush,
                        (this.Width - foundSize.Width) / 2, textY);
                }
            }
        }

        private void DrawSkeletonRow(Graphics g, int y, float widthFactor)
        {
            // Column 1: No (narrow)
            DrawSkeletonBar(g, 12, y, (int)(this.Width * 0.08) - 16, RowHeight);

            // Column 2: Page (narrow)
            DrawSkeletonBar(g, (int)(this.Width * 0.08) + 4, y, (int)(this.Width * 0.10) - 8, RowHeight);

            // Column 3: QR Content (wide, variable width)
            int qrWidth = (int)((this.Width * 0.50 - 8) * widthFactor);
            DrawSkeletonBar(g, (int)(this.Width * 0.18) + 4, y, qrWidth, RowHeight);

            // Column 4: Label (medium, variable width)
            int labelWidth = (int)((this.Width * 0.30 - 16) * (widthFactor * 0.7f + 0.3f));
            DrawSkeletonBar(g, (int)(this.Width * 0.68) + 4, y, labelWidth, RowHeight);
        }

        private void DrawSkeletonBar(Graphics g, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0) return;

            var barRect = new Rectangle(x, y, width, height);

            // Base color
            using (var baseBrush = new SolidBrush(Color.FromArgb(230, 230, 230)))
            {
                FillRoundedRectangle(g, baseBrush, barRect, BarRadius);
            }

            // Shimmer effect
            int shimmerWidth = 200;
            int shimmerX = (int)_shimmerOffset;
            var shimmerRect = new Rectangle(shimmerX, y, shimmerWidth, height);

            // Clip to the bar area
            var oldClip = g.Clip;
            using (var clipPath = CreateRoundedRectPath(barRect, BarRadius))
            {
                g.SetClip(clipPath);

                using (var shimmerBrush = new LinearGradientBrush(
                    shimmerRect,
                    Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(80, 255, 255, 255),
                    LinearGradientMode.Horizontal))
                {
                    try
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new[] {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(80, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        shimmerBrush.InterpolationColors = blend;
                        g.FillRectangle(shimmerBrush, shimmerRect);
                    }
                    catch { }
                }

                g.Clip = oldClip;
            }
        }

        private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (var path = CreateRoundedRectPath(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer.Stop();
                _animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
