using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PinFolder
{
    /// <summary>
    /// 開いている窓を探す・前面に出す・閉じる。
    /// フォルダはエクスプローラーの窓（COM の Shell.Application）、アプリは exe の場所が同じプロセスの窓で判定する。
    /// </summary>
    internal static class WindowFinder
    {
        private const string ShellProgId = "Shell.Application";
        /// <summary>エクスプローラーの exe の名前</summary>
        public const string ExplorerExe = "explorer.exe";
        private const string ExeExtension = ".exe";
        private const int PathBufferSize = 1024;

        /// <summary>エクスプローラーの窓が取れなかった時のログを 1 回だけ書くための印</summary>
        private static bool _shellErrorLogged;

        /// <summary>比べるためにパスをそろえる（大文字・末尾の \ を無くす）</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            string full = path.Trim();
            try
            {
                full = Path.GetFullPath(full);
            }
            catch (Exception)
            {
                // 形の崩れたパスは、そのまま比べる（比べて一致しないだけで害はない）
            }
            return full.TrimEnd('\\').ToUpperInvariant();
        }

        /// <summary>exe（アプリ本体）か。false はファイル（関連付けで開く物）</summary>
        public static bool IsExe(string path)
        {
            return !string.IsNullOrEmpty(path) && path.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 開いているエクスプローラーの窓を、フォルダごとにすべて取る。
        /// 取れたら、各窓の COM の物を呼び出し側が使い終えたら Release する。
        /// </summary>
        public static List<ExplorerWindow> GetExplorerWindows()
        {
            List<ExplorerWindow> list = new List<ExplorerWindow>();
            object shell = null;
            object windows = null;
            try
            {
                Type type = Type.GetTypeFromProgID(ShellProgId);
                if (type == null)
                {
                    LogShellErrorOnce("Shell.Application を使えない（Windows の部品が無い・止められている）", null);
                    return list;
                }
                shell = Activator.CreateInstance(type);
                windows = ((dynamic)shell).Windows();
                int count = ((dynamic)windows).Count;
                int skipped = 0;
                Exception firstError = null;
                for (int i = 0; i < count; i++)
                {
                    object window = null;
                    bool kept = false;
                    try
                    {
                        window = ((dynamic)windows).Item(i);
                        if (window == null)
                        {
                            continue;
                        }
                        dynamic com = window;
                        string exe = com.FullName;
                        if (exe == null || !exe.EndsWith(ExplorerExe, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string folder = com.Document.Folder.Self.Path;
                        long handle = Convert.ToInt64(com.HWND);
                        list.Add(new ExplorerWindow(folder, new IntPtr(handle), window));
                        kept = true;
                    }
                    catch (Exception ex)
                    {
                        // 閉じかけの窓・フォルダでない場所（コントロールパネルなど）は飛ばす。すべて失敗した時だけ記録する
                        skipped++;
                        if (firstError == null)
                        {
                            firstError = ex;
                        }
                    }
                    finally
                    {
                        if (!kept)
                        {
                            ReleaseCom(window);
                        }
                    }
                }
                if (count > 0 && skipped == count)
                {
                    LogShellErrorOnce("開いている窓 " + count + " 件をすべて読めなかった", firstError);
                }
            }
            catch (Exception ex)
            {
                LogShellErrorOnce("エクスプローラーの窓の一覧を取れなかった", ex);
            }
            finally
            {
                ReleaseCom(windows);
                ReleaseCom(shell);
            }
            return list;
        }

        /// <summary>COM の物を手放す（null・COM でない物は何もしない）。</summary>
        public static void ReleaseCom(object com)
        {
            if (com != null && Marshal.IsComObject(com))
            {
                Marshal.FinalReleaseComObject(com);
            }
        }

        private static void LogShellErrorOnce(string reason, Exception ex)
        {
            if (_shellErrorLogged)
            {
                return;
            }
            _shellErrorLogged = true;
            const string next = "開いている窓の判定ができないため、同じフォルダを開くと 2 つ目の窓が開きます。PinFolder を起動し直し、直らなければこのログを開発の担当へ渡してください";
            if (ex == null)
            {
                AppLog.Warn("エクスプローラーの窓の取得", ShellProgId, reason, next);
            }
            else
            {
                AppLog.Error("エクスプローラーの窓の取得（" + reason + "）", ShellProgId, ex, next);
            }
        }

        /// <summary>そのフォルダを開いているエクスプローラーの窓（無ければ IntPtr.Zero）</summary>
        public static IntPtr FindExplorer(string folder)
        {
            string target = NormalizePath(folder);
            IntPtr found = IntPtr.Zero;
            foreach (ExplorerWindow window in GetExplorerWindows())
            {
                if (found == IntPtr.Zero && window.NormalizedPath == target)
                {
                    found = window.Handle;
                }
                window.Release();
            }
            return found;
        }

        /// <summary>そのフォルダを開いているエクスプローラーの窓を閉じる。</summary>
        /// <returns>閉じた窓の数</returns>
        public static int CloseExplorer(string folder)
        {
            string target = NormalizePath(folder);
            int closed = 0;
            foreach (ExplorerWindow window in GetExplorerWindows())
            {
                if (window.NormalizedPath == target)
                {
                    try
                    {
                        window.Com.Quit();
                        closed++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("フォルダの窓を閉じる", folder, ex, "その窓を手で閉じてください");
                    }
                }
                window.Release();
            }
            return closed;
        }

        /// <summary>アプリ（exe）・ファイルの窓（無ければ IntPtr.Zero）</summary>
        public static IntPtr FindApp(string path)
        {
            if (IsExe(path))
            {
                string target = NormalizePath(path);
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle != IntPtr.Zero && NormalizePath(ProcessPath(process.Id)) == target)
                        {
                            return handle;
                        }
                    }
                    catch (Exception)
                    {
                        // 終わりかけのプロセス・権限の無いプロセスは飛ばす
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
                return IntPtr.Zero;
            }
            return FindWindowByTitle(Path.GetFileName(path));
        }

        /// <summary>アプリの窓に「閉じる」を送る（保存の確認はアプリが出す）。</summary>
        /// <returns>true＝送った</returns>
        public static bool CloseApp(string path)
        {
            IntPtr handle = FindApp(path);
            if (handle == IntPtr.Zero)
            {
                return false;
            }
            return NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>窓を前面に出す（最小化していれば元に戻す）。</summary>
        /// <returns>true＝前面に出せた。false＝Windows の制限で出せなかった（タスクバーで点滅する）</returns>
        public static bool Activate(IntPtr handle)
        {
            if (NativeMethods.IsIconic(handle))
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
            }
            // 通知領域のアプリからは前面に出す許可が無いことがあるため、Alt を一度押して許可を得る（よく使われる方法）
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            return NativeMethods.SetForegroundWindow(handle);
        }

        /// <summary>プロセスの exe の場所（取れなければ空）</summary>
        public static string ProcessPath(int processId)
        {
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }
            try
            {
                StringBuilder buffer = new StringBuilder(PathBufferSize);
                uint size = (uint)buffer.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : string.Empty;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        /// <summary>見えている窓の題名をすべて取る（ファイルを開いている窓の判定に使う）</summary>
        public static List<KeyValuePair<IntPtr, string>> VisibleWindowTitles()
        {
            List<KeyValuePair<IntPtr, string>> list = new List<KeyValuePair<IntPtr, string>>();
            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd))
                {
                    return true;
                }
                int length = NativeMethods.GetWindowTextLength(hWnd);
                if (length == 0)
                {
                    return true;
                }
                StringBuilder title = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, title, title.Capacity);
                list.Add(new KeyValuePair<IntPtr, string>(hWnd, title.ToString()));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private static IntPtr FindWindowByTitle(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return IntPtr.Zero;
            }
            foreach (KeyValuePair<IntPtr, string> window in VisibleWindowTitles())
            {
                if (window.Value.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return window.Key;
                }
            }
            return IntPtr.Zero;
        }
    }

    /// <summary>エクスプローラーの窓 1 つ（Win11 のタブは 1 タブ 1 件。同じ窓の番号になる）</summary>
    internal sealed class ExplorerWindow
    {
        public ExplorerWindow(string path, IntPtr handle, object com)
        {
            NormalizedPath = WindowFinder.NormalizePath(path);
            Handle = handle;
            Com = com;
        }

        /// <summary>開いているフォルダ（比べるためにそろえた形）</summary>
        public string NormalizedPath { get; private set; }

        /// <summary>窓の番号</summary>
        public IntPtr Handle { get; private set; }

        /// <summary>COM の窓の物（閉じる時に使う）</summary>
        public dynamic Com { get; private set; }

        /// <summary>COM の物を手放す。</summary>
        public void Release()
        {
            object com = Com;
            Com = null;
            WindowFinder.ReleaseCom(com);
        }
    }

    /// <summary>
    /// ある時点の「開いている物」の写し。一覧の印を付ける時に、プロセスを何度も調べないために使う。
    /// </summary>
    internal sealed class OpenSnapshot
    {
        private readonly HashSet<string> _folders = new HashSet<string>();
        private readonly HashSet<string> _exePaths = new HashSet<string>();
        private readonly List<string> _titles = new List<string>();

        /// <summary>今の状態を調べて写しを作る。</summary>
        public static OpenSnapshot Take()
        {
            OpenSnapshot snapshot = new OpenSnapshot();
            foreach (ExplorerWindow window in WindowFinder.GetExplorerWindows())
            {
                snapshot._folders.Add(window.NormalizedPath);
                window.Release();
            }
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        snapshot._exePaths.Add(WindowFinder.NormalizePath(WindowFinder.ProcessPath(process.Id)));
                    }
                }
                catch (Exception)
                {
                    // 終わりかけのプロセスは飛ばす
                }
                finally
                {
                    process.Dispose();
                }
            }
            foreach (KeyValuePair<IntPtr, string> window in WindowFinder.VisibleWindowTitles())
            {
                snapshot._titles.Add(window.Value);
            }
            return snapshot;
        }

        /// <summary>登録した物が開いているか。true＝開いている</summary>
        public bool IsOpen(Entry entry)
        {
            if (entry.Kind == EntryKinds.Folder)
            {
                return _folders.Contains(WindowFinder.NormalizePath(entry.Path));
            }
            if (WindowFinder.IsExe(entry.Path))
            {
                return _exePaths.Contains(WindowFinder.NormalizePath(entry.Path));
            }
            string fileName = Path.GetFileName(entry.Path ?? string.Empty);
            if (fileName.Length == 0)
            {
                return false;
            }
            foreach (string title in _titles)
            {
                if (title.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
