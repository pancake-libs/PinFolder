using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 区画パネルの区画 1 つ（自分で描く）。
    /// 入っている区画は白地に左のグループの色の帯、左上に小さく番号、真ん中にグループの名前（小さく）と登録の名前（大きな太字）。
    /// 空いた区画は、地の色に破線の枠と「空き」（2026-11-25 の Gemini との相互レビュー G-0006 で、番号・グループ・名前が同じ大きさで読みにくかったため）。
    /// </summary>
    internal sealed class ZoneTile : Control
    {
        /// <summary>左の帯の幅（96dpi の値）</summary>
        private const int StripWidth = 4;

        /// <summary>番号を置く、左上からの余白（96dpi の値）</summary>
        private const int NumberMargin = 8;

        /// <summary>登録の名前の文字の大きさ（ふつうの文字に対する倍率）</summary>
        private const float NameFontRatio = 1.25f;

        /// <summary>番号・グループの名前の文字の大きさ（ふつうの文字に対する倍率）</summary>
        private const float SmallFontRatio = 0.9f;

        /// <summary>グループの名前と登録の名前の間（96dpi の値）</summary>
        private const int LineGap = 2;

        private const TextFormatFlags CenterFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private readonly Theme _theme;

        /// <summary>区画を作る。</summary>
        /// <param name="theme">見た目のスタイル（地・枠・文字の色）</param>
        public ZoneTile(Theme theme)
        {
            _theme = theme;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>区画の番号（1 から）</summary>
        public string Number { get; set; }

        /// <summary>グループの名前（空いた区画は空の文字）</summary>
        public string GroupName { get; set; }

        /// <summary>登録の名前（空いた区画は「空き」）</summary>
        public string EntryName { get; set; }

        /// <summary>true＝登録が入っている、false＝空いている</summary>
        public bool Filled { get; set; }

        /// <summary>左の帯の色（グループごとの色）</summary>
        public Color GroupColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle rect = ClientRectangle;
            using (SolidBrush back = new SolidBrush(Filled ? _theme.Surface : _theme.WindowBack))
            {
                g.FillRectangle(back, rect);
            }
            Rectangle frame = new Rectangle(rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
            using (Pen border = new Pen(_theme.ControlBorder))
            {
                if (!Filled)
                {
                    border.DashStyle = DashStyle.Dash;
                }
                g.DrawRectangle(border, frame);
            }
            int strip = 0;
            if (Filled)
            {
                strip = DpiWatcher.Scale(this, StripWidth);
                using (SolidBrush brush = new SolidBrush(GroupColor))
                {
                    g.FillRectangle(brush, rect.Left, rect.Top, strip, rect.Height);
                }
            }

            int margin = DpiWatcher.Scale(this, NumberMargin);
            using (Font small = new Font(Font.FontFamily, Font.Size * SmallFontRatio, FontStyle.Regular, Font.Unit))
            using (Font name = new Font(Font.FontFamily, Font.Size * (Filled ? NameFontRatio : 1f), Filled ? FontStyle.Bold : FontStyle.Regular, Font.Unit))
            {
                // 左上：番号
                TextRenderer.DrawText(g, Number ?? string.Empty, small, new Point(rect.Left + strip + margin, rect.Top + margin), _theme.SubText, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

                // 真ん中：グループの名前（小さく）と登録の名前（大きく）を、まとめて上下の真ん中に置く
                int textLeft = rect.Left + strip + margin;
                int textWidth = Math.Max(1, rect.Width - strip - margin * 2);
                int gap = DpiWatcher.Scale(this, LineGap);
                int groupHeight = string.IsNullOrEmpty(GroupName) ? 0 : small.Height + gap;
                int top = rect.Top + (rect.Height - groupHeight - name.Height) / 2;
                if (groupHeight > 0)
                {
                    TextRenderer.DrawText(g, GroupName, small, new Rectangle(textLeft, top, textWidth, small.Height), _theme.SubText, CenterFlags);
                }
                TextRenderer.DrawText(g, EntryName ?? string.Empty, name, new Rectangle(textLeft, top + groupHeight, textWidth, name.Height),
                    Filled ? _theme.Text : _theme.SubText, CenterFlags);
            }
        }
    }
}
