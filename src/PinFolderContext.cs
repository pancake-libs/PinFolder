using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 常駐の本体。浮かぶボタン・大きな画面・キー・設定・知らせをまとめる。
    /// 左クリックで大きな画面、右クリックで区画パネル。
    /// </summary>
    internal sealed class PinFolderContext : ApplicationContext
    {
        private readonly FloatButton _button;
        private readonly QuickSetBar _quick;
        private AreaPanel _area;
        private readonly HotkeyWindow _hotkeys;
        private readonly HashSet<string> _failedHotkeys = new HashSet<string>();
        private MainForm _main;
        private AppConfig _config;
        private Theme _theme;
        private SettingsForm _settings;
        private ToastForm _toast;

        /// <summary>true＝終了の途中（設定画面を閉じた時にキーを登録し直さない）</summary>
        private bool _exiting;

        /// <summary>true＝並べている途中（窓が出るのを待つ間に、別の操作が割り込まないようにする）</summary>
        private bool _arranging;

        /// <summary>true＝並べている途中に終了を頼まれた（並べ終えてから終える）</summary>
        private bool _exitRequested;

        public PinFolderContext()
        {
            string warning;
            _config = ConfigStore.Load(out warning);
            Lang.Set(_config.Language);
            _theme = Theme.Get(_config.Style);


            _button = new FloatButton(_config.FloatX, _config.FloatY);
            _button.LeftClicked += (s, e) => ToggleMain();
            _button.RightClicked += (s, e) => ToggleArea();
            _button.Moved += (s, e) =>
            {
                _config.FloatX = _button.Left;
                _config.FloatY = _button.Top;
                if (_quick != null)
                {
                    _quick.PlaceAbove(_button.Bounds);
                }
                SaveConfig();
            };
            _button.Show();

            // 丸いボタンの上の、配置パターンの □（セットの 1〜12 番目）
            _quick = new QuickSetBar(_config.QuickBarExpanded);
            _quick.PatternClicked += (s, index) => ApplySet(index);
            _quick.PatternRightClicked += (s, index) => CloseSet(index);
            _quick.ExpandedChanged += (s, expanded) =>
            {
                _config.QuickBarExpanded = expanded;
                SaveConfig();
            };
            _quick.ShowSets(_config);
            _quick.PlaceAbove(_button.Bounds);
            _quick.Show();

            _main = new MainForm(this, _theme);
            _area = new AreaPanel(this, _config, _theme);
            _hotkeys = new HotkeyWindow();
            ApplyHotkeys();

            if (warning != null)
            {
                Notify(Lang.T(warning), true);
            }
        }

        /// <summary>今の設定（大きな画面が直接直し、直したら ConfigChanged を呼ぶ）</summary>
        public AppConfig Config
        {
            get { return _config; }
        }

        /// <summary>グループを、最後に使った順（使っていない物は足した順）で返す。</summary>
        public List<Job> JobsByRecent()
        {
            // OrderByDescending は安定な並べ替え（同じ日時なら足した順を保つ）
            return _config.Jobs.OrderByDescending(job => job.LastUsed ?? string.Empty, StringComparer.Ordinal).ToList();
        }

        /// <summary>大きな画面で登録を直した後に呼ぶ（保存し、キーを登録し直す）。</summary>
        public void ConfigChanged()
        {
            SaveConfig();
            ApplyHotkeys();
            // 区画パネルの「登録した物」と、丸いボタンの上の □ も新しくする（区画の編集中の割り当ては残す）
            if (_area != null)
            {
                _area.RefreshEntries();
            }
            if (_quick != null)
            {
                _quick.ShowSets(_config);
            }
        }

        /// <summary>登録した物を 1 つ開く（開いていれば前面に出す）。</summary>
        /// <returns>true＝開いた・前面に出した</returns>
        public bool OpenEntry(Job job, Entry entry)
        {
            if (_arranging)
            {
                // 配置を適用している間に窓が増えると、並べ方が乱れるため
                return false;
            }
            string message;
            if (Launcher.Open(job.Name, entry, out message) == OpenResult.Failed)
            {
                Notify(message, true);
                return false;
            }
            Touch(job);
            // 丸いボタンの窓の絵を、磁石に吸い付かせて見せる
            _button.PlaySnap();
            return true;
        }

        /// <summary>選んだグループの登録をすべて開く。</summary>
        public void OpenJobs(List<Job> jobs)
        {
            if (_arranging)
            {
                return;
            }
            List<string> failed = new List<string>();
            foreach (Job job in jobs)
            {
                foreach (Entry entry in job.Entries)
                {
                    string message;
                    if (Launcher.Open(job.Name, entry, out message) == OpenResult.Failed)
                    {
                        failed.Add(message);
                    }
                }
                Touch(job);
            }
            if (failed.Count > 0)
            {
                Notify(failed.Count == 1 ? failed[0] : Lang.F("{0} ほか {1} 件", failed[0], failed.Count - 1), true);
            }
        }

        /// <summary>選んだグループの登録の窓を閉じる（ほかの窓は閉じない）。</summary>
        public void CloseJobs(List<Job> jobs)
        {
            if (_arranging)
            {
                return;
            }
            foreach (Job job in jobs)
            {
                foreach (Entry entry in job.Entries)
                {
                    Launcher.Close(job.Name, entry);
                }
            }
        }

        /// <summary>設定画面を開く（開いていれば前面に出す）。</summary>
        public void OpenSettings()
        {
            if (_arranging)
            {
                return;
            }
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsForm(ConfigStore.Clone(_config), _theme, _failedHotkeys);
            // 設定画面でキーを押し直す時に、登録済みのキーが働いてフォルダが開かないよう、開いている間はキーを外す
            _hotkeys.UnregisterAll();
            // 設定画面は設定の写しを直すため、開いている間は大きな画面・区画パネルで直させない（保存で写しに差し替わり、消えるのを防ぐ）
            _main.Hide();
            _area.Hide();
            _settings.FormClosed += (s, e) =>
            {
                SettingsForm form = (SettingsForm)s;
                // 先に参照を外す（閉じた直後はまだ破棄されていないため、外さないとキーの登録し直しが止まる）
                _settings = null;
                if (form.DialogResult == DialogResult.OK)
                {
                    ApplySettings(form);
                }
                else if (!_exiting)
                {
                    ApplyHotkeys();
                }
                form.Dispose();
            };
            _settings.Show();
            _settings.Activate();
        }

        /// <summary>知らせを出す（浮かぶボタンの上の札と、大きな画面の下の帯）。</summary>
        /// <param name="text">知らせの文</param>
        /// <param name="isError">true＝誤り（赤）</param>
        public void Notify(string text, bool isError)
        {
            if (_main != null && _main.Visible)
            {
                _main.SetStatus(text, isError);
            }
            if (_exiting || _button.IsDisposed)
            {
                return;
            }
            // 前の札が残っていれば閉じてから出す（重ならないように）
            if (_toast != null && !_toast.IsDisposed)
            {
                _toast.Close();
            }
            _toast = new ToastForm(text, isError, _theme, _button.Bounds);
            _toast.Show();
        }

        private void ApplySettings(SettingsForm form)
        {
            // 浮かぶボタンの位置は、設定画面を開いている間に動かした分を残す
            int floatX = _config.FloatX;
            int floatY = _config.FloatY;
            bool languageChanged = Lang.Normalize(form.Result.Language) != Lang.Current;
            _config = form.Result;
            _config.FloatX = floatX;
            _config.FloatY = floatY;
            if (languageChanged)
            {
                // 言語は先に切り替える（この後に出す知らせも、新しい言語で出すため）
                Lang.Set(_config.Language);
                Theme.ResetFonts();
            }
            _theme = Theme.Get(_config.Style);
            IconCache.Clear();
            SaveConfig();
            if (!Startup.Set(form.StartWithWindows))
            {
                Notify(Lang.T("起動時の常駐を切り替えられませんでした"), true);
            }
            ApplyHotkeys();
            if (languageChanged)
            {
                // 言語を替えた時は、大きな画面と区画パネルをその言語で作り直す（部品の文字は作る時に決まるため）。
                // 区画パネルで保存していない割り当ては、作り直しで消える（言語の切り替えは設定画面で行い、その間は区画パネルを隠しているため、
                // 編集の途中で切り替わることは少ない）。切り替えの知らせは出さない（画面の言葉が替わるので分かる。前の誤りの知らせを消さないため）
                RecreateForms();
                AppLog.Info("言語の切り替え", Lang.Current, "大きな画面と区画パネルを作り直した");
            }
            else
            {
                _main.SetTheme(_theme);
                _area.Reload(_config);
                _area.SetTheme(_theme);
            }
            _quick.ShowSets(_config);
            AppLog.Info("設定の保存", ConfigStore.FilePath, "グループ " + _config.Jobs.Count + " 件、見た目 " + _theme.Name);
        }

        /// <summary>大きな画面と区画パネルを作り直す（言語を替えた時）。</summary>
        private void RecreateForms()
        {
            MainForm oldMain = _main;
            AreaPanel oldArea = _area;
            _main = new MainForm(this, _theme);
            _area = new AreaPanel(this, _config, _theme);
            oldMain.Dispose();
            oldArea.Dispose();
        }

        /// <summary>区画のとおりに窓を並べる（区画パネルの［並べる］）。</summary>
        /// <param name="shape">区画の形</param>
        /// <param name="zones">区画ごとの登録の番号</param>
        /// <param name="area">並べる画面の作業領域</param>
        public void Arrange(string shape, List<string> zones, System.Drawing.Rectangle area)
        {
            if (_arranging)
            {
                return;
            }
            _config.AreaShape = shape;
            SaveConfig();
            ArrangeResult result;
            _arranging = true;
            try
            {
                result = Arranger.Arrange(_config, shape, zones, area);
            }
            catch (Exception ex)
            {
                AppLog.Error("配置を適用", AreaShapes.Label(shape), ex, "想定外の誤りです。このログのファイルを添えて開発の担当へ連絡してください。もう一度［配置を適用］を押すと、多くの場合は並べ直せます");
                Notify(Lang.T("配置を適用できませんでした"), true);
                return;
            }
            finally
            {
                _arranging = false;
                // 並べている途中に頼まれた終了は、並べ終えたら（途中で失敗しても）行う
                if (_exitRequested)
                {
                    Exit();
                }
            }
            if (_exitRequested)
            {
                return;
            }
            if (result.Failed.Count > 0)
            {
                Notify(Lang.F("配置を適用しました（{0} 件）。開けなかった物：{1}", result.Placed, string.Join(Lang.ListSeparator, result.Failed.ToArray())), true);
            }

            // うまく置けた時は知らせを出さない（並んだ窓を見れば分かるため。2026-11-23 の利用者の指示）

        }

        /// <summary>区画に入っている物の窓をすべて閉じる（区画パネルの［一式を閉じる］。アプリは保存の確認をアプリが出す）。</summary>
        /// <param name="zones">区画ごとの登録の番号</param>
        /// <returns>閉じた（閉じる指示を送った）数</returns>
        public int CloseZones(List<string> zones)
        {
            if (_arranging)
            {
                return 0;
            }
            int closed = 0;
            foreach (string id in zones)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                Job job;
                Entry entry = Arranger.FindEntry(_config, id, out job);
                if (entry != null && Launcher.Close(job.Name, entry))
                {
                    closed++;
                }
            }
            AppLog.Info("一式を閉じる", "区画パネル", closed + " 件の窓を閉じた");
            return closed;
        }

        /// <summary>区画のセットの番号（1〜12 番目）で配置を適用する（丸いボタンの上の □）。</summary>
        /// <param name="index">セットの番号（0 から）</param>
        public void ApplySet(int index)
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                // 設定画面を開いている間は直させない（保存で設定の写しに差し替わり、選んだセットが消えるため）
                _settings.Activate();
                return;
            }
            if (_arranging || index < 0 || index >= _config.LayoutSets.Count)
            {
                return;
            }
            LayoutSet set = _config.LayoutSets[index];
            _config.LastLayoutSet = set.Name;
            _quick.ShowSets(_config);
            if (_area.Visible)
            {
                _area.Hide();
            }
            Arrange(set.Shape, new List<string>(set.Zones), ButtonArea());
        }

        /// <summary>区画のセットの番号の物の窓を、すべて閉じる（丸いボタンの上の □ を右クリック）。</summary>
        /// <param name="index">セットの番号（0 から）</param>
        public void CloseSet(int index)
        {
            if (_arranging || index < 0 || index >= _config.LayoutSets.Count)
            {
                return;
            }
            LayoutSet set = _config.LayoutSets[index];
            AppLog.Info("一式を閉じる", set.Name, "□ " + (index + 1) + " の右クリック");
            CloseZones(new List<string>(set.Zones));
        }

        /// <summary>並べる先の作業領域（丸いボタンのいる画面）。</summary>
        public System.Drawing.Rectangle ButtonArea()
        {
            return Screen.FromRectangle(_button.Bounds).WorkingArea;
        }

        /// <summary>PinFolder を終える（区画パネルの［終了］）。</summary>
        public void ExitApp()
        {
            // 押し間違いで常駐が止まらないよう、確かめてから終える（2026-11-25 の利用者の指示）
            if (MessageBox.Show(_area.Visible ? (IWin32Window)_area : null, Lang.T("PinFolder を終了しますか。"), AppPaths.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            if (_arranging)
            {
                _exitRequested = true;
                return;
            }
            Exit();
        }

        private void ToggleArea()
        {
            if (_arranging)
            {
                return;
            }
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }
            if (_area.Visible && _area.WindowState != FormWindowState.Minimized && Form.ActiveForm == _area)
            {
                _area.Hide();
                return;
            }
            _area.ShowPanel();
        }

        private void ToggleMain()
        {
            if (_arranging)
            {
                return;
            }
            if (_settings != null && !_settings.IsDisposed)
            {
                // 設定画面を開いている間は、設定画面を前に出す
                _settings.Activate();
                return;
            }
            if (_main.Visible && _main.WindowState != FormWindowState.Minimized && Form.ActiveForm == _main)
            {
                _main.Hide();
                return;
            }
            _main.ShowMain();
        }

        /// <summary>キーを登録し直す。登録できなかったキーは知らせ、設定画面の「キー」に「使えません」と出す。</summary>
        private void ApplyHotkeys()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                // 設定画面を開いている間はキーを外したままにする（閉じた時に登録し直す）
                return;
            }
            _hotkeys.UnregisterAll();
            HashSet<string> before = new HashSet<string>(_failedHotkeys);
            _failedHotkeys.Clear();
            List<string> failed = new List<string>();
            RegisterHotkey("PinFolder の画面を開く", _config.ListHotkey, ToggleMain, failed);
            RegisterHotkey("左の区画へ移す", _config.MoveLeftHotkey, () => MoveForeground(-1, 0), failed);
            RegisterHotkey("右の区画へ移す", _config.MoveRightHotkey, () => MoveForeground(1, 0), failed);
            RegisterHotkey("上の区画へ移す", _config.MoveUpHotkey, () => MoveForeground(0, -1), failed);
            RegisterHotkey("下の区画へ移す", _config.MoveDownHotkey, () => MoveForeground(0, 1), failed);
            foreach (Job job in _config.Jobs)
            {
                foreach (Entry entry in job.Entries)
                {
                    Job targetJob = job;
                    Entry targetEntry = entry;
                    RegisterHotkey(job.Name + " / " + entry.Name, entry.Hotkey, () => OpenEntry(targetJob, targetEntry), failed);
                }
            }
            // 知らせるのは、前回は無かった失敗だけ（登録を足すたびに同じ知らせが出ないように）
            failed.RemoveAll(hotkey => before.Contains(HotkeyText.Normalize(hotkey)));
            if (failed.Count == 1)
            {
                Notify(Lang.F("{0} は使えませんでした", failed[0]), true);
            }
            else if (failed.Count > 1)
            {
                Notify(Lang.F("{0} ほか {1} 件のキーは使えませんでした", failed[0], failed.Count - 1), true);
            }
        }

        private void RegisterHotkey(string target, string hotkey, Action action, List<string> failed)
        {
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                return;
            }
            string reason;
            if (_hotkeys.Register(hotkey, action, out reason))
            {
                return;
            }
            failed.Add(hotkey);
            _failedHotkeys.Add(HotkeyText.Normalize(hotkey));
            AppLog.Warn("キーの登録", target + "（" + hotkey + "）", "キーを登録できなかった：" + reason,
                "設定画面の「キー」で、別のキーにしてください（ほかのアプリと重ならない組み合わせ。例：Ctrl+Alt+数字）");
        }

        private void MoveForeground(int columnStep, int rowStep)
        {
            if (!_arranging)
            {
                Arranger.MoveForeground(_config.AreaShape, columnStep, rowStep);
            }
        }

        private void Touch(Job job)
        {
            job.LastUsed = DateTime.Now.ToString("s");
            SaveConfig();
        }

        private void SaveConfig()
        {
            string error;
            if (!ConfigStore.Save(_config, out error))
            {
                Notify(Lang.T("設定を保存できませんでした"), true);
            }
        }

        private void Exit()
        {
            // 設定画面を先に閉じる（閉じた時にキーを登録し直さないよう、終了中の印を立てる）
            _exiting = true;
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Close();
            }
            _hotkeys.Dispose();
            _main.Dispose();
            _area.Dispose();
            _quick.Close();
            _button.Close();
            ExitThread();
        }
    }
}
