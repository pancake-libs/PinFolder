using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 画面ごとの倍率に合わせて、画面（Form）の部品と文字の大きさを作り直す（exe 1 つのまま対応する。2026-11-23 の利用者の回答で案 B）。
    /// ・画面を倍率の違う画面へ動かした時（Windows の WM_DPICHANGED）
    /// ・画面を主の画面と倍率の違う画面に初めて出した時
    /// 部品の位置・大きさは Control.Scale で、文字は大きさを掛けた Font で作り直す。
    /// </summary>
    internal sealed class DpiWatcher : NativeWindow
    {
        private const int WM_DPICHANGED = 0x02E0;
        private const float BaseDpi = 96f;

        /// <summary>見張っている画面と、その見張りの対応</summary>
        private static readonly Dictionary<Form, DpiWatcher> Watchers = new Dictionary<Form, DpiWatcher>();

        private readonly Form _form;
        private int _dpi;

        /// <summary>この見張りが作った文字（次に作り直した時に片づける。スタイルの共有の文字は片づけない）</summary>
        private List<Font> _createdFonts = new List<Font>();

        private DpiWatcher(Form form)
        {
            _form = form;
            _dpi = SystemDpi();
        }

        /// <summary>画面を見張り始める（画面を作った直後に呼ぶ）。</summary>
        public static void Attach(Form form)
        {
            if (Watchers.ContainsKey(form))
            {
                return;
            }
            DpiWatcher watcher = new DpiWatcher(form);
            Watchers[form] = watcher;
            if (form.IsHandleCreated)
            {
                watcher.Hook();
            }
            form.HandleCreated += (s, e) => watcher.Hook();
            form.HandleDestroyed += (s, e) => watcher.ReleaseHandle();
            form.Shown += (s, e) => watcher.MatchMonitor(null);
            form.VisibleChanged += (s, e) =>
            {
                if (form.Visible)
                {
                    watcher.MatchMonitor(null);
                }
            };
            form.Disposed += (s, e) => Watchers.Remove(form);
        }

        /// <summary>その画面の、主の画面の倍率に対する今の倍率（見張っていない画面は 1）</summary>
        public static float FactorOf(Control control)
        {
            Form form = control as Form ?? (control == null ? null : control.FindForm());
            DpiWatcher watcher;
            if (form != null && Watchers.TryGetValue(form, out watcher))
            {
                return watcher._dpi / (float)SystemDpi();
            }
            return 1f;
        }

        /// <summary>96dpi の値を、その画面のある画面の倍率に合わせる（後から作る部品の大きさに使う）。</summary>
        public static int Scale(Control control, int value)
        {
            return (int)Math.Round(Ui.Scale(value) * FactorOf(control));
        }

        /// <summary>
        /// スタイルを当て直した後（文字が主の画面の大きさに戻る）に、今の倍率の文字にし直す。
        /// </summary>
        public static void ReapplyFonts(Form form)
        {
            float factor = FactorOf(form);
            DpiWatcher watcher;
            if (Math.Abs(factor - 1f) > 0.01f && Watchers.TryGetValue(form, out watcher))
            {
                watcher.ReplaceFonts(factor);
            }
        }

        private void Hook()
        {
            if (Handle != _form.Handle)
            {
                if (Handle != IntPtr.Zero)
                {
                    ReleaseHandle();
                }
                AssignHandle(_form.Handle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                // 下位 16 ビットが新しい倍率。lParam は Windows が勧める新しい四角
                int newDpi = m.WParam.ToInt32() & 0xFFFF;
                RECT suggested = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                Rescale(newDpi, Rectangle.FromLTRB(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>画面のある画面の倍率が今の倍率と違えば、合わせる。</summary>
        private void MatchMonitor(Rectangle? bounds)
        {
            if (!_form.IsHandleCreated)
            {
                return;
            }
            int dpi = WindowDpi(_form.Handle);
            if (dpi > 0 && dpi != _dpi)
            {
                Rescale(dpi, bounds);
            }
        }

        private void Rescale(int newDpi, Rectangle? suggested)
        {
            if (newDpi <= 0 || newDpi == _dpi)
            {
                return;
            }
            int oldDpi = _dpi;
            float factor = newDpi / (float)_dpi;
            _dpi = newDpi;
            try
            {
                _form.SuspendLayout();
                Size client = _form.ClientSize;
                _form.Scale(new SizeF(factor, factor));
                ReplaceFonts(factor);
                _form.ResumeLayout(true);
                if (suggested.HasValue)
                {
                    _form.Bounds = suggested.Value;
                }
                else
                {
                    _form.ClientSize = new Size((int)Math.Round(client.Width * factor), (int)Math.Round(client.Height * factor));
                }
                AppLog.Info("画面の倍率", _form.Text, "倍率 " + Math.Round(oldDpi / BaseDpi * 100) + "% → " + Math.Round(newDpi / BaseDpi * 100) + "% の画面に合わせた");
            }
            catch (Exception ex)
            {
                // 途中までしか合わせられなかったので、倍率の値を元に戻す（次に画面を移した時にやり直す）
                _dpi = oldDpi;
                AppLog.Error("画面の倍率", _form.Text, ex, "画面を主の画面へ戻すと、元の大きさで使えます");
            }
        }

        /// <summary>文字を作り直し、前にこの見張りが作った文字を片づける。</summary>
        private void ReplaceFonts(float factor)
        {
            List<Font> created = new List<Font>();
            ScaleFonts(_form, factor, created);
            foreach (Font old in _createdFonts)
            {
                old.Dispose();
            }
            _createdFonts = created;
        }

        /// <summary>画面と、文字を直に持つ部品の文字を、同じ割合で大きくする。</summary>
        private static void ScaleFonts(Control control, float factor, List<Font> created)
        {
            Font own = control.Font;
            bool explicitFont = control.Parent == null || !ReferenceEquals(own, control.Parent.Font);
            foreach (Control child in control.Controls)
            {
                ScaleFonts(child, factor, created);
            }
            if (explicitFont)
            {
                Font font = new Font(own.FontFamily, own.Size * factor, own.Style, own.Unit);
                created.Add(font);
                control.Font = font;
            }
        }

        private static int SystemDpi()
        {
            return (int)Math.Round(Ui.Scale(100) * BaseDpi / 100f);
        }

        /// <summary>窓のある画面の倍率（Windows 10 1607 より前は主の画面の倍率）。</summary>
        private static int WindowDpi(IntPtr handle)
        {
            try
            {
                return (int)GetDpiForWindow(handle);
            }
            catch (EntryPointNotFoundException)
            {
                return SystemDpi();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
