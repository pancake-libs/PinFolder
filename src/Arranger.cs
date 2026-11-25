using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>並べた結果</summary>
    internal sealed class ArrangeResult
    {
        public ArrangeResult()
        {
            Failed = new List<string>();
        }

        /// <summary>区画へ置いた数</summary>
        public int Placed { get; set; }

        /// <summary>開けなかった・窓が見つからなかった物の名前（その区画は空のまま）</summary>
        public List<string> Failed { get; private set; }
    }

    /// <summary>
    /// 窓を区画へ置く。区画は、画面の作業領域を「横の列 × 縦 2 段」に均等に割った四角（すき間なし）。
    /// 開いていない物は開いてから置く。Windows 10・11 の窓の見えない縁の分を差し引き、見た目でぴったり並べる。
    /// </summary>
    internal static class Arranger
    {
        /// <summary>開いた後、窓が出るのを待つ最長の時間</summary>
        private const int OpenWaitMilliseconds = 5000;

        /// <summary>窓が出たかを見る間隔</summary>
        private const int PollMilliseconds = 100;

        /// <summary>PinFolder 自身のプロセスの番号（自分の画面を動かさないため）</summary>
        private static readonly uint CurrentProcessId = GetCurrentProcessId();

        /// <summary>置き直しを見る回数</summary>
        private const int CorrectRounds = 2;

        /// <summary>置き直しの前に待つ時間（窓が倍率の違う画面で大きさを変え直すのを待つ）</summary>
        private const int CorrectWaitMilliseconds = 150;

        /// <summary>区画とのずれを許す幅（px）</summary>
        private const int CorrectTolerance = 2;

        /// <summary>窓のクラス名を読む長さ</summary>
        private const int ClassNameLength = 256;

        /// <summary>動かしてはいけない Windows の窓（デスクトップ・タスクバー）のクラス名</summary>
        private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        /// <summary>区画の四角を求める。</summary>
        /// <param name="area">画面の作業領域</param>
        /// <param name="shape">区画の形</param>
        /// <param name="index">区画の番号（0 から。左上から右へ、上の段から下の段へ）</param>
        public static Rectangle ZoneBounds(Rectangle area, string shape, int index)
        {
            int columns = AreaShapes.Columns(shape);
            int column = index % columns;
            int row = index / columns;
            int left = area.Left + area.Width * column / columns;
            int right = area.Left + area.Width * (column + 1) / columns;
            int top = area.Top + area.Height * row / AreaShapes.Rows;
            int bottom = area.Top + area.Height * (row + 1) / AreaShapes.Rows;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// 区画のとおりに並べる（配置を適用）。
        /// ① 開いている物は窓を覚え、開いていない物があれば並べる先の画面に幕（撮った絵）をかけて、一度にすべて開く
        /// ② 新しい窓がそろうまで待ち（始めてから最長 5 秒）、出てきた窓はすぐ隠す
        /// ③ 同じ窓を 2 つの区画に置かないよう、重なりを外す
        /// ④ 幕の後ろで、一度にまとめて区画へ置いて表示する（終わったら幕を外す）
        /// ⑤ 並べた窓を、ほかのアプリの窓より手前に出す（並べ直した時も手前に出る）
        /// </summary>
        /// <param name="config">設定（登録を番号で探す）</param>
        /// <param name="shape">区画の形</param>
        /// <param name="zones">区画ごとの登録の番号（空の文字は空いた区画）</param>
        /// <param name="area">並べる画面の作業領域</param>
        public static ArrangeResult Arrange(AppConfig config, string shape, IList<string> zones, Rectangle area)
        {
            ArrangeResult result = new ArrangeResult();
            int count = Math.Min(zones.Count, AreaShapes.Count(shape));
            Stopwatch watch = Stopwatch.StartNew();
            AppLog.Info("配置を適用", AreaShapes.Label(shape), "始めた（区画 " + count + " 個、画面の作業領域 位置 " + area.Left + "," + area.Top + "・大きさ " + area.Width + "×" + area.Height + "）");

            List<Slot> ready = new List<Slot>();
            List<Slot> toOpen = new List<Slot>();
            List<Slot> waiting = new List<Slot>();
            HashSet<IntPtr> used = new HashSet<IntPtr>();
            List<IntPtr> placed = new List<IntPtr>();
            Curtain curtain = null;
            try
            {
                // ① 開いている物は窓を覚え、開いていない物は「開く物」に分ける
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrEmpty(zones[i]))
                    {
                        continue;
                    }
                    Job job;
                    Entry entry = FindEntry(config, zones[i], out job);
                    if (entry == null)
                    {
                        // 登録を消した後の区画は、空いた区画として扱う
                        continue;
                    }
                    Slot slot = new Slot(job, entry, i);
                    slot.Window = FindWindow(entry);
                    if (slot.Window != IntPtr.Zero)
                    {
                        ready.Add(slot);
                        continue;
                    }
                    toOpen.Add(slot);
                }

                // 新しく開く物がある時だけ、並べる先の画面に幕をかける（1 つずつ窓が出る過程を見せない）
                if (toOpen.Count > 0)
                {
                    curtain = Curtain.Cover(area);
                }
                foreach (Slot slot in toOpen)
                {
                    string message;
                    if (Launcher.Open(slot.Job.Name, slot.Entry, out message) == OpenResult.Failed)
                    {
                        Fail(result, slot, "開けなかった");
                        continue;
                    }
                    waiting.Add(slot);
                }

                // ② 新しく開いた窓がそろうのを待つ。出てきた窓は縁の幅を測ってからすぐ隠す
                while (waiting.Count > 0 && watch.ElapsedMilliseconds < OpenWaitMilliseconds)
                {
                    Application.DoEvents();
                    Thread.Sleep(PollMilliseconds);
                    for (int k = waiting.Count - 1; k >= 0; k--)
                    {
                        Slot slot = waiting[k];
                        slot.Window = FindWindow(slot.Entry);
                        if (slot.Window == IntPtr.Zero)
                        {
                            continue;
                        }
                        // 最大化・最小化で開いた窓は、先に戻してから縁を測る（戻さないと区画の大きさにならない）
                        if (NativeMethods.IsZoomed(slot.Window) || NativeMethods.IsIconic(slot.Window))
                        {
                            NativeMethods.ShowWindow(slot.Window, NativeMethods.SW_RESTORE);
                        }
                        slot.Border = Border(slot.Window);
                        slot.Hidden = true;
                        NativeMethods.ShowWindow(slot.Window, NativeMethods.SW_HIDE);
                        ready.Add(slot);
                        waiting.RemoveAt(k);
                    }
                }
                foreach (Slot slot in waiting)
                {
                    Fail(result, slot, OpenWaitMilliseconds / 1000 + " 秒待っても窓が出なかった");
                }

                // ③ 同じ窓が 2 つの区画に入っていないかを見て、区画の順に並べる
                ready.Sort((a, b) => a.Zone.CompareTo(b.Zone));
                List<Slot> placeable = new List<Slot>();
                foreach (Slot slot in ready)
                {
                    if (!used.Add(slot.Window))
                    {
                        // Win11 のタブで 2 つのフォルダが 1 つの窓に入っている時など
                        Fail(result, slot, "ほかの区画と同じ窓だった（エクスプローラーのタブにせず、別の窓で開いてから適用し直す）");
                        continue;
                    }
                    if (!slot.Hidden)
                    {
                        if (NativeMethods.IsZoomed(slot.Window) || NativeMethods.IsIconic(slot.Window))
                        {
                            NativeMethods.ShowWindow(slot.Window, NativeMethods.SW_RESTORE);
                        }
                        slot.Border = Border(slot.Window);
                    }
                    placeable.Add(slot);
                }

                // ④ 一度にまとめて置いて表示する（幕の後ろで行う）
                placed = PlaceAll(placeable, area, shape, result);
            }
            finally
            {
                // 途中で失敗しても、隠した窓は必ず表示に戻し、幕は必ず外す
                foreach (Slot slot in ready)
                {
                    if (slot.Hidden && slot.Window != IntPtr.Zero && !NativeMethods.IsWindowVisible(slot.Window))
                    {
                        NativeMethods.ShowWindow(slot.Window, NativeMethods.SW_SHOWNOACTIVATE);
                    }
                }
                Curtain.Remove(curtain);
            }

            // ⑤ 幕を外してから、並べた窓をほかのアプリより手前に出し、いちばん前の区画の窓に入力を向ける
            // （幕をかけている間に手前に出すと、一瞬だけ幕の上に出るため）
            foreach (IntPtr window in placed)
            {
                BringToFront(window);
            }
            if (placed.Count > 0)
            {
                WindowFinder.Activate(placed[0]);
            }
            AppLog.Info("配置を適用", AreaShapes.Label(shape), "終えた（区画へ " + result.Placed + " 件を置いた" + (result.Failed.Count > 0 ? "、" + result.Failed.Count + " 件は空のまま" : string.Empty)
                + "、" + watch.ElapsedMilliseconds / 1000.0 + " 秒）");
            return result;
        }

        /// <summary>まとめて区画へ置いて表示する。</summary>
        /// <returns>置けた窓（区画の順）</returns>
        private static List<IntPtr> PlaceAll(List<Slot> slots, Rectangle area, string shape, ArrangeResult result)
        {
            List<IntPtr> placedWindows = new List<IntPtr>();
            if (slots.Count == 0)
            {
                return placedWindows;
            }
            const uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
            IntPtr defer = NativeMethods.BeginDeferWindowPos(slots.Count);
            foreach (Slot slot in slots)
            {
                Rectangle rect = slot.TargetBounds(ZoneBounds(area, shape, slot.Zone));
                if (defer != IntPtr.Zero)
                {
                    // 0 が返ったら、まとめて置く用意は Windows が捨てている（EndDeferWindowPos は呼ばず、下で 1 つずつ置く）
                    defer = NativeMethods.DeferWindowPos(defer, slot.Window, NativeMethods.HWND_TOP, rect.X, rect.Y, rect.Width, rect.Height, flags);
                }
            }
            bool together = defer != IntPtr.Zero && NativeMethods.EndDeferWindowPos(defer);
            foreach (Slot slot in slots)
            {
                bool placed = together;
                if (!together)
                {
                    // まとめて置けなかった時（管理者の権限の窓が混じる時など）は 1 つずつ置く
                    Rectangle rect = slot.TargetBounds(ZoneBounds(area, shape, slot.Zone));
                    placed = NativeMethods.SetWindowPos(slot.Window, NativeMethods.HWND_TOP, rect.X, rect.Y, rect.Width, rect.Height, flags);
                }
                if (!placed)
                {
                    Fail(result, slot, "窓を動かせなかった（Windows のエラー " + Marshal.GetLastWin32Error() + "。管理者の権限で動いている窓は動かせない）");
                    slot.Failed = true;
                    continue;
                }
                slot.Hidden = false;
                result.Placed++;
                placedWindows.Add(slot.Window);
                AppLog.Info("配置を適用", slot.Target, "区画 " + (slot.Zone + 1) + " へ置いた");
            }
            // 2 回目：倍率の違う画面へ移った窓は、Windows の知らせで自分の大きさを変え直すことがあり、縁の幅も変わる。
            // 少し待ってから見えている四角を測り直し、区画からずれた窓だけを置き直す
            CorrectPlacement(slots, area, shape);
            return placedWindows;
        }

        /// <summary>置いた窓の見えている四角を測り、区画から外れていれば、縁を測り直して置き直す（最大 2 回）。</summary>
        private static void CorrectPlacement(List<Slot> slots, Rectangle area, string shape)
        {
            const uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;
            for (int round = 0; round < CorrectRounds; round++)
            {
                Application.DoEvents();
                Thread.Sleep(CorrectWaitMilliseconds);
                int corrected = 0;
                foreach (Slot slot in slots)
                {
                    if (slot.Failed || slot.Hidden || !NativeMethods.IsWindowVisible(slot.Window))
                    {
                        continue;
                    }
                    Rectangle zone = ZoneBounds(area, shape, slot.Zone);
                    Rectangle visible = VisibleBounds(slot.Window);
                    if (Near(visible, zone))
                    {
                        continue;
                    }
                    slot.Border = Border(slot.Window);
                    Rectangle rect = slot.TargetBounds(zone);
                    if (!NativeMethods.SetWindowPos(slot.Window, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, flags))
                    {
                        AppLog.Warn("配置を適用", slot.Target, "置き直せなかった（Windows のエラー " + Marshal.GetLastWin32Error() + "）", "その窓を手で区画へ動かしてください");
                        slot.Failed = true;
                        continue;
                    }
                    corrected++;
                }
                if (corrected == 0)
                {
                    return;
                }
                AppLog.Info("配置を適用", AreaShapes.Label(shape), "区画からずれた窓 " + corrected + " 件を置き直した（" + (round + 1) + " 回目）");
            }
            // 置き直してもずれている窓（大きさの下限がある窓など）を記録する
            foreach (Slot slot in slots)
            {
                if (!slot.Failed && !slot.Hidden && NativeMethods.IsWindowVisible(slot.Window) && !Near(VisibleBounds(slot.Window), ZoneBounds(area, shape, slot.Zone)))
                {
                    AppLog.Warn("配置を適用", slot.Target, "区画の大きさに合わなかった（窓の大きさに下限がある、または倍率の違う画面で大きさを変え直した）", "その窓の大きさを手で合わせてください");
                }
            }
        }

        /// <summary>2 つの四角が、ほぼ同じ（ずれが CorrectTolerance 以下）か。</summary>
        private static bool Near(Rectangle a, Rectangle b)
        {
            return Math.Abs(a.Left - b.Left) <= CorrectTolerance && Math.Abs(a.Top - b.Top) <= CorrectTolerance
                && Math.Abs(a.Right - b.Right) <= CorrectTolerance && Math.Abs(a.Bottom - b.Bottom) <= CorrectTolerance;
        }

        /// <summary>ほかのアプリの窓より手前に出す（いったん最前面にして戻す。入力の向きは変えない）。</summary>
        private static void BringToFront(IntPtr window)
        {
            // もともと常に手前の窓は、そのままにする（往復すると常に手前でなくなる）
            if ((NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0)
            {
                return;
            }
            const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;
            NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
            NativeMethods.SetWindowPos(window, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        }

        private static void Fail(ArrangeResult result, Slot slot, string reason)
        {
            result.Failed.Add(slot.Entry.Name);
            AppLog.Warn("配置を適用", slot.Target, reason + "（区画 " + (slot.Zone + 1) + " は空のまま）",
                "その物を一覧の札から開けるか確かめてください（フォルダが無い・アプリが起動できない・管理者の権限の窓）");
        }

        /// <summary>窓の見えない縁（影）の幅。窓の外側と見えている部分の差</summary>
        private static Padding Border(IntPtr window)
        {
            NativeMethods.RECT outer;
            NativeMethods.GetWindowRect(window, out outer);
            Rectangle visible = VisibleBounds(window);
            return new Padding(visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom);
        }

        /// <summary>区画 1 つに置く物（登録・区画の番号・窓・縁の幅）</summary>
        private sealed class Slot
        {
            public Slot(Job job, Entry entry, int zone)
            {
                Job = job;
                Entry = entry;
                Zone = zone;
            }

            public Job Job { get; private set; }
            public Entry Entry { get; private set; }
            public int Zone { get; private set; }
            public IntPtr Window { get; set; }
            public Padding Border { get; set; }

            /// <summary>true＝新しく開いて、置くまで隠している窓</summary>
            public bool Hidden { get; set; }

            /// <summary>true＝動かせなかった窓（置き直しの対象から外す）</summary>
            public bool Failed { get; set; }

            /// <summary>ログに書く名前</summary>
            public string Target
            {
                get { return Job.Name + " / " + Entry.Name; }
            }

            /// <summary>見えない縁の分を足した、窓の外側の四角</summary>
            public Rectangle TargetBounds(Rectangle zone)
            {
                return new Rectangle(zone.Left - Border.Left, zone.Top - Border.Top,
                    zone.Width + Border.Horizontal, zone.Height + Border.Vertical);
            }
        }
        /// <summary>手前の窓を、隣の区画へ移す（区画を移るキー）。</summary>
        /// <param name="shape">区画の形（最後に使った形）</param>
        /// <param name="columnStep">横の向き（-1＝左、1＝右、0＝動かさない）</param>
        /// <param name="rowStep">縦の向き（-1＝上、1＝下、0＝動かさない）</param>
        public static void MoveForeground(string shape, int columnStep, int rowStep)
        {
            IntPtr window = NativeMethods.GetForegroundWindow();
            string skip = SkipReason(window);
            if (skip != null)
            {
                AppLog.Info("区画を移す", "手前の窓", "動かさなかった（" + skip + "）");
                return;
            }
            if (NativeMethods.IsIconic(window))
            {
                NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
            }
            Rectangle visible = VisibleBounds(window);
            Point center = new Point(visible.Left + visible.Width / 2, visible.Top + visible.Height / 2);
            Rectangle area = Screen.FromPoint(center).WorkingArea;
            int columns = AreaShapes.Columns(shape);
            int column = Clamp((center.X - area.Left) * columns / Math.Max(1, area.Width), 0, columns - 1);
            int row = Clamp((center.Y - area.Top) * AreaShapes.Rows / Math.Max(1, area.Height), 0, AreaShapes.Rows - 1);
            column = Clamp(column + columnStep, 0, columns - 1);
            row = Clamp(row + rowStep, 0, AreaShapes.Rows - 1);
            bool placed = Place(window, ZoneBounds(area, shape, row * columns + column));
            AppLog.Info("区画を移す", "手前の窓", placed ? "区画 " + (row * columns + column + 1) + " へ移した" : "動かせなかった（管理者の権限で動いている窓など）");
        }

        /// <summary>手前の窓を動かしてはいけない理由（動かしてよければ null）</summary>
        private static string SkipReason(IntPtr window)
        {
            if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window))
            {
                return "手前の窓が無い";
            }
            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId == CurrentProcessId)
            {
                return "PinFolder の画面";
            }
            System.Text.StringBuilder name = new System.Text.StringBuilder(ClassNameLength);
            NativeMethods.GetClassName(window, name, name.Capacity);
            if (Array.IndexOf(ShellClasses, name.ToString()) >= 0)
            {
                return "デスクトップ・タスクバー";
            }
            return null;
        }

        /// <summary>登録を番号で探す（無ければ null）。</summary>
        public static Entry FindEntry(AppConfig config, string id, out Job owner)
        {
            owner = null;
            foreach (Job job in config.Jobs)
            {
                foreach (Entry entry in job.Entries)
                {
                    if (entry.Id == id)
                    {
                        owner = job;
                        return entry;
                    }
                }
            }
            return null;
        }

        private static IntPtr FindWindow(Entry entry)
        {
            return entry.Kind == EntryKinds.Folder ? WindowFinder.FindExplorer(entry.Path) : WindowFinder.FindApp(entry.Path);
        }


        /// <summary>窓を四角へ置く（最大化・最小化は元に戻し、見えない縁の分を差し引く）。</summary>
        /// <returns>true＝置けた。false＝Windows が受け付けなかった</returns>
        private static bool Place(IntPtr window, Rectangle zone)
        {
            if (NativeMethods.IsZoomed(window) || NativeMethods.IsIconic(window))
            {
                NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
            }
            NativeMethods.RECT outer;
            NativeMethods.GetWindowRect(window, out outer);
            Rectangle visible = VisibleBounds(window);
            // 見えない縁（影）の幅＝窓の外側と見えている部分の差
            int left = visible.Left - outer.Left;
            int top = visible.Top - outer.Top;
            int right = outer.Right - visible.Right;
            int bottom = outer.Bottom - visible.Bottom;
            return NativeMethods.SetWindowPos(window, IntPtr.Zero,
                zone.Left - left, zone.Top - top, zone.Width + left + right, zone.Height + top + bottom,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>窓の見えている部分の四角（取れない時は窓の外側）</summary>
        private static Rectangle VisibleBounds(IntPtr window)
        {
            NativeMethods.RECT rect;
            if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(NativeMethods.RECT))) != 0)
            {
                NativeMethods.GetWindowRect(window, out rect);
            }
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        private static uint GetCurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return (uint)process.Id;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
