using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PinFolder
{
    /// <summary>開いた結果</summary>
    internal enum OpenResult
    {
        /// <summary>新しく開いた</summary>
        Opened,

        /// <summary>既に開いていた窓を前面に出した</summary>
        Activated,

        /// <summary>開けなかった</summary>
        Failed
    }

    /// <summary>
    /// 登録した物を開く。既に開いていれば、新しく開かずに前面に出す。
    /// </summary>
    internal static class Launcher
    {
        /// <summary>登録した物を開く（または前面に出す）。</summary>
        /// <param name="jobName">グループの名前（ログに書く）</param>
        /// <param name="entry">開く物</param>
        /// <param name="message">開けなかった時に利用者に知らせる文（開けた時は null）</param>
        public static OpenResult Open(string jobName, Entry entry, out string message)
        {
            message = null;
            string target = jobName + " / " + entry.Name;
            try
            {
                if (entry.Kind == EntryKinds.Folder)
                {
                    return OpenFolder(target, entry, out message);
                }
                return OpenApp(target, entry, out message);
            }
            catch (Win32Exception ex)
            {
                message = Lang.F("開けませんでした：{0}", entry.Name);
                AppLog.Error("開く", target + "（" + entry.Path + "）", ex,
                    "そのアプリ・ファイルをエクスプローラーから直接開けるか確かめてください（権限・関連付け・壊れた exe）");
                return OpenResult.Failed;
            }
            catch (Exception ex)
            {
                message = Lang.F("開けませんでした：{0}", entry.Name);
                AppLog.Error("開く", target + "（" + entry.Path + "）", ex,
                    "想定外の誤りです。このログのファイルを添えて開発の担当へ連絡してください");
                return OpenResult.Failed;
            }
        }

        /// <summary>そのフォルダ・アプリの窓を閉じる。</summary>
        /// <returns>true＝閉じた（閉じる指示を送った）</returns>
        public static bool Close(string jobName, Entry entry)
        {
            string target = jobName + " / " + entry.Name;
            if (entry.Kind == EntryKinds.Folder)
            {
                int closed = WindowFinder.CloseExplorer(entry.Path);
                if (closed > 0)
                {
                    AppLog.Info("閉じる", target, "フォルダの窓を " + closed + " 個閉じた");
                }
                return closed > 0;
            }
            bool sent = WindowFinder.CloseApp(entry.Path);
            if (sent)
            {
                AppLog.Info("閉じる", target, "アプリに閉じる指示を送った（保存の確認はアプリが出す）");
            }
            return sent;
        }

        private static void LogActivate(string target, bool activated, string detail)
        {
            if (activated)
            {
                AppLog.Info("開く", target, detail);
                return;
            }
            AppLog.Warn("開く", target, "既に開いていた窓を前面に出せなかった（Windows の制限。タスクバーのボタンが点滅する）",
                "タスクバーで点滅しているボタンを押してください。続く時は、PinFolder の一覧を開いてからもう一度押してください");
        }

        private static OpenResult OpenFolder(string target, Entry entry, out string message)
        {
            message = null;
            if (!Directory.Exists(entry.Path))
            {
                message = Lang.F("フォルダがありません：{0}", entry.Path);
                AppLog.Warn("開く", target + "（" + entry.Path + "）", "フォルダがありません",
                    "フォルダの名前が変わった・消えた、またはネットワークのドライブにつながっていません。エクスプローラーで開けるか確かめ、設定画面の「登録」で場所を直してください");
                return OpenResult.Failed;
            }
            IntPtr window = WindowFinder.FindExplorer(entry.Path);
            if (window != IntPtr.Zero)
            {
                LogActivate(target, WindowFinder.Activate(window), "既に開いていた窓を前面に出した");
                return OpenResult.Activated;
            }
            using (Process.Start(WindowFinder.ExplorerExe, "\"" + entry.Path + "\""))
            {
            }
            AppLog.Info("開く", target, "フォルダを新しく開いた（" + entry.Path + "）");
            return OpenResult.Opened;
        }

        private static OpenResult OpenApp(string target, Entry entry, out string message)
        {
            message = null;
            if (!File.Exists(entry.Path))
            {
                message = Lang.F("ファイルがありません：{0}", entry.Path);
                AppLog.Warn("開く", target + "（" + entry.Path + "）", "ファイルがありません",
                    "アプリの入れ直しなどで場所が変わった可能性があります。設定画面の「登録」で場所を直してください");
                return OpenResult.Failed;
            }
            IntPtr window = WindowFinder.FindApp(entry.Path);
            if (window != IntPtr.Zero)
            {
                LogActivate(target, WindowFinder.Activate(window), "既に動いていたアプリの窓を前面に出した");
                return OpenResult.Activated;
            }
            ProcessStartInfo info = new ProcessStartInfo(entry.Path);
            info.UseShellExecute = true;
            info.Arguments = entry.Args ?? string.Empty;
            info.WorkingDirectory = Path.GetDirectoryName(entry.Path) ?? string.Empty;
            using (Process.Start(info))
            {
            }
            AppLog.Info("開く", target, "アプリを起動した（" + entry.Path + "）");
            return OpenResult.Opened;
        }
    }
}
