using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 丸いボタンの上の、配置パターンの番号の □ と、出す・隠すの矢印（自分で描く）。
    /// 角を少し丸め、細い明るい縁を付ける。今のセットは赤に白い縁（2026-11-25 の Gemini との相互レビュー G-0006）。
    /// 置き場所の画面は TransparencyKey で抜くため、角はぼかさずに描く（ぼかすと抜く色が縁に混ざる）。
    /// </summary>
    internal sealed class PatternBox : Control
    {
        /// <summary>角の丸みの半径（96dpi の値）</summary>
        private const int CornerRadius = 3;

        /// <summary>矢印の三角の幅（□ の高さに対する割合）</summary>
        private const float TriangleRatio = 0.55f;

        private static readonly Color BoxBack = ColorTranslator.FromHtml("#2D3235");
        private static readonly Color BoxHover = ColorTranslator.FromHtml("#41484E");
        private static readonly Color BoxEdge = ColorTranslator.FromHtml("#C9CED6");
        private static readonly Color CurrentEdge = Color.White;

        private bool _hover;

        /// <summary>部品を作る。</summary>
        public PatternBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        /// <summary>左クリックで押された</summary>
        public event EventHandler LeftClicked;

        /// <summary>右クリックで押された</summary>
        public event EventHandler RightClicked;

        /// <summary>true＝今のセット（赤に白い縁）</summary>
        public bool Current { get; set; }

        /// <summary>true＝番号ではなく矢印を描く</summary>
        public bool IsArrow { get; set; }

        /// <summary>矢印の向き（true＝上向き＝□ を出す、false＝下向き＝隠す）</summary>
        public bool ArrowUp { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            // 押した後に部品の外で離した時は、押さなかったことにする
            if (!ClientRectangle.Contains(e.Location))
            {
                return;
            }
            EventHandler handler = e.Button == MouseButtons.Left ? LeftClicked : (e.Button == MouseButtons.Right ? RightClicked : null);
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 抜く色（親の画面の地の色）で塗ってから描く
            g.Clear(Parent == null ? BackColor : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.None;

            int edge = Math.Max(1, DpiWatcher.Scale(this, 1));
            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = DpiWatcher.Scale(this, CornerRadius);
            using (GraphicsPath outerPath = RoundRect(outer, radius))
            using (SolidBrush edgeBrush = new SolidBrush(Current ? BoxBack : BoxEdge))
            {
                g.FillPath(edgeBrush, outerPath);
            }
            if (Current)
            {
                // 今のセットは、外に濃い縁・内に白い縁（白い地の上でも形と大きさがほかの □ と同じに見える）
                outer = Rectangle.Inflate(outer, -edge, -edge);
                using (GraphicsPath whitePath = RoundRect(outer, Math.Max(0, radius - edge)))
                using (SolidBrush whiteBrush = new SolidBrush(CurrentEdge))
                {
                    g.FillPath(whiteBrush, whitePath);
                }
            }
            Rectangle inner = Rectangle.Inflate(outer, -edge, -edge);
            Color fill = Current ? (_hover ? Theme.AccentRedDark : Theme.AccentRed) : (_hover ? BoxHover : BoxBack);
            using (GraphicsPath innerPath = RoundRect(inner, Math.Max(0, radius - edge)))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            {
                g.FillPath(fillBrush, innerPath);
            }

            if (IsArrow)
            {
                DrawTriangle(g, inner);
                return;
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        /// <summary>出す（上向き）・隠す（下向き）の三角を描く。</summary>
        private void DrawTriangle(Graphics g, Rectangle area)
        {
            float size = area.Height * TriangleRatio;
            float cx = area.Left + area.Width / 2f;
            float cy = area.Top + area.Height / 2f;
            float half = size / 2f;
            float tall = size * 0.5f;
            PointF[] points = ArrowUp
                ? new[] { new PointF(cx - half, cy + tall / 2f), new PointF(cx + half, cy + tall / 2f), new PointF(cx, cy - tall / 2f) }
                : new[] { new PointF(cx - half, cy - tall / 2f), new PointF(cx + half, cy - tall / 2f), new PointF(cx, cy + tall / 2f) };
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(ForeColor))
            {
                g.FillPolygon(brush, points);
            }
        }

        /// <summary>角を丸めた四角の形を作る（半径 0 はふつうの四角）。</summary>
        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || rect.Width <= radius * 2 || rect.Height <= radius * 2)
            {
                path.AddRectangle(rect);
                return path;
            }
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
