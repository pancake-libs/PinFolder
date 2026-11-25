using System;
using System.Drawing;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 浮かぶボタンの上に数秒だけ出る、知らせの札（キーが使えない・フォルダが無い など）。
    /// </summary>
    internal sealed class ToastForm : Form
    {
        private const int ShowMilliseconds = 5000;
        private const int MaxTextWidth = 360;
        private const int Gap = 8;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly Timer _timer;

        /// <summary>札を作る（Show で出し、時間が来たら自分で閉じる）。</summary>
        /// <param name="text">知らせの文</param>
        /// <param name="isError">true＝誤り（赤い縁と文字）</param>
        /// <param name="theme">見た目のスタイル</param>
        /// <param name="anchor">この四角（浮かぶボタン）の上に出す</param>
        public ToastForm(string text, bool isError, Theme theme, Rectangle anchor)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = isError ? Theme.ErrorText : theme.Border;
            Padding = new Padding(Ui.Scale(2));

            Label label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(Ui.Scale(MaxTextWidth), 0),
                Padding = new Padding(Ui.Scale(10), Ui.Scale(8), Ui.Scale(10), Ui.Scale(8)),
                BackColor = isError ? Theme.AccentRedBack : theme.Surface,
                ForeColor = isError ? Theme.ErrorText : theme.Text,
                Font = theme.BoldFont,
                Dock = DockStyle.Fill
            };
            label.Click += (s, e) => Close();
            Controls.Add(label);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // 画面ごとの倍率に合わせる
            DpiWatcher.Attach(this);
            _timer = new Timer { Interval = ShowMilliseconds };
            _timer.Tick += (s, e) => Close();

            Load += (s, e) =>
            {
                Rectangle area = Screen.FromRectangle(anchor).WorkingArea;
                int left = Math.Max(area.Left, Math.Min(anchor.Right - Width, area.Right - Width));
                int top = anchor.Top - Height - DpiWatcher.Scale(this, Gap);
                if (top < area.Top)
                {
                    top = anchor.Bottom + DpiWatcher.Scale(this, Gap);
                }
                Location = new Point(left, top);
                _timer.Start();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            // モードレスの画面は、閉じると自分で破棄される
            base.OnFormClosed(e);
        }
    }
}
