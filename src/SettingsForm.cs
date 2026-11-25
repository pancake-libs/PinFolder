using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 設定（タブ「キー」「全般」）。登録は大きな画面で行う。設定の写しを直し、［保存］で返す。
    /// </summary>
    internal sealed class SettingsForm : Form, IFitDialog
    {
        private const string StatusUnusable = "使えません";
        private const string ListTarget = "PinFolder の画面を開く";

        /// <summary>「全般」の入力欄の左の位置（96dpi の値。英語の項目名でも重ならない幅）</summary>
        private const int FieldX = 150;

        /// <summary>「キー」の入力欄の左の位置（96dpi の値）</summary>
        private const int KeyBoxX = 90;

        private readonly AppConfig _config;
        private readonly HashSet<string> _failedHotkeys;
        private readonly TabControl _tabs;
        private readonly Panel _body;
        private readonly FlowLayoutPanel _footer;
        private readonly TabPage _keyPage;
        private Theme _theme;

        // キー
        private readonly ListView _keyList;
        private readonly HotkeyBox _keyBox;
        private readonly Label _keyError;

        // 全般
        private readonly CheckBox _startupCheck;
        private readonly ComboBox _styleCombo;
        private readonly ComboBox _languageCombo;
        private readonly Label _generalError;

        /// <summary>画面を作る。</summary>
        /// <param name="config">直す設定の写し</param>
        /// <param name="theme">今の見た目のスタイル</param>
        /// <param name="failedHotkeys">登録できなかったキー（「使えません」と出す）</param>
        public SettingsForm(AppConfig config, Theme theme, HashSet<string> failedHotkeys)
        {
            _config = config;
            _theme = theme;
            _failedHotkeys = failedHotkeys;

            SuspendLayout();
            Ui.SetupDialog(this, Lang.T("PinFolder 設定"), 760, 560);
            _footer = Ui.CreateDialogLayout(this, out _body);
            _body.Padding = new Padding(8, 8, 8, 0);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _keyPage = new TabPage(Lang.T("キー"));
            TabPage generalPage = new TabPage(Lang.T("全般"));
            _tabs.TabPages.Add(_keyPage);
            _tabs.TabPages.Add(generalPage);
            _body.Controls.Add(_tabs);

            // ---- キー ----
            _keyList = new ListView
            {
                Location = new Point(8, 10),
                Size = new Size(720, 340),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false
            };
            _keyList.Columns.Add(Lang.T("対象"), 330);
            _keyList.Columns.Add(Lang.T("キー"), 220);
            _keyList.Columns.Add(Lang.T("状態"), 140);
            _keyList.SelectedIndexChanged += (s, e) => ShowSelectedKey();
            _keyPage.Controls.Add(_keyList);
            _keyPage.Controls.Add(Ui.Label(Lang.T("キー"), 8, 365));
            _keyBox = new HotkeyBox { Location = new Point(KeyBoxX, 361), Width = 220 };
            _keyPage.Controls.Add(_keyBox);
            _keyPage.Controls.Add(Ui.Button(Lang.T("設定する"), KeyBoxX + 228, 359, 96, (s, e) => SetSelectedKey(_keyBox.Text)));
            _keyPage.Controls.Add(Ui.Button(Lang.T("外す"), KeyBoxX + 332, 359, 72, (s, e) => SetSelectedKey(string.Empty)));
            _keyError = Ui.ErrorLabel(8, 396, 720);
            _keyPage.Controls.Add(_keyError);

            // ---- 全般 ----
            _startupCheck = new CheckBox { Text = Lang.T("Windows の起動時に常駐する"), Location = new Point(16, 18), AutoSize = true, Tag = UiTags.Bold };
            generalPage.Controls.Add(_startupCheck);
            // 言語（選択肢は、どの言語の画面からでも読めるよう、その言語の名前で出す）
            generalPage.Controls.Add(Ui.Label(Lang.T("言語"), 16, 60));
            _languageCombo = new ComboBox { Location = new Point(FieldX, 56), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string id in Lang.Ids)
            {
                _languageCombo.Items.Add(Lang.DisplayName(id));
            }
            generalPage.Controls.Add(_languageCombo);
            generalPage.Controls.Add(Ui.Label(Lang.T("見た目"), 16, 98));
            _styleCombo = new ComboBox { Location = new Point(FieldX, 94), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            _styleCombo.Items.AddRange(Theme.All);
            _styleCombo.SelectedIndexChanged += (s, e) => PreviewStyle();
            generalPage.Controls.Add(_styleCombo);
            generalPage.Controls.Add(Ui.Label(Lang.T("ログの場所"), 16, 136));
            TextBox logBox = Ui.TextBox(FieldX, 132, 504);
            logBox.ReadOnly = true;
            logBox.Text = AppLog.FolderPath;
            generalPage.Controls.Add(logBox);
            generalPage.Controls.Add(Ui.Button(Lang.T("開く"), FieldX + 512, 130, 80, (s, e) => OpenLogFolder()));
            _generalError = Ui.ErrorLabel(16, 172, 700);
            generalPage.Controls.Add(_generalError);

            // ---- 下のボタン（いつも見える帯） ----
            // 設定画面はモードレス（Show）で開くため、DialogResult だけでは閉じない。押したら閉じる
            Button cancel = Ui.AddFooterButton(_footer, Lang.T("キャンセル"), (s, e) => { DialogResult = DialogResult.Cancel; Close(); });
            Button save = Ui.AddFooterButton(_footer, Lang.T("保存"), (s, e) => Save());
            save.Tag = UiTags.Primary;
            CancelButton = cancel;

            FillKeys();
            _startupCheck.Checked = Startup.IsEnabled();
            _styleCombo.SelectedItem = Theme.Get(_config.Style);
            _languageCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Lang.Ids, Lang.Normalize(_config.Language)));

            _theme.Apply(this);
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>大きさの確かめ（--check-layout）で使う：大きさを中身に合わせる。</summary>
        public void FitForCheck()
        {
            Ui.FitToContent(this, _body, _footer);
        }

        protected override void OnLoad(EventArgs e)
        {
            // 画面の倍率・文字の大きさで項目やボタンが隠れないよう、中身に合わせて大きさを決める
            Ui.FitToContent(this, _body, _footer);
            base.OnLoad(e);
        }

        /// <summary>保存した設定（［保存］で閉じた時だけ使う）</summary>
        public AppConfig Result
        {
            get { return _config; }
        }

        /// <summary>「Windows の起動時に常駐する」の値</summary>
        public bool StartWithWindows
        {
            get { return _startupCheck.Checked; }
        }

        // ================= キー =================

        private void FillKeys()
        {
            _keyError.Text = string.Empty;
            List<KeyTarget> targets = KeyTargets();
            Dictionary<string, int> counts = CountHotkeys(targets);
            _keyList.BeginUpdate();
            _keyList.Items.Clear();
            foreach (KeyTarget target in targets)
            {
                string hotkey = target.Get();
                ListViewItem item = new ListViewItem(target.Name);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(string.IsNullOrEmpty(hotkey) ? Lang.T("なし") : hotkey);
                string normalized = HotkeyText.Normalize(hotkey);
                bool unusable = normalized.Length > 0 && (counts[normalized] > 1 || _failedHotkeys.Contains(normalized));
                ListViewItem.ListViewSubItem status = item.SubItems.Add(unusable ? Lang.T(StatusUnusable) : string.Empty);
                status.ForeColor = Theme.ErrorText;
                item.Tag = target;
                _keyList.Items.Add(item);
            }
            _keyList.EndUpdate();
        }

        /// <summary>キーを決められる対象（画面を開く・区画を移す・登録した物）</summary>
        private List<KeyTarget> KeyTargets()
        {
            List<KeyTarget> targets = new List<KeyTarget>();
            targets.Add(new KeyTarget(Lang.T(ListTarget), () => _config.ListHotkey, value => _config.ListHotkey = value));
            targets.Add(new KeyTarget(Lang.T("左の区画へ移す"), () => _config.MoveLeftHotkey, value => _config.MoveLeftHotkey = value));
            targets.Add(new KeyTarget(Lang.T("右の区画へ移す"), () => _config.MoveRightHotkey, value => _config.MoveRightHotkey = value));
            targets.Add(new KeyTarget(Lang.T("上の区画へ移す"), () => _config.MoveUpHotkey, value => _config.MoveUpHotkey = value));
            targets.Add(new KeyTarget(Lang.T("下の区画へ移す"), () => _config.MoveDownHotkey, value => _config.MoveDownHotkey = value));
            foreach (Job job in _config.Jobs)
            {
                foreach (Entry entry in job.Entries)
                {
                    Entry target = entry;
                    targets.Add(new KeyTarget(job.Name + " / " + entry.Name, () => target.Hotkey, value => target.Hotkey = value));
                }
            }
            return targets;
        }

        /// <summary>キーごとの使われている数（同じキーが 2 つ以上あると使えない）</summary>
        private static Dictionary<string, int> CountHotkeys(List<KeyTarget> targets)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (KeyTarget target in targets)
            {
                string normalized = HotkeyText.Normalize(target.Get());
                if (normalized.Length == 0)
                {
                    continue;
                }
                int count;
                counts.TryGetValue(normalized, out count);
                counts[normalized] = count + 1;
            }
            return counts;
        }

        private void ShowSelectedKey()
        {
            KeyTarget target = _keyList.SelectedItems.Count == 0 ? null : _keyList.SelectedItems[0].Tag as KeyTarget;
            _keyBox.Text = target == null ? string.Empty : target.Get();
        }

        private void SetSelectedKey(string hotkey)
        {
            if (_keyList.SelectedItems.Count == 0)
            {
                _keyError.Text = Lang.T("対象を選んでください");
                return;
            }
            int index = _keyList.SelectedIndices[0];
            ((KeyTarget)_keyList.SelectedItems[0].Tag).Set(hotkey);
            FillKeys();
            if (index < _keyList.Items.Count)
            {
                _keyList.Items[index].Selected = true;
            }
        }

        /// <summary>キーを決められる対象 1 つ（名前と、キーの読み書き）</summary>
        private sealed class KeyTarget
        {
            private readonly Func<string> _get;
            private readonly Action<string> _set;

            public KeyTarget(string name, Func<string> get, Action<string> set)
            {
                Name = name;
                _get = get;
                _set = set;
            }

            public string Name { get; private set; }

            public string Get()
            {
                return _get();
            }

            public void Set(string value)
            {
                _set(value);
            }
        }
        // ================= 全般 =================

        private void PreviewStyle()
        {
            Theme selected = _styleCombo.SelectedItem as Theme;
            if (selected == null || selected == _theme)
            {
                return;
            }
            _theme = selected;
            _theme.Apply(this);
        }

        private void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLog.FolderPath);
                using (Process.Start(WindowFinder.ExplorerExe, "\"" + AppLog.FolderPath + "\""))
                {
                }
            }
            catch (Exception ex)
            {
                _generalError.Text = Lang.T("ログの場所を開けませんでした");
                AppLog.Error("ログの場所を開く", AppLog.FolderPath, ex, "エクスプローラーで、上の場所を直接開いてください");
            }
        }

        // ================= 保存 =================

        private void Save()
        {
            foreach (KeyValuePair<string, int> pair in CountHotkeys(KeyTargets()))
            {
                if (pair.Value > 1)
                {
                    _tabs.SelectedTab = _keyPage;
                    FillKeys();
                    _keyError.Text = Lang.F("同じキーが 2 つあります：{0}", pair.Key);
                    return;
                }
            }
            Theme selected = _styleCombo.SelectedItem as Theme;
            _config.Style = selected == null ? Theme.DefaultId : selected.Id;
            _config.Language = Lang.Ids[Math.Max(0, _languageCombo.SelectedIndex)];
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
