using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PinFolder
{
    /// <summary>
    /// 生成 AI に渡すログ（共通ルール LOGGING.md の形）。
    /// 起動から終了までの 1 回 ＝ 1 ファイルで、exe の横の to_ai_log に書く。12 か月を過ぎたファイルは起動時に消す。
    /// </summary>
    internal static class AppLog
    {
        private const string LogFolderName = "to_ai_log";
        private const string FilePrefix = "pinfolder_resident_";
        private const string FileExtension = ".md";
        private const string FileDateFormat = "yyyyMMdd_HHmmss";
        private const string LineTimeFormat = "yyyy-MM-dd HH:mm:ss";
        private const int KeepMonths = 12;
        private const int StackLines = 4;

        private static readonly object Sync = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static string _path;
        private static DateTime _startedAt;
        private static int _sequence;
        private static bool _ended;

        /// <summary>ログのフォルダ（exe の横の to_ai_log）</summary>
        public static string FolderPath
        {
            get { return Path.Combine(AppPaths.ExeDir, LogFolderName); }
        }

        /// <summary>この起動のログのファイル（作れなかった時は空）</summary>
        public static string FilePath
        {
            get { return _path ?? string.Empty; }
        }

        /// <summary>起動時に呼ぶ。ファイルを作って読み方を書き、古いファイルを消す。</summary>
        public static void Start()
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                _startedAt = DateTime.Now;
                string baseName = FilePrefix + _startedAt.ToString(FileDateFormat, CultureInfo.InvariantCulture);
                string path = Path.Combine(FolderPath, baseName + FileExtension);
                int suffix = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(FolderPath, baseName + "_" + suffix + FileExtension);
                    suffix++;
                }
                _path = path;
                Append(BuildHeader());
                Info("起動", AppPaths.AppName, "起動した（版 " + AppPaths.Version + "、利用者 " + Environment.UserName
                    + "、OS " + Environment.OSVersion.VersionString + "、場所 " + AppPaths.ExeDir + "）");
            }
            catch (Exception ex)
            {
                // ログが書けなくても本来の処理は止めない
                Debug.WriteLine("ログを作れませんでした: " + ex);
                _path = null;
                return;
            }
            try
            {
                DeleteOldFiles();
            }
            catch (Exception ex)
            {
                Warn("古いログの削除", FolderPath, ex.GetType().FullName + "：" + ex.Message, "対処は要りません（古いログが残るだけです）");
            }
        }

        /// <summary>正常に済んだ操作を 1 行書く。</summary>
        /// <param name="operation">操作（例：開く）</param>
        /// <param name="target">対象（例：標準化 / INPUT）</param>
        /// <param name="detail">結果の中身</param>
        public static void Info(string operation, string target, string detail)
        {
            WriteLine("正常", NextReference(), operation, target, detail);
        }

        /// <summary>止めはしないが、利用者の対処が要る出来事を書く（1 行と異常の節）。</summary>
        /// <param name="operation">操作</param>
        /// <param name="target">対象</param>
        /// <param name="reason">なぜ（エラーの番号と文）</param>
        /// <param name="next">次に何をするか</param>
        public static void Warn(string operation, string target, string reason, string next)
        {
            string reference = NextReference();
            WriteLine("警告", reference, operation, target, reason);
            WriteSection(reference, operation, target, "PinFolder の " + operation, reason, next);
        }

        /// <summary>誤り（例外）を書く（1 行と異常の節）。</summary>
        /// <param name="operation">操作</param>
        /// <param name="target">対象</param>
        /// <param name="ex">起きた例外（無い時は null）</param>
        /// <param name="next">次に何をするか</param>
        public static void Error(string operation, string target, Exception ex, string next)
        {
            string reference = NextReference();
            string reason = ex == null ? "例外の中身が取れなかった" : ex.GetType().FullName + "：" + ex.Message;
            string where = "PinFolder の " + operation;
            if (ex != null)
            {
                where += "（" + StackTop(ex) + "）";
            }
            WriteLine("異常", reference, operation, target, reason);
            WriteSection(reference, operation, target, where, reason, next);
        }

        /// <summary>終了時に呼ぶ。結果の 1 行を書く。</summary>
        /// <param name="normal">true＝正常に終わった、false＝誤りで終わった</param>
        public static void End(bool normal)
        {
            lock (Sync)
            {
                if (_ended)
                {
                    return;
                }
                _ended = true;
            }
            TimeSpan elapsed = DateTime.Now - _startedAt;
            Info("終了", AppPaths.AppName, "常駐を終えた（起動から " + (int)elapsed.TotalMinutes + " 分）");
            Append("\n結果：" + (normal ? "正常終了" : "異常終了（止まった所は上の「### 異常」の節）") + "\n");
        }

        private static string NextReference()
        {
            lock (Sync)
            {
                _sequence++;
                return "P-" + _sequence.ToString("0000", CultureInfo.InvariantCulture);
            }
        }

        private static void WriteLine(string result, string reference, string operation, string target, string detail)
        {
            string line = "- " + DateTime.Now.ToString(LineTimeFormat, CultureInfo.InvariantCulture)
                + "｜" + result + "｜" + reference + "｜" + Cell(operation) + "｜" + Cell(target) + "｜" + Cell(detail) + "\n";
            Append(line);
        }

        private static void WriteSection(string reference, string operation, string target, string where, string reason, string next)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("\n### 異常 ").Append(reference).Append("（").Append(DateTime.Now.ToString(LineTimeFormat, CultureInfo.InvariantCulture)).Append("）\n\n");
            sb.Append("| 項目 | 内容 |\n|---|---|\n");
            sb.Append("| 何が | ").Append(Cell(operation)).Append(" |\n");
            sb.Append("| いつ | ").Append(DateTime.Now.ToString(LineTimeFormat, CultureInfo.InvariantCulture)).Append(" |\n");
            sb.Append("| どこで | PC ").Append(Cell(Environment.MachineName)).Append("、").Append(Cell(where)).Append(" |\n");
            sb.Append("| 何に対して | ").Append(Cell(target)).Append(" |\n");
            sb.Append("| なぜ | ").Append(Cell(reason)).Append(" |\n");
            sb.Append("| 次に何をするか | ").Append(Cell(next)).Append(" |\n");
            sb.Append("| 参照番号 | ").Append(reference).Append(" |\n\n");
            Append(sb.ToString());
        }

        private static string StackTop(Exception ex)
        {
            if (string.IsNullOrEmpty(ex.StackTrace))
            {
                return "場所の情報なし";
            }
            string[] lines = ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Length && i < StackLines; i++)
            {
                if (i > 0)
                {
                    sb.Append(" / ");
                }
                sb.Append(lines[i].Trim());
            }
            return sb.ToString();
        }

        /// <summary>表のセルに入れられる形にする（| と改行を置き換える）</summary>
        private static string Cell(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（なし）";
            }
            return text.Replace("|", "｜").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
        }

        private static void Append(string text)
        {
            if (_path == null)
            {
                return;
            }
            lock (Sync)
            {
                try
                {
                    File.AppendAllText(_path, text, Utf8NoBom);
                }
                catch (Exception ex)
                {
                    // ログが書けなくても本来の処理は止めない
                    Debug.WriteLine("ログを書けませんでした: " + ex);
                }
            }
        }

        private static void DeleteOldFiles()
        {
            DateTime limit = DateTime.Now.AddMonths(-KeepMonths);
            int deleted = 0;
            foreach (string file in Directory.GetFiles(FolderPath, FilePrefix + "*" + FileExtension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length < FilePrefix.Length + FileDateFormat.Length)
                {
                    continue;
                }
                string datePart = name.Substring(FilePrefix.Length, FileDateFormat.Length);
                DateTime fileDate;
                if (!DateTime.TryParseExact(datePart, FileDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out fileDate))
                {
                    continue;
                }
                if (fileDate >= limit)
                {
                    continue;
                }
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Warn("古いログの削除", Path.GetFileName(file), ex.GetType().FullName + "：" + ex.Message, "ファイルが開かれていないかを確かめてください。消せなくても PinFolder の動きには影響しません");
                }
            }
            if (deleted > 0)
            {
                Info("古いログの削除", FolderPath, KeepMonths + " か月を過ぎたファイルを " + deleted + " 件消した");
            }
        }

        private static string BuildHeader()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# PinFolder 常駐 のログ（").Append(_startedAt.ToString(LineTimeFormat, CultureInfo.InvariantCulture)).Append(" に開始）\n\n");
            sb.Append("このファイルは、PinFolder（通知領域に常駐し、登録したフォルダ・アプリを開く道具）の起動から終了までの記録と、異常の詳細です。生成 AI に渡して、原因と対処を尋ねるために出しています。\n\n");
            sb.Append("## 読み方\n");
            sb.Append("- 「- 」で始まる 1 行が、1 つの操作です。項目は「時刻｜結果｜参照番号｜操作｜対象｜中身」の順です\n");
            sb.Append("- 結果は「正常」「警告」「異常」のどれかです。「警告」「異常」の行は、後ろに「### 異常 <参照番号>」の節があり、詳しい中身があります\n");
            sb.Append("- 最後の行の「結果：」が、常駐の終わり方です。「正常終了」はメニューの［終了］で終えた、「異常終了」は想定外の誤りで止まった、です（行が無い時は、まだ動いているか、Windows の終了・タスクマネージャーで止められました）\n");
            sb.Append("- 設定は exe の横の config.json、このログは exe の横の to_ai_log にあります\n\n");
            sb.Append("## よくある原因と対処\n");
            sb.Append("| 症状 | 原因の候補 | 対処 |\n|---|---|---|\n");
            sb.Append("| フォルダがありません | フォルダの名前が変わった・消えた、ネットワークのドライブにつながっていない | エクスプローラーでそのフォルダを開けるか確かめ、設定画面の「登録」で場所を直す |\n");
            sb.Append("| ファイルがありません | アプリの入れ直しで場所が変わった | 設定画面の「登録」で場所を直す |\n");
            sb.Append("| キーが使えませんでした | ほかのアプリが同じキーを使っている | 設定画面の「キー」で別のキーにする |\n");
            sb.Append("| 設定を読めません | config.json の中身が崩れた（手で直した、書き込みの途中で止まった） | 控え（config.json.<日時>.bak）を開発の担当へ渡し、設定画面で登録し直す |\n");
            sb.Append("| 開けませんでした | アプリが起動できない（権限・壊れた exe・関連付けの無いファイル） | そのアプリをエクスプローラーから直接開けるか確かめる |\n\n");
            sb.Append("## 記録\n\n");
            return sb.ToString();
        }
    }
}
