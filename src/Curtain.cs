using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 配置を適用する間、並べる先の画面にかける「幕」（2026-11-25 の利用者の指示）。
    /// 始める直前の画面を撮った絵を、いちばん手前に画面いっぱいに出し、真ん中に小さく磁石の絵を出す。
    /// 幕の後ろでフォルダを開いて並べ、終わったら外す（1 つずつ窓が出る過程を見せない）。
    /// 外し忘れても画面を操作できなくならないよう、最長 MaxShowMilliseconds で自分で閉じる。
    /// </summary>
    internal sealed class Curtain : Form
    {
        /// <summary>
        /// 幕を出しておく最長の時間。並べる処理は、新しい窓の待ち（最長 5 秒）と置き直し（150 ミリ秒 × 2 回）で
        /// 約 5.3 秒に収まるため、それより 2 秒以上長くする
        /// </summary>
        private const int MaxShowMilliseconds = 8000;

        /// <summary>真ん中の磁石の絵の大きさ（96dpi の値）</summary>
        private const int IconSize = 64;

        /// <summary>真ん中の絵の下に敷く丸の、絵に対する大きさ</summary>
        private const float PlateRatio = 1.5f;

        /// <summary>倍率 100% の時の dpi</summary>
        private const int BaseDpi = 96;

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        private const string LogTarget = "幕";

        private static readonly Color PlateColor = Color.FromArgb(220, 38, 42, 46);

        private readonly Bitmap _snapshot;
        private readonly Image _icon;
        private readonly Timer _safety;
        private readonly Stopwatch _watch = new Stopwatch();

        private Curtain(Rectangle area, Bitmap snapshot)
        {
            _snapshot = snapshot;
            _icon = AppPaths.LoadImage(AppPaths.FloatImageResourceName);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = area;
            DoubleBuffered = true;
            Cursor = Cursors.WaitCursor;

            _safety = new Timer { Interval = MaxShowMilliseconds };
            _safety.Tick += (s, e) =>
            {
                AppLog.Warn("配置を適用", LogTarget, "幕が " + MaxShowMilliseconds / 1000 + " 秒外れなかったため、自分で閉じた", "もう一度［配置を適用］を押してください");
                Close();
            };
        }

        /// <summary>
        /// 幕をかける（画面を撮って出す）。出せなかった時は null（幕なしで並べる）。
        /// </summary>
        /// <param name="area">並べる先の画面の作業領域</param>
        public static Curtain Cover(Rectangle area)
        {
            Bitmap snapshot = null;
            Curtain curtain = null;
            try
            {
                snapshot = new Bitmap(area.Width, area.Height);
                CaptureScreen(area, snapshot);
                curtain = new Curtain(area, snapshot);
                snapshot = null; // ここからは幕が持ち、幕の Dispose で解放する
                // 出す途中で止まっても必ず外れるよう、先に保険のタイマーを動かす
                curtain._safety.Start();
                curtain._watch.Start();
                curtain.Show();
                // 倍率の違う画面では、出した時に Windows が大きさを変えることがあるため、撮った絵と同じ四角に戻す
                if (curtain.Bounds != area)
                {
                    curtain.Bounds = area;
                }
                curtain.Refresh();
                AppLog.Info("配置を適用", LogTarget, "かけた（位置 " + area.Left + "," + area.Top + "・大きさ " + area.Width + "×" + area.Height + "）");
                return curtain;
            }
            catch (Exception ex)
            {
                if (curtain != null)
                {
                    curtain.Dispose();
                }
                if (snapshot != null)
                {
                    snapshot.Dispose();
                }
                AppLog.Error("配置を適用", LogTarget, ex, "幕なしで並べました（窓が 1 つずつ出るのが見えます）。続く時はこのログを開発の担当へ渡してください");
                return null;
            }
        }

        /// <summary>
        /// 画面の四角を絵に写す。CAPTUREBLT を付け、丸いボタン・□（重ねて描く窓）も絵に入れる
        /// （入れないと、幕の間だけ消えて見える。.NET の CopyFromScreen はこの組み合わせを受け付けない）。
        /// </summary>
        private static void CaptureScreen(Rectangle area, Bitmap snapshot)
        {
            using (Graphics g = Graphics.FromImage(snapshot))
            {
                IntPtr screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero)
                {
                    throw new InvalidOperationException("画面の DC を取れなかった");
                }
                IntPtr bitmapDc = g.GetHdc();
                try
                {
                    if (!BitBlt(bitmapDc, 0, 0, area.Width, area.Height, screenDc, area.Left, area.Top, SRCCOPY | CAPTUREBLT))
                    {
                        throw new System.ComponentModel.Win32Exception();
                    }
                }
                finally
                {
                    g.ReleaseHdc(bitmapDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
        }

        /// <summary>幕を外す（null でもよい）。外せなかった時もログに残し、例外は外へ出さない。</summary>
        public static void Remove(Curtain curtain)
        {
            if (curtain == null || curtain.IsDisposed)
            {
                return;
            }
            long elapsed = curtain._watch.ElapsedMilliseconds;
            try
            {
                curtain.Close();
                AppLog.Info("配置を適用", LogTarget, "外した（" + elapsed + " ミリ秒）");
            }
            catch (Exception ex)
            {
                AppLog.Error("配置を適用", LogTarget, ex, "幕を閉じる時に誤りが出たため、窓ごと消しました。画面に幕が残る時は PinFolder を終了してください");
            }
            finally
            {
                // Close に失敗しても、窓を消して資源を解放する（幕を画面に残さない）
                try
                {
                    curtain.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Error("配置を適用", LogTarget, ex, "幕を消せませんでした。画面に幕が残る時は PinFolder を終了してください");
                }
            }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(_snapshot, 0, 0);
            if (_icon == null)
            {
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // 幕のいる画面の倍率に合わせる（主の画面と倍率が違う画面でも同じ見た目の大きさにする）
            int size = IconSize * DeviceDpi / BaseDpi;
            int plate = (int)(size * PlateRatio);
            int cx = ClientSize.Width / 2;
            int cy = ClientSize.Height / 2;
            using (SolidBrush brush = new SolidBrush(PlateColor))
            {
                g.FillEllipse(brush, cx - plate / 2, cy - plate / 2, plate, plate);
            }
            float scale = Math.Min(size / (float)_icon.Width, size / (float)_icon.Height);
            float w = _icon.Width * scale;
            float h = _icon.Height * scale;
            g.DrawImage(_icon, cx - w / 2f, cy - h / 2f, w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _safety.Stop();
                _safety.Dispose();
                _snapshot.Dispose();
                if (_icon != null)
                {
                    _icon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
