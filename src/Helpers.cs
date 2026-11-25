using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 登録した物の小さなアイコン（16px）。フォルダは Windows のフォルダの絵、アプリはその exe・ファイルの絵。
    /// </summary>
    internal static class IconCache
    {
        /// <summary>Windows の小さなアイコンの大きさ（これ以下は小さな絵を読む）</summary>
        private const int SmallIconSize = 16;
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        /// <summary>アイコンを返す（無い・読めない時は null）</summary>
        /// <param name="entry">登録した物</param>
        /// <param name="size">絵の大きさ（px）</param>
        public static Image Get(Entry entry, int size)
        {
            string key = entry.Kind + "|" + size + "|" + entry.Path;
            Image image;
            if (Cache.TryGetValue(key, out image))
            {
                return image;
            }
            image = Load(entry, size);
            Cache[key] = image;
            return image;
        }

        /// <summary>覚えた絵を捨てる（場所を直した後に、新しい絵を読むため）</summary>
        public static void Clear()
        {
            // 表示中の札がまだ使っている絵があるため、ここでは破棄しない（参照が無くなれば .NET が片づける）
            Cache.Clear();
        }

        private static Image Load(Entry entry, int size)
        {
            try
            {
                if (entry.Kind == EntryKinds.Folder)
                {
                    return Directory.Exists(entry.Path) ? LoadShellIcon(entry.Path, size) : null;
                }
                if (!File.Exists(entry.Path))
                {
                    return null;
                }
                using (Icon icon = Icon.ExtractAssociatedIcon(entry.Path))
                {
                    return icon == null ? null : ToBitmap(icon, size);
                }
            }
            catch (Exception ex)
            {
                // 絵が無くても動きに影響しないので、1 行の記録だけ残して続ける
                AppLog.Info("アイコンの読み込み", entry.Path, "絵を読めなかった（動きに影響なし）：" + ex.GetType().FullName + "：" + ex.Message);
                return null;
            }
        }

        private static Image LoadShellIcon(string path, int size)
        {
            NativeMethods.SHFILEINFO info = new NativeMethods.SHFILEINFO();
            uint flags = NativeMethods.SHGFI_ICON | (size <= SmallIconSize ? NativeMethods.SHGFI_SMALLICON : 0u);
            IntPtr result = NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                using (Icon icon = Icon.FromHandle(info.hIcon))
                {
                    return ToBitmap(icon, size);
                }
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }

        private static Image ToBitmap(Icon icon, int size)
        {
            using (Bitmap source = icon.ToBitmap())
            {
                return new Bitmap(source, size, size);
            }
        }
    }

    /// <summary>
    /// Windows の起動時に常駐する設定（利用者のスタートアップのフォルダにショートカットを置く。管理者の権限は要らない）。
    /// </summary>
    internal static class Startup
    {
        private const string ShortcutName = "PinFolder.lnk";
        private const string WshProgId = "WScript.Shell";

        /// <summary>ショートカットの場所</summary>
        public static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName); }
        }

        /// <summary>起動時に常駐する設定か。true＝する</summary>
        public static bool IsEnabled()
        {
            return File.Exists(ShortcutPath);
        }

        /// <summary>起動時に常駐する・しないを切り替える。</summary>
        /// <param name="enabled">true＝する（ショートカットを作る）、false＝しない（PinFolder が作ったショートカットを消す）</param>
        /// <returns>true＝切り替えられた</returns>
        public static bool Set(bool enabled)
        {
            string path = ShortcutPath;
            if (enabled == IsEnabled())
            {
                return true;
            }
            try
            {
                if (!enabled)
                {
                    File.Delete(path);
                    AppLog.Info("起動時の常駐", path, "スタートアップのショートカットを消した");
                    return true;
                }
                Type type = Type.GetTypeFromProgID(WshProgId);
                if (type == null)
                {
                    AppLog.Warn("起動時の常駐", path, "WScript.Shell を使えない（会社の決まりで止められている可能性）",
                        "スタートアップのフォルダ（shell:startup）に、PinFolder.exe のショートカットを手で置いてください");
                    return false;
                }
                object shell = Activator.CreateInstance(type);
                object shortcut = null;
                try
                {
                    shortcut = ((dynamic)shell).CreateShortcut(path);
                    dynamic link = shortcut;
                    link.TargetPath = Application.ExecutablePath;
                    link.WorkingDirectory = AppPaths.ExeDir;
                    link.Save();
                }
                finally
                {
                    WindowFinder.ReleaseCom(shortcut);
                    WindowFinder.ReleaseCom(shell);
                }
                AppLog.Info("起動時の常駐", path, "スタートアップにショートカットを作った");
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("起動時の常駐", path, ex, "スタートアップのフォルダ（shell:startup）に書き込めるか確かめてください。会社の決まりで止められていることがあります");
                return false;
            }
        }
    }

    /// <summary>中身に合わせて大きさを決める子画面（大きさの確かめ --check-layout で使う）</summary>
    internal interface IFitDialog
    {
        /// <summary>大きさを中身に合わせる（画面を出さずに確かめる時に呼ぶ）。</summary>
        void FitForCheck();
    }

    /// <summary>画面の部品を作る小さな道具（位置は 96dpi の値で書き、画面の倍率に合わせて広がる）</summary>
    internal static class Ui
    {
        private static float _dpiScale;

        /// <summary>96dpi の値を、今の画面の倍率に合わせる（後から作る部品の大きさに使う）。</summary>
        public static int Scale(int value)
        {
            if (_dpiScale <= 0f)
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    _dpiScale = graphics.DpiX / 96f;
                }
            }
            return (int)Math.Round(value * _dpiScale);
        }

        /// <summary>ボタンの高さの下限（96dpi の値）</summary>
        private const int ButtonHeight = 28;

        /// <summary>画面の端と中身の間（96dpi の値）</summary>
        private const int DialogMargin = 12;

        /// <summary>ボタンを作る（文字が長い・大きい時は、文字に合わせて広がる）。</summary>
        public static Button Button(string text, int x, int y, int width, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowOnly;
            button.MinimumSize = new Size(width, ButtonHeight);
            button.Size = new Size(width, ButtonHeight);
            if (click != null)
            {
                button.Click += click;
            }
            return button;
        }

        /// <summary>
        /// 子画面の形を作る：中身の面（はみ出したら巻き取る）と、下のボタンの帯（いつも見える）。
        /// 下のボタンは右から並び、文字の大きさに合わせて広がる。
        /// </summary>
        /// <param name="form">子画面</param>
        /// <param name="body">中身を置く面（項目はこの中に置く）</param>
        /// <returns>下のボタンの帯（AddFooterButton で足す）</returns>
        public static FlowLayoutPanel CreateDialogLayout(Form form, out Panel body)
        {
            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Padding = new Padding(DialogMargin, 8, DialogMargin, DialogMargin)
            };
            body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            form.Controls.Add(body);
            form.Controls.Add(footer);
            return footer;
        }

        /// <summary>下のボタンの帯にボタンを足す（先に足した物が右）。</summary>
        public static Button AddFooterButton(FlowLayoutPanel footer, string text, EventHandler click)
        {
            Button button = Button(text, 0, 0, 88, click);
            button.Margin = new Padding(8, 0, 0, 0);
            footer.Controls.Add(button);
            return button;
        }

        /// <summary>
        /// 子画面の大きさを中身に合わせる（解像度・画面の倍率・文字の大きさで項目やボタンが隠れないように）。
        /// 作業領域より大きくなる時は作業領域に収め、中身の面を巻き取りにする（下のボタンはいつも見える）。
        /// 画面を出す前（Load）に呼ぶ。
        /// </summary>
        public static void FitToContent(Form form, Panel body, FlowLayoutPanel footer)
        {
            form.PerformLayout();
            int margin = Scale(DialogMargin);
            Size content = ContentSize(body);
            int footerWidth = footer.GetPreferredSize(Size.Empty).Width;
            int footerHeight = footer.GetPreferredSize(Size.Empty).Height;
            // 面の内側の余白と、巻き取りの帯が出た時の幅も見込む
            int extraWidth = margin + body.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth;
            int extraHeight = margin + body.Padding.Vertical;
            int width = Math.Max(Math.Max(form.ClientSize.Width, content.Width + extraWidth), footerWidth);
            int height = Math.Max(form.ClientSize.Height, content.Height + extraHeight + footerHeight);

            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int frameWidth = form.Width - form.ClientSize.Width;
            int frameHeight = form.Height - form.ClientSize.Height;
            width = Math.Min(width, area.Width - frameWidth);
            height = Math.Min(height, area.Height - frameHeight);
            form.ClientSize = new Size(width, height);
            form.Location = new Point(area.Left + (area.Width - form.Width) / 2, area.Top + (area.Height - form.Height) / 2);
        }

        /// <summary>面の中身が要る大きさ（タブがあれば、いちばん大きいタブの中身にタブの縁を足す）。</summary>
        private static Size ContentSize(Control container)
        {
            int width = 0;
            int height = 0;
            foreach (Control child in container.Controls)
            {
                TabControl tabs = child as TabControl;
                if (tabs != null)
                {
                    int edgeWidth = tabs.Width - tabs.DisplayRectangle.Width;
                    int edgeHeight = tabs.Height - tabs.DisplayRectangle.Height;
                    foreach (TabPage page in tabs.TabPages)
                    {
                        page.AutoScroll = true;
                        Size inner = ContentSize(page);
                        width = Math.Max(width, inner.Width + edgeWidth + Scale(DialogMargin));
                        height = Math.Max(height, inner.Height + edgeHeight + Scale(DialogMargin));
                    }
                    continue;
                }
                Size preferred = child.AutoSize ? child.GetPreferredSize(Size.Empty) : child.Size;
                width = Math.Max(width, child.Left + Math.Max(child.Width, preferred.Width));
                height = Math.Max(height, child.Top + Math.Max(child.Height, preferred.Height));
            }
            return new Size(width, height);
        }

        /// <summary>文字の札を作る。</summary>
        public static Label Label(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.AutoSize = true;
            return label;
        }

        /// <summary>誤りの文の札を作る（初めは空）。</summary>
        public static Label ErrorLabel(int x, int y, int width)
        {
            Label label = new Label();
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.MaximumSize = new Size(width, 0);
            label.Tag = UiTags.Error;
            return label;
        }

        /// <summary>入力欄を作る。</summary>
        public static TextBox TextBox(int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Location = new Point(x, y);
            box.Width = width;
            return box;
        }

        /// <summary>入力欄に、何も無い時の薄い見出しを出す（Windows の標準の機能）。</summary>
        public static void SetCue(TextBox box, string cue)
        {
            NativeMethods.SendMessage(box.Handle, NativeMethods.EM_SETCUEBANNER, new IntPtr(1), cue);
        }

        /// <summary>子画面の共通の形（大きさ固定・中央）。</summary>
        public static void SetupDialog(Form form, string title, int width, int height)
        {
            form.Text = title;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.ClientSize = new Size(width, height);
            form.Icon = AppPaths.WindowIcon();
        }
    }
}
