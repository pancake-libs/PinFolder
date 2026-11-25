using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 浮かぶボタンの上に出す、配置パターンの切り替え（区画のセットの番号の □）。押すとそのセットで配置を適用する。
    /// □ はセットの数だけ（最大 MaxPatterns）、横 4 つずつ並べる。下の小さな矢印を押すと、□ の表示・非表示を切り替える
    /// 矢印は、丸いボタン・□ にマウスを当てている時だけ出す
    /// （隠すと矢印だけになり、画面がごちゃごちゃしない）。
    /// </summary>
    internal sealed class QuickSetBar : Form
    {
        /// <summary>出せるパターンの最大の数</summary>
        public const int MaxPatterns = 12;

        /// <summary>横に並べる □ の数（1234 で 1 行目、5678 で 2 行目。2026-11-25 の利用者の指示）</summary>
        private const int Columns = 4;

        /// <summary>マウスが当たっているかを見る間隔</summary>
        private const int HoverPollMilliseconds = 150;

        /// <summary>マウスが離れてから矢印を隠すまでの時間</summary>
        private const int HoverHideMilliseconds = 600;

        /// <summary>マウスが当たっているとみなす、□・丸いボタンの周りの広さ（96dpi の値）</summary>
        private const int HoverMargin = 8;

        /// <summary>□ 1 つの大きさ（96dpi の値）</summary>
        private const int BoxSize = 20;

        /// <summary>□ の間と、浮かぶボタンとの間（96dpi の値）</summary>
        private const int Gap = 4;

        /// <summary>矢印の大きさ（96dpi の値）</summary>
        private const int ArrowWidth = 32;
        private const int ArrowHeight = 16;

        /// <summary>□ の文字の大きさ（ポイント）</summary>
        private const float BoxFontSize = 8f;

        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly List<PatternBox> _boxes = new List<PatternBox>();
        private readonly PatternBox _arrow;
        private readonly Font _boxFont;

        /// <summary>□ の説明（この画面は入力の向きを持たないため、ShowAlways でいつも出す）</summary>
        private readonly ToolTip _tip = new ToolTip { ShowAlways = true };

        private readonly Timer _hoverTimer;
        private bool _expanded;
        private Rectangle _anchor;
        private DateTime _lastInside = DateTime.MinValue;

        /// <summary>□ が押された（引数はパターンの番号。0 から）</summary>
        public event EventHandler<int> PatternClicked;

        /// <summary>□ が右クリックされた（引数はパターンの番号。0 から。そのセットの窓をすべて閉じる）</summary>
        public event EventHandler<int> PatternRightClicked;

        /// <summary>矢印で表示・非表示を切り替えた（引数は true＝表示。設定に覚えるため）</summary>
        public event EventHandler<bool> ExpandedChanged;

        /// <summary>画面を作る。</summary>
        /// <param name="expanded">true＝□ を出した状態で始める</param>
        public QuickSetBar(bool expanded)
        {
            _expanded = expanded;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            _boxFont = new Font(Font.FontFamily, BoxFontSize, FontStyle.Bold);

            // 矢印は文字を描かない（三角を描く）ため、文字は付けない
            _arrow = new PatternBox { IsArrow = true };
            _arrow.LeftClicked += (s, e) => Toggle();
            _arrow.Visible = false;
            Controls.Add(_arrow);

            // 矢印はマウスを当てた時だけ出す（ほかの時は何も出さず、画面をすっきりさせる）
            _hoverTimer = new Timer { Interval = HoverPollMilliseconds };
            _hoverTimer.Tick += (s, e) => UpdateArrowVisibility();
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                {
                    _hoverTimer.Start();
                }
                else
                {
                    _hoverTimer.Stop();
                }
            };

            // 画面ごとの倍率に合わせる
            DpiWatcher.Attach(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // Alt+Tab に出さず、押しても入力の向きを奪わない
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>セットの数だけ □ を作り直し、今のセット（最後に選んだセット）を赤にする。</summary>
        public void ShowSets(AppConfig config)
        {
            SuspendLayout();
            foreach (PatternBox old in _boxes)
            {
                Controls.Remove(old);
                _tip.SetToolTip(old, null);
                old.Dispose();
            }
            _boxes.Clear();
            int count = Math.Min(config.LayoutSets.Count, MaxPatterns);
            for (int i = 0; i < count; i++)
            {
                int pattern = i;
                LayoutSet set = config.LayoutSets[i];
                PatternBox button = new PatternBox
                {
                    Text = (i + 1).ToString(),
                    Current = set.Name == config.LastLayoutSet,
                    Font = _boxFont
                };
                button.LeftClicked += (s, e) =>
                {
                    // 押された □ は、配置の後に作り直す（Dispose する）ため、クリックの処理を抜けてから行う
                    BeginInvoke(new Action(() =>
                    {
                        if (PatternClicked != null)
                        {
                            PatternClicked(this, pattern);
                        }
                    }));
                };
                button.RightClicked += (s, e) =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (PatternRightClicked != null)
                        {
                            PatternRightClicked(this, pattern);
                        }
                    }));
                };
                _tip.SetToolTip(button, (i + 1) + "：" + set.Name);
                _boxes.Add(button);
                Controls.Add(button);
            }
            ResumeLayout(false);
            // 倍率の違う画面にある時は、新しい □ の文字もその倍率に合わせる
            DpiWatcher.ReapplyFonts(this);
            LayoutBoxes();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            // 倍率の違う画面へ移って文字が作り直された時は、□ と矢印の大きさ・位置も合わせ直す
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(LayoutBoxes));
            }
        }

        /// <summary>浮かぶボタンの真上に置く（覚えておき、□ の数や表示が変わった時も合わせ直す）。</summary>
        public void PlaceAbove(Rectangle button)
        {
            _anchor = button;
            int gap = DpiWatcher.Scale(this, Gap);
            Rectangle area = Screen.FromRectangle(button).WorkingArea;
            int left = Math.Max(area.Left, Math.Min(button.Left + (button.Width - Width) / 2, area.Right - Width));
            int top = button.Top - Height - gap;
            if (top < area.Top)
            {
                // 画面の上の端に近い時は、ボタンの下に出す
                top = button.Bottom + gap;
            }
            Location = new Point(left, top);
            // 矢印は丸いボタンの真上に置く（画面の端で □ の帯が左へ寄った時も、矢印だけは丸いボタンの中心にそろえる）
            int arrowLeft = button.Left + button.Width / 2 - left - _arrow.Width / 2;
            _arrow.Left = Math.Max(0, Math.Min(arrowLeft, Width - _arrow.Width));
        }

        /// <summary>丸いボタン・□ の周りにマウスがあれば矢印を出し、離れて少したったら隠す。</summary>
        private void UpdateArrowVisibility()
        {
            Rectangle zone = _anchor.IsEmpty ? Bounds : Rectangle.Union(Bounds, _anchor);
            int margin = DpiWatcher.Scale(this, HoverMargin);
            zone.Inflate(margin, margin);
            DateTime now = DateTime.Now;
            if (zone.Contains(Cursor.Position))
            {
                _lastInside = now;
            }
            bool show = (now - _lastInside).TotalMilliseconds < HoverHideMilliseconds;
            if (_arrow.Visible != show)
            {
                _arrow.Visible = show;
            }
        }

        private void Toggle()
        {
            _expanded = !_expanded;
            LayoutBoxes();
            if (ExpandedChanged != null)
            {
                ExpandedChanged(this, _expanded);
            }
        }

        /// <summary>□ を横 4 つずつ並べ、その下に矢印を置いて、画面の大きさを合わせる。</summary>
        private void LayoutBoxes()
        {
            int box = DpiWatcher.Scale(this, BoxSize);
            int gap = DpiWatcher.Scale(this, Gap);
            int arrowWidth = DpiWatcher.Scale(this, ArrowWidth);
            int arrowHeight = DpiWatcher.Scale(this, ArrowHeight);
            bool showBoxes = _expanded && _boxes.Count > 0;
            int columns = Math.Min(Columns, Math.Max(1, _boxes.Count));
            int rows = (_boxes.Count + Columns - 1) / Columns;
            int boxesWidth = columns * box + (columns - 1) * gap;
            int boxesHeight = showBoxes ? rows * box + (rows - 1) * gap + gap : 0;
            int width = Math.Max(arrowWidth, showBoxes ? boxesWidth : 0);
            Size = new Size(width, boxesHeight + arrowHeight);

            int offsetX = (width - boxesWidth) / 2;
            for (int i = 0; i < _boxes.Count; i++)
            {
                PatternBox button = _boxes[i];
                button.Visible = showBoxes;
                button.Bounds = new Rectangle(offsetX + (i % Columns) * (box + gap), (i / Columns) * (box + gap), box, box);
            }
            _arrow.Bounds = new Rectangle((width - arrowWidth) / 2, boxesHeight, arrowWidth, arrowHeight);
            _arrow.ArrowUp = !_expanded;
            _arrow.Invalidate();
            _tip.SetToolTip(_arrow, _expanded ? Lang.T("配置パターンを隠す") : Lang.T("配置パターンを出す"));
            if (!_anchor.IsEmpty)
            {
                PlaceAbove(_anchor);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hoverTimer.Stop();
                _hoverTimer.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                _tip.Dispose();
                _boxFont.Dispose();
            }
        }
    }
}
