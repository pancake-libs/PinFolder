using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PinFolder
{
    /// <summary>
    /// 画面の言葉の切り替え（日本語・英語・中国語（簡体）。2026-11-25 の利用者の指示）。
    /// 画面の文字は日本語の文をそのまま鍵にして引く（Lang.T("保存")）。辞書に無い文は日本語のまま出す。
    /// 差し込みのある文は {0} の形で書き、Lang.F で埋める。ログ（to_ai_log）は生成 AI に渡すため、日本語のまま変えない。
    /// 訳は Claude が作り、Gemini の第二意見（G-0011）で見直した。
    /// </summary>
    internal static class Lang
    {
        /// <summary>日本語（初期の言語）</summary>
        public const string Japanese = "ja";

        /// <summary>英語</summary>
        public const string English = "en";

        /// <summary>中国語（簡体）</summary>
        public const string Chinese = "zh";

        /// <summary>設定画面の選択肢に出す並び（言語の ID）</summary>
        public static readonly string[] Ids = { Japanese, English, Chinese };

        /// <summary>config.json から言語だけを読む形（"Language": "en"）</summary>
        private const string LanguagePattern = "\"Language\"\\s*:\\s*\"([a-z]+)\"";

        private const int EnglishColumn = 0;
        private const int ChineseColumn = 1;

        private static readonly Dictionary<string, string[]> Table = BuildTable();
        private static string _current = Japanese;

        /// <summary>今の言語の ID</summary>
        public static string Current
        {
            get { return _current; }
        }

        /// <summary>言語を切り替える（知らない ID は日本語）。</summary>
        public static void Set(string id)
        {
            _current = Normalize(id);
        }

        /// <summary>言語の ID を、使える ID にそろえる（知らない ID・空は日本語）。</summary>
        public static string Normalize(string id)
        {
            return id == English || id == Chinese ? id : Japanese;
        }

        /// <summary>選択肢に出す言語の名前（その言語で書く。どの言語の画面からでも選べるように）</summary>
        public static string DisplayName(string id)
        {
            switch (Normalize(id))
            {
                case English: return "English";
                case Chinese: return "中文（简体）";
                default: return "日本語";
            }
        }

        /// <summary>画面の文字に使う書体（その言語の字形が揃っている Windows 標準の書体）</summary>
        public static string FontName
        {
            get
            {
                switch (_current)
                {
                    case English: return "Segoe UI";
                    case Chinese: return "Microsoft YaHei UI";
                    default: return "Yu Gothic UI";
                }
            }
        }

        /// <summary>並べる時の区切り（日本語・中国語は「、」、英語は「, 」）</summary>
        public static string ListSeparator
        {
            get { return _current == English ? ", " : "、"; }
        }

        /// <summary>日本語の文を、今の言語の文にする。</summary>
        public static string T(string japanese)
        {
            if (_current == Japanese || japanese == null)
            {
                return japanese;
            }
            string[] row;
            if (!Table.TryGetValue(japanese, out row))
            {
                return japanese;
            }
            return row[_current == English ? EnglishColumn : ChineseColumn];
        }

        /// <summary>差し込みのある文（{0} など）を、今の言語の文にして埋める。</summary>
        public static string F(string japaneseFormat, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, T(japaneseFormat), args);
        }

        /// <summary>
        /// 起動の初め（設定を読む前）に、config.json の言語だけを読む（「既に起動しています」などを、その言語で出すため）。
        /// 読めない時は日本語のまま（ログは出さない。設定の読み込みで改めて記録する）。
        /// </summary>
        public static void LoadEarly(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return;
                }
                Match match = Regex.Match(File.ReadAllText(configPath, Encoding.UTF8), LanguagePattern);
                if (match.Success)
                {
                    Set(match.Groups[1].Value);
                }
            }
            catch (Exception)
            {
                // ログを始める前のため記録はせず、日本語のまま続ける（設定の読み込みで、誤りをログに残す）
            }
        }

        private static Dictionary<string, string[]> BuildTable()
        {
            Dictionary<string, string[]> t = new Dictionary<string, string[]>(StringComparer.Ordinal);
            // ---- 大きな画面 ----
            Add(t, "設定", "Settings", "设置");
            Add(t, "グループ", "Groups", "分组");
            Add(t, "＋ グループを追加", "+ Add group", "+ 添加分组");
            Add(t, "＋ フォルダを追加", "+ Add folder", "+ 添加文件夹");
            Add(t, "＋ アプリを追加", "+ Add app", "+ 添加应用");
            Add(t, "＋ フォルダ・EXE をドロップ", "+ Drop folders or EXEs", "+ 拖入文件夹或 EXE");
            Add(t, "すべて開く", "Open all", "全部打开");
            Add(t, "すべて閉じる", "Close all", "全部关闭");
            Add(t, "名前を変える", "Rename", "重命名");
            Add(t, "消す", "Delete", "删除");
            Add(t, "編集", "Edit", "编辑");
            // グループの一覧の右の件数（幅が狭いため、英語は数だけ）
            Add(t, "{0} 件", "{0}", "{0} 项");
            Add(t, "「{0}」を足しました", "Added \"{0}\"", "已添加“{0}”");
            Add(t, "「{0}」と、その登録をすべて消しますか。", "Delete \"{0}\" and all of its items?", "要删除“{0}”及其全部条目吗？");
            Add(t, "「{0}」を消しました", "Deleted \"{0}\"", "已删除“{0}”");
            Add(t, "グループがありません", "No groups", "暂无分组");
            Add(t, "● 開いている", "● Open", "● 已打开");
            Add(t, "アプリ (*.exe)|*.exe|すべてのファイル (*.*)|*.*", "Apps (*.exe)|*.exe|All files (*.*)|*.*", "应用 (*.exe)|*.exe|所有文件 (*.*)|*.*");
            Add(t, "フォルダ・ファイルではないため、登録できませんでした", "Only folders and files can be added", "只能添加文件夹或文件");
            Add(t, "もう登録してあります", "Already added", "已添加过");
            Add(t, "「{0}」に {1} 件を登録しました", "Added {1} item(s) to \"{0}\"", "已向“{0}”添加 {1} 项");
            Add(t, "「{0}」の登録を消しますか（フォルダ・ファイルそのものは消しません）。", "Remove \"{0}\" from the list? (The folder or file itself will not be deleted.)", "要从列表中移除“{0}”吗？（不会删除文件夹或文件本身。）");
            Add(t, "「{0}」の窓を閉じました", "Closed windows for \"{0}\"", "已关闭“{0}”的窗口");
            // ---- 区画パネル ----
            Add(t, "PinFolder 区画", "PinFolder Layout", "PinFolder 布局");
            Add(t, "（新しいセット）", "(New layout)", "（新布局）");
            Add(t, "空き", "Empty", "空");
            Add(t, "セット", "Layout", "布局");
            Add(t, "保存", "Save", "保存");
            Add(t, "別名で保存", "Save as", "另存为");
            Add(t, "形", "Grid", "网格");
            Add(t, "配置を適用", "Arrange", "应用布局");
            Add(t, "一式を閉じる", "Close windows", "关闭布局窗口");
            Add(t, "登録した物", "Items", "已添加条目");
            Add(t, "終了", "Exit", "退出");
            Add(t, "空にする", "Clear", "清空");
            Add(t, "空いた区画がありません", "No empty cells", "没有空区域");
            Add(t, "もう区画に入っています", "Already in a cell", "已在区域中");
            Add(t, "セットの名前", "Layout name", "布局名称");
            Add(t, "{0} の写し", "Copy of {0}", "{0} 副本");
            Add(t, "同じ名前のセット「{0}」があります。上書きしますか。", "A layout named \"{0}\" already exists. Overwrite it?", "已有同名布局“{0}”。要覆盖吗？");
            Add(t, "セット「{0}」を保存しました", "Saved layout \"{0}\"", "已保存布局“{0}”");
            Add(t, "セット「{0}」を消しますか（フォルダ・アプリの登録は消しません）。", "Delete layout \"{0}\"? (Folders and apps will stay in the list.)", "要删除布局“{0}”吗？（不会移除已添加的文件夹和应用。）");
            Add(t, "セット「{0}」を消しました", "Deleted layout \"{0}\"", "已删除布局“{0}”");
            Add(t, "区画に登録した物を入れてください", "Add items to the cells", "请将条目放入区域");
            Add(t, "閉じる窓がありませんでした", "No windows to close", "没有可关闭的窗口");
            Add(t, "{0} 件の窓を閉じました", "Closed {0} window(s)", "已关闭 {0} 个窗口");
            // ---- 設定 ----
            Add(t, "PinFolder 設定", "PinFolder Settings", "PinFolder 设置");
            Add(t, "キー", "Shortcut", "快捷键");
            Add(t, "全般", "General", "常规");
            Add(t, "対象", "Action", "操作");
            Add(t, "状態", "Status", "状态");
            Add(t, "使えません", "Unavailable", "不可用");
            Add(t, "なし", "None", "无");
            Add(t, "設定する", "Set", "设置");
            Add(t, "外す", "Clear", "清除");
            Add(t, "PinFolder の画面を開く", "Open PinFolder window", "打开 PinFolder 窗口");
            Add(t, "左の区画へ移す", "Move to left cell", "移到左侧区域");
            Add(t, "右の区画へ移す", "Move to right cell", "移到右侧区域");
            Add(t, "上の区画へ移す", "Move to upper cell", "移到上方区域");
            Add(t, "下の区画へ移す", "Move to lower cell", "移到下方区域");
            Add(t, "Windows の起動時に常駐する", "Start with Windows", "开机时自动启动");
            Add(t, "見た目", "Appearance", "外观");
            Add(t, "言語", "Language", "语言");
            Add(t, "ログの場所", "Log folder", "日志文件夹");
            Add(t, "開く", "Open", "打开");
            Add(t, "キャンセル", "Cancel", "取消");
            Add(t, "対象を選んでください", "Select an action first", "请先选择操作");
            Add(t, "ログの場所を開けませんでした", "Could not open the log folder", "无法打开日志文件夹");
            Add(t, "同じキーが 2 つあります：{0}", "Duplicate shortcut: {0}", "快捷键重复：{0}");
            Add(t, "標準オフィス", "Standard Office", "标准办公");
            Add(t, "スチール道具箱", "Steel Toolbox", "钢制工具箱");
            Add(t, "コンパクト卓上メモ", "Compact Desk Memo", "紧凑桌面便签");
            // ---- 登録の編集・名前 ----
            Add(t, "登録の編集", "Edit item", "编辑条目");
            Add(t, "種類", "Type", "类型");
            Add(t, "フォルダ", "Folder", "文件夹");
            Add(t, "アプリ", "App", "应用");
            Add(t, "表示名", "Display name", "显示名称");
            Add(t, "場所", "Location", "位置");
            Add(t, "参照", "Browse", "浏览");
            Add(t, "引数", "Arguments", "参数");
            Add(t, "ホットキー", "Shortcut", "快捷键");
            Add(t, "表示名を入れてください", "Enter a display name", "请输入显示名称");
            Add(t, "フォルダがありません", "Folder not found", "找不到文件夹");
            Add(t, "ファイルがありません", "File not found", "找不到文件");
            Add(t, "グループの追加", "Add group", "添加分组");
            Add(t, "名前の変更", "Rename", "重命名");
            Add(t, "名前", "Name", "名称");
            Add(t, "名前を入れてください", "Enter a name", "请输入名称");
            // ---- 知らせ ----
            Add(t, "{0} ほか {1} 件", "{0} and {1} more", "{0} 及另外 {1} 项");
            Add(t, "起動時の常駐を切り替えられませんでした", "Could not change the startup setting", "无法更改开机启动设置");
            Add(t, "配置を適用できませんでした", "Could not apply the layout", "无法应用布局");
            Add(t, "配置を適用しました（{0} 件）。開けなかった物：{1}", "Layout applied ({0} item(s)). Could not open: {1}", "已应用布局（{0} 项）。无法打开：{1}");
            Add(t, "PinFolder を終了しますか。", "Exit PinFolder?", "要退出 PinFolder 吗？");
            Add(t, "{0} は使えませんでした", "Could not use shortcut: {0}", "快捷键 {0} 无法使用");
            Add(t, "{0} ほか {1} 件のキーは使えませんでした", "Could not use shortcuts: {0} and {1} more", "快捷键 {0} 及另外 {1} 个无法使用");
            Add(t, "設定を保存できませんでした", "Could not save the settings", "无法保存设置");
            Add(t, "設定を読めません。新しい設定で動きます", "Could not read the settings. Starting with new settings.", "无法读取设置，将使用新设置运行。");
            Add(t, "配置パターンを隠す", "Hide layout buttons", "隐藏布局按钮");
            Add(t, "配置パターンを出す", "Show layout buttons", "显示布局按钮");
            Add(t, "開けませんでした：{0}", "Could not open: {0}", "无法打开：{0}");
            Add(t, "フォルダがありません：{0}", "Folder not found: {0}", "找不到文件夹：{0}");
            Add(t, "ファイルがありません：{0}", "File not found: {0}", "找不到文件：{0}");
            Add(t, "PinFolder は既に起動しています。", "PinFolder is already running.", "PinFolder 已在运行。");
            Add(t, "PinFolder を続けられませんでした。ログ：{0}", "PinFolder could not continue. Log: {0}", "PinFolder 无法继续运行。日志：{0}");
            return t;
        }

        private static void Add(Dictionary<string, string[]> table, string japanese, string english, string chinese)
        {
            table[japanese] = new[] { english, chinese };
        }
    }
}
