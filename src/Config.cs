using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PinFolder
{
    /// <summary>登録できる物の種類</summary>
    internal static class EntryKinds
    {
        /// <summary>フォルダ（エクスプローラーで開く）</summary>
        public const string Folder = "folder";

        /// <summary>アプリ（EXE やファイル。関連付けで開く）</summary>
        public const string App = "app";

        /// <summary>画面に出す種類の名前</summary>
        public static string Label(string kind)
        {
            return Lang.T(kind == Folder ? "フォルダ" : "アプリ");
        }
    }

    /// <summary>
    /// 設定の全体（exe の横の config.json）。人は直さず、PinFolder の画面だけが読み書きする。古い版の項目（InputFolderName など）は読み飛ばす。
    /// </summary>
    public class AppConfig
    {
        /// <summary>設定ファイルの形の版</summary>
        public const int CurrentVersion = 1;

        /// <summary>浮かぶボタンの位置が、まだ決まっていない印</summary>
        public const int NoPosition = int.MinValue;

        /// <summary>区画の形の初期値（2×3＝横 3 列・縦 2 段）</summary>
        public const string DefaultAreaShape = "2x3";

        /// <summary>手前の窓を左・右・上・下の区画へ移すキーの初期値</summary>
        public const string DefaultMoveLeft = "Ctrl+Win+Alt+Left";
        public const string DefaultMoveRight = "Ctrl+Win+Alt+Right";
        public const string DefaultMoveUp = "Ctrl+Win+Alt+Up";
        public const string DefaultMoveDown = "Ctrl+Win+Alt+Down";

        /// <summary>大きな画面を開くキーの初期値</summary>
        public const string DefaultListHotkey = "Ctrl+Alt+P";

        public AppConfig()
        {
            Version = CurrentVersion;
            Style = Theme.DefaultId;
            Language = Lang.Japanese;
            ListHotkey = DefaultListHotkey;
            FloatX = NoPosition;
            FloatY = NoPosition;
            AreaShape = DefaultAreaShape;
            MoveLeftHotkey = DefaultMoveLeft;
            MoveRightHotkey = DefaultMoveRight;
            MoveUpHotkey = DefaultMoveUp;
            MoveDownHotkey = DefaultMoveDown;
            LayoutSets = new List<LayoutSet>();
            Jobs = new List<Job>();
        }

        /// <summary>設定ファイルの形の版</summary>
        public int Version { get; set; }

        /// <summary>見た目のスタイル（office・steel・memo）</summary>
        public string Style { get; set; }

        /// <summary>大きな画面を開くキー（空なら使わない）</summary>
        public string ListHotkey { get; set; }

        /// <summary>浮かぶボタンの左の位置（画面の座標。NoPosition＝初めの位置＝右下）</summary>
        public int FloatX { get; set; }

        /// <summary>浮かぶボタンの上の位置（画面の座標。NoPosition＝初めの位置＝右下）</summary>
        public int FloatY { get; set; }

        /// <summary>最後に使った区画の形（2x2・2x3・2x4。区画を移るキーもこの形で動く）</summary>
        public string AreaShape { get; set; }

        /// <summary>最後に選んだ区画のセットの名前（無ければ空）</summary>
        public string LastLayoutSet { get; set; }

        /// <summary>手前の窓を左の区画へ移すキー</summary>
        public string MoveLeftHotkey { get; set; }

        /// <summary>手前の窓を右の区画へ移すキー</summary>
        public string MoveRightHotkey { get; set; }

        /// <summary>手前の窓を上の区画へ移すキー</summary>
        public string MoveUpHotkey { get; set; }

        /// <summary>手前の窓を下の区画へ移すキー</summary>
        public string MoveDownHotkey { get; set; }

        /// <summary>丸いボタンの上の配置パターンの □ を出しているか（true＝出す。矢印で切り替える）</summary>
        public bool QuickBarExpanded { get; set; }

        /// <summary>画面の言葉（ja＝日本語・en＝英語・zh＝中国語（簡体））</summary>
        public string Language { get; set; }

        /// <summary>区画のセット（区画の形と、区画ごとの登録）</summary>
        public List<LayoutSet> LayoutSets { get; set; }

        /// <summary>グループの一覧（足した順）</summary>
        public List<Job> Jobs { get; set; }

        /// <summary>読み込んだ後に、欠けた値を初期値で埋める。</summary>
        public void Normalize()
        {
            if (Jobs == null)
            {
                Jobs = new List<Job>();
            }
            if (Theme.Find(Style) == null)
            {
                Style = Theme.DefaultId;
            }

            Language = Lang.Normalize(Language);

            if (ListHotkey == null)
            {
                ListHotkey = DefaultListHotkey;
            }
            if (!AreaShapes.IsValid(AreaShape))
            {
                AreaShape = DefaultAreaShape;
            }
            if (MoveLeftHotkey == null)
            {
                MoveLeftHotkey = DefaultMoveLeft;
            }
            if (MoveRightHotkey == null)
            {
                MoveRightHotkey = DefaultMoveRight;
            }
            if (MoveUpHotkey == null)
            {
                MoveUpHotkey = DefaultMoveUp;
            }
            if (MoveDownHotkey == null)
            {
                MoveDownHotkey = DefaultMoveDown;
            }
            if (LayoutSets == null)
            {
                LayoutSets = new List<LayoutSet>();
            }
            LayoutSets.RemoveAll(set => set == null || string.IsNullOrWhiteSpace(set.Name));
            foreach (LayoutSet set in LayoutSets)
            {
                if (!AreaShapes.IsValid(set.Shape))
                {
                    set.Shape = DefaultAreaShape;
                }
                if (set.Zones == null)
                {
                    set.Zones = new List<string>();
                }

                for (int i = 0; i < set.Zones.Count; i++)
                {
                    if (set.Zones[i] == null)
                    {
                        set.Zones[i] = string.Empty;
                    }
                }
            }
            Jobs.RemoveAll(job => job == null);
            HashSet<string> ids = new HashSet<string>();
            foreach (Job job in Jobs)
            {
                if (job.Entries == null)
                {
                    job.Entries = new List<Entry>();
                }
                job.Entries.RemoveAll(entry => entry == null);
                foreach (Entry entry in job.Entries)
                {
                    if (entry.Kind != EntryKinds.Folder && entry.Kind != EntryKinds.App)
                    {
                        entry.Kind = EntryKinds.Folder;
                    }
                    // 番号が無い・重なっている（設定を写した時など）登録には、新しい番号を振る
                    if (string.IsNullOrEmpty(entry.Id) || !ids.Add(entry.Id))
                    {
                        entry.Id = Entry.NewId();
                        ids.Add(entry.Id);
                    }
                }
            }
        }
    }

    /// <summary>グループ（1 件の案件など）。登録した物をまとめる。</summary>
    public class Job
    {
        public Job()
        {
            Entries = new List<Entry>();
        }

        /// <summary>グループの名前（例：標準化）</summary>
        public string Name { get; set; }


        /// <summary>最後に使った日時（yyyy-MM-ddTHH:mm:ss。一覧の並びに使う）</summary>
        public string LastUsed { get; set; }

        /// <summary>登録した物</summary>
        public List<Entry> Entries { get; set; }

        /// <summary>一覧の部品に出す名前</summary>
        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }

    /// <summary>登録した物（フォルダ 1 つ、またはアプリ 1 つ）</summary>
    public class Entry
    {
        public Entry()
        {
            Id = NewId();
        }

        /// <summary>消えない番号（区画のセットから登録を指す。名前・場所を直しても変わらない）</summary>
        public string Id { get; set; }

        /// <summary>新しい番号を作る。</summary>
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>種類（EntryKinds.Folder・EntryKinds.App）</summary>
        public string Kind { get; set; }

        /// <summary>表示名（例：INPUT）</summary>
        public string Name { get; set; }

        /// <summary>場所（フォルダ・exe・ファイルのフルパス）</summary>
        public string Path { get; set; }

        /// <summary>アプリに渡す引数（フォルダの時は使わない）</summary>
        public string Args { get; set; }

        /// <summary>開くキー（例：Ctrl+Alt+1。空なら無し）</summary>
        public string Hotkey { get; set; }
    }

    /// <summary>区画のセット（名前・区画の形・区画ごとの登録の番号。空いた区画は空の文字）</summary>
    public class LayoutSet
    {

        public LayoutSet()
        {
            Shape = AppConfig.DefaultAreaShape;
            Zones = new List<string>();
        }


        /// <summary>セットの名前（例：朝の作業）</summary>
        public string Name { get; set; }

        /// <summary>区画の形（2x2・2x3・2x4）</summary>
        public string Shape { get; set; }

        /// <summary>区画ごとの登録の番号（左上から右へ、上の段から下の段へ。空いた区画は空の文字）</summary>
        public List<string> Zones { get; set; }

        /// <summary>選択肢に出す名前</summary>
        public override string ToString()
        {
            return Name ?? string.Empty;
        }
    }

    /// <summary>
    /// 並べる先の画面の決め方：丸いボタンのいる画面（2026-11-25 の利用者の指示。画面を選ぶ運用はしない）。
    /// </summary>
    internal static class ScreenTargets
    {
        /// <summary>丸いボタンの位置から、並べる先の作業領域を返す（位置が決まっていない・画面が無い時は主の画面）。</summary>
        /// <param name="buttonX">丸いボタンの左の位置</param>
        /// <param name="buttonY">丸いボタンの上の位置</param>
        public static Rectangle ForButton(int buttonX, int buttonY)
        {
            if (buttonX == AppConfig.NoPosition || buttonY == AppConfig.NoPosition)
            {
                return Screen.PrimaryScreen.WorkingArea;
            }
            return Screen.FromPoint(new Point(buttonX, buttonY)).WorkingArea;
        }
    }
    /// <summary>区画の形（2×2・2×3・2×4。縦は 2 段、横は 2〜4 列。2×3＝横 3 列・縦 2 段）</summary>
    internal static class AreaShapes
    {
        /// <summary>選べる形（画面の並び）</summary>
        public static readonly string[] All = { "2x2", "2x3", "2x4" };

        /// <summary>縦の段の数（どの形も 2 段）</summary>
        public const int Rows = 2;

        /// <summary>選べる形か。true＝選べる</summary>
        public static bool IsValid(string shape)
        {
            return Array.IndexOf(All, shape) >= 0;
        }

        /// <summary>横の列の数（2x3 なら 3）</summary>
        public static int Columns(string shape)
        {
            return IsValid(shape) ? shape[2] - '0' : 3;
        }

        /// <summary>区画の数</summary>
        public static int Count(string shape)
        {
            return Columns(shape) * Rows;
        }

        /// <summary>画面に出す名前（2×3 など）</summary>
        public static string Label(string shape)
        {
            return shape.Replace("x", "×");
        }
    }

    /// <summary>
    /// config.json の読み書き。書く時は一時ファイルに書いてから置き換え、途中で止まっても壊れないようにする。
    /// </summary>
    internal static class ConfigStore
    {
        private const string FileName = "config.json";
        private const string TempSuffix = ".tmp";
        private const string BackupDateFormat = "yyyyMMdd_HHmmss";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>壊れた設定の控えを作れなかった時、上書きを止める</summary>
        private static bool _saveBlocked;

        /// <summary>上書きを止めたことを、ログに 1 回だけ書くための印</summary>
        private static bool _saveBlockedLogged;

        /// <summary>設定ファイルの場所（exe の横）</summary>
        public static string FilePath
        {
            get { return System.IO.Path.Combine(AppPaths.ExeDir, FileName); }
        }

        /// <summary>
        /// 設定を読む。無ければ新しく作り、読めなければ控えを残して新しい設定で動く。
        /// </summary>
        /// <param name="warning">利用者に知らせる文（問題が無ければ null）</param>
        public static AppConfig Load(out string warning)
        {
            warning = null;
            string path = FilePath;
            if (!File.Exists(path))
            {
                AppConfig created = new AppConfig();
                AppLog.Info("設定の読み込み", path, "設定ファイルが無いため、新しく作る");
                string error;
                Save(created, out error);
                return created;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                AppConfig config = new JavaScriptSerializer().Deserialize<AppConfig>(json);
                if (config == null)
                {
                    throw new InvalidDataException("設定ファイルの中身が空です");
                }
                config.Normalize();
                AppLog.Info("設定の読み込み", path, "グループ " + config.Jobs.Count + " 件を読んだ");
                return config;
            }
            catch (Exception ex)
            {
                string backup = path + "." + DateTime.Now.ToString(BackupDateFormat) + ".bak";
                try
                {
                    File.Copy(path, backup, false);
                }
                catch (Exception copyEx)
                {
                    AppLog.Error("設定の控え", backup, copyEx, "config.json を手で別の場所へ写してから、PinFolder を起動し直してください");
                    backup = null;
                    _saveBlocked = true;
                }
                AppLog.Error("設定の読み込み", path, ex,
                    "設定ファイルの中身が崩れています。控え（" + (backup ?? "作れなかった") + "）を開発の担当へ渡し、設定画面で登録し直してください");
                warning = "設定を読めません。新しい設定で動きます";
                return new AppConfig();
            }
        }

        /// <summary>設定を書く。</summary>
        /// <param name="config">書く設定</param>
        /// <param name="error">書けなかった時の理由（書けた時は null）</param>
        /// <returns>true＝書けた</returns>
        public static bool Save(AppConfig config, out string error)
        {
            error = null;
            string path = FilePath;
            if (_saveBlocked)
            {
                error = "壊れた設定の控えを作れなかったため、上書きを止めています";
                if (!_saveBlockedLogged)
                {
                    _saveBlockedLogged = true;
                    AppLog.Warn("設定の保存", path, error, "config.json を手で別の場所へ写してから、PinFolder を起動し直してください");
                }
                return false;
            }
            string temp = path + TempSuffix;
            try
            {
                string json = new JavaScriptSerializer().Serialize(config);
                File.WriteAllText(temp, json, Utf8NoBom);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLog.Error("設定の保存", path, ex, "exe のフォルダに書き込めるか（読み取り専用・権限）を確かめてください");
                return false;
            }
        }

        /// <summary>設定画面で直すための写しを作る（キャンセルした時に元を変えないため）。</summary>
        public static AppConfig Clone(AppConfig config)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            AppConfig copy = serializer.Deserialize<AppConfig>(serializer.Serialize(config));
            copy.Normalize();
            return copy;
        }
    }
}
