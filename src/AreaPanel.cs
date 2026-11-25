using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 区画パネル（浮かぶボタンの右クリックで開く）。
    /// 区画の形を選び、右の「登録した物」から区画へ引っぱって割り当て、右上の［配置を適用］で画面に並べる（並べた窓は手前に出す）。
    /// 区画の形と割り当ては、名前を付けたセットとして複数記録できる。
    /// </summary>
    internal sealed class AreaPanel : Form
    {
        private const int FormWidth = 960;
        private const int FormHeight = 600;
        private const int MinWidth = 640;
        private const int MinHeight = 420;
        private const string NewSetLabel = "（新しいセット）";
        private const string EmptyZoneText = "空き";
        private const string DragEntryPrefix = "entry:";
        private const string DragZonePrefix = "zone:";

        /// <summary>「セット」「形」の札の幅（96dpi の値。右の部品の左の端をそろえる）</summary>
        private const int LabelWidth = 48;

        /// <summary>［配置を適用］と［一式を閉じる］の間（96dpi の値。押し間違えないよう離す）</summary>
        private const int CloseGap = 20;

        /// <summary>区画どうしの間（96dpi の値。半分ずつ両側に取る）</summary>
        private const int ZoneGap = 8;

        /// <summary>［消す］と左のボタンの間（96dpi の値）</summary>
        private const int DangerGap = 12;

        /// <summary>選んでいる形のボタンの枠の太さ</summary>
        private const int SelectedBorder = 2;

        /// <summary>登録した物の一覧の行の高さ（文字の高さに対する倍率）</summary>
        private const float TreeRowRatio = 1.7f;

        /// <summary>区画の左の帯の色（グループの並びの順に使い、足りなければ繰り返す）</summary>
        private static readonly Color[] GroupColors =
        {
            ColorTranslator.FromHtml("#2563EB"),
            ColorTranslator.FromHtml("#0D9488"),
            ColorTranslator.FromHtml("#D97706"),
            ColorTranslator.FromHtml("#7C3AED"),
            ColorTranslator.FromHtml("#DB2777"),
            ColorTranslator.FromHtml("#4B5563")
        };

        private readonly PinFolderContext _owner;
        private readonly ComboBox _setCombo;
        private readonly List<RadioButton> _shapeButtons = new List<RadioButton>();
        private readonly TableLayoutPanel _zoneGrid;
        private readonly TreeView _entryList;
        private readonly Panel _entryFrame;
        private readonly Panel _entryInner;
        private readonly Button _closeAllButton;
        private readonly Label _status;
        private readonly ContextMenuStrip _zoneMenu;
        private readonly ToolTip _zoneTip = new ToolTip();
        private AppConfig _config;
        private Theme _theme;
        private string _shape;
        private List<string> _zones = new List<string>();
        private int _menuZone = -1;
        private bool _loading;
        private bool _positioned;

        /// <summary>画面を作る。</summary>
        /// <param name="owner">常駐の本体（保存・並べる・設定・終了を頼む先。大きさの確かめの時は null）</param>
        /// <param name="config">今の設定</param>
        /// <param name="theme">見た目のスタイル</param>
        public AreaPanel(PinFolderContext owner, AppConfig config, Theme theme)
        {
            _owner = owner;
            _config = config;
            _theme = theme;
            _shape = config.AreaShape;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Lang.T("PinFolder 区画");
            Icon = AppPaths.WindowIcon();
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(FormWidth, FormHeight);
            MinimumSize = new Size(MinWidth, MinHeight);
            KeyPreview = true;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- 上：セットと形（左）、配置を適用（右上。いちばん目立たせる） ----
            TableLayoutPanel top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel settings = new FlowLayoutPanel { Anchor = AnchorStyles.Top | AnchorStyles.Left, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };

            // 折り返さない（折り返すと、高さを多めに取って区画との間が大きく空く）
            FlowLayoutPanel setRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            // 「セット」「形」の札は同じ幅にして、右の部品の左の端をそろえる
            setRow.Controls.Add(new Label { Text = Lang.T("セット"), AutoSize = true, MinimumSize = new Size(LabelWidth, 0), Tag = UiTags.Bold, Margin = new Padding(0, 7, 8, 0) });
            _setCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, Margin = new Padding(0, 3, 8, 0) };
            _setCombo.SelectedIndexChanged += (s, e) => LoadSelectedSet();
            // セットに番号を付けて出す（1〜12 番目は、丸いボタンの上の □ で呼び出せる）
            _setCombo.FormattingEnabled = true;
            _setCombo.Format += (s, e) =>
            {
                LayoutSet set = e.ListItem as LayoutSet;
                if (set != null)
                {
                    e.Value = (_config.LayoutSets.IndexOf(set) + 1) + ". " + set.Name;
                }
            };
            setRow.Controls.Add(_setCombo);
            setRow.Controls.Add(SmallButton(Lang.T("保存"), (s, e) => SaveSet(false)));
            setRow.Controls.Add(SmallButton(Lang.T("別名で保存"), (s, e) => SaveSet(true)));
            // 消すは少し離し、赤い文字にする（保存と押し間違えない）
            Button delete = SmallButton(Lang.T("消す"), (s, e) => DeleteSet());
            delete.Tag = UiTags.Danger;
            delete.Margin = new Padding(DangerGap, 0, 0, 0);
            setRow.Controls.Add(delete);
            settings.Controls.Add(setRow);

            FlowLayoutPanel shapeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            shapeRow.Controls.Add(new Label { Text = Lang.T("形"), AutoSize = true, MinimumSize = new Size(LabelWidth, 0), Tag = UiTags.Bold, Margin = new Padding(0, 7, 8, 0) });
            foreach (string shape in AreaShapes.All)
            {
                RadioButton radio = new RadioButton
                {
                    Text = AreaShapes.Label(shape),
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    AutoSize = true,
                    MinimumSize = new Size(72, 30),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Tag = shape,
                    Margin = new Padding(0, 0, 6, 0)
                };
                radio.CheckedChanged += OnShapeChanged;
                _shapeButtons.Add(radio);
                shapeRow.Controls.Add(radio);
            }
            settings.Controls.Add(shapeRow);

            top.Controls.Add(settings, 0, 0);

            Button arrange = new Button
            {
                Text = Lang.T("配置を適用"),
                AutoSize = true,
                MinimumSize = new Size(180, 64),
                Padding = new Padding(24, 6, 24, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Tag = UiTags.Strong,
                Margin = new Padding(12, 0, 0, 0)
            };
            arrange.Click += (s, e) => Arrange();
            // 右上：配置を適用（いちばん目立たせる）と、その下に一式を閉じる
            FlowLayoutPanel actions = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0) };
            arrange.Margin = new Padding(0);
            actions.Controls.Add(arrange);
            // 一式を閉じるは、配置を適用から離して一段控えめにする（押し間違えない）
            _closeAllButton = new Button { Text = Lang.T("一式を閉じる"), AutoSize = true, MinimumSize = new Size(120, 30), Padding = new Padding(10, 0, 10, 0), Anchor = AnchorStyles.Right, Margin = new Padding(0, CloseGap, 0, 0) };
            Button closeAll = _closeAllButton;
            closeAll.Click += (s, e) => CloseSet();
            actions.Controls.Add(closeAll);
            top.Controls.Add(actions, 1, 0);
            root.Controls.Add(top, 0, 0);

            // ---- 中：区画（左）と登録した物（右） ----
            TableLayoutPanel middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            _zoneGrid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(0) };
            middle.Controls.Add(_zoneGrid, 0, 0);

            TableLayoutPanel listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            listPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listPanel.Controls.Add(new Label { Text = Lang.T("登録した物"), AutoSize = true, Tag = UiTags.Bold, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
            // グループ → 行 の 2 段で出す（行を区画へ引っぱる。ダブルクリックで空いた区画へ）
            // 点線と＋－の印は出さず（いつも開いておく）、行の高さを広げて引っぱりやすくする
            _entryList = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowRootLines = false,
                ShowPlusMinus = false,
                ShowNodeToolTips = true,
                BorderStyle = BorderStyle.None
            };
            _entryList.ItemDrag += OnEntryItemDrag;
            _entryList.NodeMouseDoubleClick += (s, e) => PutInFirstEmpty();
            _entryList.FontChanged += (s, e) => FitTreeRows();
            // 枠は薄い線（枠の色の下地に 1px の余白を取って一覧を置く）
            _entryFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            // 枠の内側に余白を取り、文字が枠の左の端にくっつかないようにする
            _entryInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 0, 0), Margin = new Padding(0) };
            _entryInner.Controls.Add(_entryList);
            _entryFrame.Controls.Add(_entryInner);
            listPanel.Controls.Add(_entryFrame, 0, 1);
            middle.Controls.Add(listPanel, 1, 0);
            root.Controls.Add(middle, 0, 1);

            // ---- 下：知らせ（左）と設定・終了（右） ----
            TableLayoutPanel bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) };
            bottom.Controls.Add(_status, 0, 0);
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            buttons.Controls.Add(SmallButton(Lang.T("設定"), (s, e) => { if (_owner != null) { _owner.OpenSettings(); } }));
            buttons.Controls.Add(SmallButton(Lang.T("終了"), (s, e) => { if (_owner != null) { _owner.ExitApp(); } }));
            bottom.Controls.Add(buttons, 1, 0);
            root.Controls.Add(bottom, 0, 2);

            Controls.Add(root);

            _zoneMenu = new ContextMenuStrip();
            _zoneMenu.Items.Add(Lang.T("空にする"), null, (s, e) => ClearZone(_menuZone));

            _theme.Apply(this);
            ApplyExtras();
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
            FitTreeRows();
            Reload(config);
        }

        /// <summary>スタイルの共通の当て方に無い色（枠・控えめのボタン・形の選択）を当てる。</summary>
        private void ApplyExtras()
        {
            _entryFrame.BackColor = _theme.ControlBorder;
            _entryInner.BackColor = _theme.ControlBack;
            _closeAllButton.ForeColor = _theme.SubText;
            StyleShapeButtons();
        }

        /// <summary>選んでいる形のボタンを、薄い地・主の色の文字と太い枠にする（ほかはふつうのボタン）。</summary>
        private void StyleShapeButtons()
        {
            foreach (RadioButton radio in _shapeButtons)
            {
                bool selected = radio.Checked;
                radio.UseVisualStyleBackColor = false;
                radio.BackColor = selected ? _theme.RowSelected : _theme.ControlBack;
                radio.ForeColor = selected ? _theme.PrimaryBack : _theme.Text;
                radio.FlatAppearance.BorderColor = selected ? _theme.PrimaryBack : _theme.ControlBorder;
                radio.FlatAppearance.BorderSize = selected ? DpiWatcher.Scale(this, SelectedBorder) : DpiWatcher.Scale(this, 1);
                radio.FlatAppearance.CheckedBackColor = _theme.RowSelected;
                radio.FlatAppearance.MouseOverBackColor = _theme.ControlHover;
            }
        }

        /// <summary>登録した物の一覧の行の高さを、文字の高さに余白を足した高さにする。</summary>
        private void FitTreeRows()
        {
            int height = (int)Math.Round(_entryList.Font.Height * TreeRowRatio);
            if (height > 0 && _entryList.ItemHeight != height)
            {
                _entryList.ItemHeight = height;
            }
        }

        /// <summary>設定が替わった時に、セット・登録の一覧・区画を作り直す。</summary>
        public void Reload(AppConfig config)
        {
            _config = config;
            _loading = true;
            _setCombo.Items.Clear();
            _setCombo.Items.Add(Lang.T(NewSetLabel));
            int select = 0;
            foreach (LayoutSet set in _config.LayoutSets)
            {
                int index = _setCombo.Items.Add(set);
                if (set.Name == _config.LastLayoutSet)
                {
                    select = index;
                }
            }
            FillEntryList();
            _loading = false;
            _setCombo.SelectedIndex = select;
            if (select == 0)
            {
                SetShape(_config.AreaShape);
            }
        }

        /// <summary>
        /// 大きな画面で登録を直した後に、「登録した物」の一覧と区画の名前だけを新しくする（区画の割り当ては残す。消えた登録の区画は空にする）。
        /// </summary>
        public void RefreshEntries()
        {
            FillEntryList();
            Job job;
            for (int i = 0; i < _zones.Count; i++)
            {
                if (!string.IsNullOrEmpty(_zones[i]) && Arranger.FindEntry(_config, _zones[i], out job) == null)
                {
                    _zones[i] = string.Empty;
                }
            }
            BuildZones();
        }

        private void FillEntryList()
        {
            _entryList.BeginUpdate();
            _entryList.Nodes.Clear();
            foreach (Job job in _config.Jobs)
            {
                TreeNode group = new TreeNode(job.Name) { NodeFont = _theme.BoldFont };
                foreach (Entry entry in job.Entries)
                {
                    group.Nodes.Add(new TreeNode(entry.Name) { Tag = new EntryItem(job, entry), ToolTipText = entry.Path });
                }
                _entryList.Nodes.Add(group);
            }
            _entryList.ExpandAll();
            _entryList.EndUpdate();
        }
        /// <summary>見た目のスタイルを替える。</summary>
        public void SetTheme(Theme theme)
        {
            _theme = theme;
            _theme.Apply(this);
            ApplyExtras();
            BuildZones();
        }

        /// <summary>画面を出す（初めはマウスのある画面の中央。作業領域に収める）。</summary>
        public void ShowPanel()
        {
            if (!_positioned)
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
                int width = Math.Min(Width, area.Width);
                int height = Math.Min(Height, area.Height);
                Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
                _positioned = true;
            }
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            _status.Text = string.Empty;
            Show();
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // ✕ は隠すだけ（終了は［終了］）
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        // ================= 形と区画 =================

        private void OnShapeChanged(object sender, EventArgs e)
        {
            RadioButton radio = (RadioButton)sender;
            if (!radio.Checked || _loading)
            {
                return;
            }
            _shape = (string)radio.Tag;
            StyleShapeButtons();
            FitZones();
            BuildZones();
        }

        private void SetShape(string shape)
        {
            _shape = AreaShapes.IsValid(shape) ? shape : AppConfig.DefaultAreaShape;
            _loading = true;
            foreach (RadioButton radio in _shapeButtons)
            {
                radio.Checked = (string)radio.Tag == _shape;
            }
            _loading = false;
            StyleShapeButtons();
            FitZones();
            BuildZones();
        }

        /// <summary>区画の数を形に合わせる（増えた区画は空き、減った分は捨てる）。</summary>
        private void FitZones()
        {
            int count = AreaShapes.Count(_shape);
            while (_zones.Count < count)
            {
                _zones.Add(string.Empty);
            }
            if (_zones.Count > count)
            {
                _zones.RemoveRange(count, _zones.Count - count);
            }
        }

        private void BuildZones()
        {
            int columns = AreaShapes.Columns(_shape);
            _zoneGrid.SuspendLayout();
            // 作り直す区画の説明（ToolTip）を外す（消した区画を ToolTip が持ち続けないように）
            _zoneTip.RemoveAll();
            Control[] old = new Control[_zoneGrid.Controls.Count];
            _zoneGrid.Controls.CopyTo(old, 0);
            _zoneGrid.Controls.Clear();
            foreach (Control control in old)
            {
                control.Dispose();
            }
            _zoneGrid.ColumnStyles.Clear();
            _zoneGrid.RowStyles.Clear();
            _zoneGrid.ColumnCount = columns;
            _zoneGrid.RowCount = AreaShapes.Rows;
            for (int c = 0; c < columns; c++)
            {
                _zoneGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            }
            for (int r = 0; r < AreaShapes.Rows; r++)
            {
                _zoneGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / AreaShapes.Rows));
            }
            for (int i = 0; i < _zones.Count; i++)
            {
                _zoneGrid.Controls.Add(BuildZone(i), i % columns, i / columns);
            }
            _zoneGrid.ResumeLayout(true);
        }

        private Control BuildZone(int index)
        {
            Job job = null;
            Entry entry = string.IsNullOrEmpty(_zones[index]) ? null : Arranger.FindEntry(_config, _zones[index], out job);
            ZoneTile zone = new ZoneTile(_theme)
            {
                Dock = DockStyle.Fill,
                Margin = ZoneMargin(index),
                AllowDrop = true,
                Number = (index + 1).ToString(),
                GroupName = entry == null ? string.Empty : job.Name,
                EntryName = entry == null ? Lang.T(EmptyZoneText) : entry.Name,
                Filled = entry != null,
                GroupColor = entry == null ? _theme.ControlBorder : GroupColors[Math.Max(0, _config.Jobs.IndexOf(job)) % GroupColors.Length],
                Tag = index
            };
            if (entry != null)
            {
                _zoneTip.SetToolTip(zone, job.Name + " / " + entry.Name + "\n" + entry.Path);
            }
            zone.DragEnter += OnZoneDragEnter;
            zone.DragDrop += OnZoneDrop;
            zone.MouseDown += OnZoneMouseDown;
            return zone;
        }

        /// <summary>区画の余白（区画どうしの間だけ空け、外側の端は区画の枠に合わせる。右の一覧と上下の端がそろう）。</summary>
        private Padding ZoneMargin(int index)
        {
            int columns = AreaShapes.Columns(_shape);
            int half = DpiWatcher.Scale(this, ZoneGap) / 2;
            int column = index % columns;
            int row = index / columns;
            return new Padding(
                column == 0 ? 0 : half,
                row == 0 ? 0 : half,
                column == columns - 1 ? 0 : half,
                row == AreaShapes.Rows - 1 ? 0 : half);
        }

        private void ClearZone(int index)
        {
            if (index < 0 || index >= _zones.Count)
            {
                return;
            }
            _zones[index] = string.Empty;
            BuildZones();
        }

        private void PutInFirstEmpty()
        {
            EntryItem item = _entryList.SelectedNode == null ? null : _entryList.SelectedNode.Tag as EntryItem;
            if (item == null)
            {
                return;
            }
            int empty = _zones.IndexOf(string.Empty);
            if (empty < 0)
            {
                SetStatus(Lang.T("空いた区画がありません"), true);
                return;
            }
            if (_zones.Contains(item.Entry.Id))
            {
                SetStatus(Lang.T("もう区画に入っています"), false);
                return;
            }
            _zones[empty] = item.Entry.Id;
            BuildZones();
        }

        // ================= 引っぱって入れる =================

        private void OnEntryItemDrag(object sender, ItemDragEventArgs e)
        {
            // グループの行は引っぱれない（区画に入れるのは、その下の行）
            TreeNode node = e.Item as TreeNode;
            EntryItem item = node == null ? null : node.Tag as EntryItem;
            if (e.Button != MouseButtons.Left || item == null)
            {
                return;
            }
            _entryList.SelectedNode = node;
            _entryList.DoDragDrop(DragEntryPrefix + item.Entry.Id, DragDropEffects.Copy);
        }
        private void OnZoneMouseDown(object sender, MouseEventArgs e)
        {
            Control zone = (Control)sender;
            int index = (int)zone.Tag;
            if (e.Button == MouseButtons.Right)
            {
                _menuZone = index;
                _zoneMenu.Show(zone, e.Location);
                return;
            }
            if (e.Button == MouseButtons.Left && !string.IsNullOrEmpty(_zones[index]))
            {
                zone.DoDragDrop(DragZonePrefix + index, DragDropEffects.Move);
            }
        }

        private void OnZoneDragEnter(object sender, DragEventArgs e)
        {
            string data = e.Data.GetData(DataFormats.StringFormat) as string;
            if (data != null && data.StartsWith(DragEntryPrefix, StringComparison.Ordinal))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else if (data != null && data.StartsWith(DragZonePrefix, StringComparison.Ordinal))
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void OnZoneDrop(object sender, DragEventArgs e)
        {
            int target = (int)((Control)sender).Tag;
            string data = e.Data.GetData(DataFormats.StringFormat) as string;
            if (data == null)
            {
                return;
            }
            if (data.StartsWith(DragEntryPrefix, StringComparison.Ordinal))
            {
                string id = data.Substring(DragEntryPrefix.Length);
                Job job;
                if (Arranger.FindEntry(_config, id, out job) == null)
                {
                    return;
                }
                // 同じ物は 1 つの区画だけに置く（ほかの区画にあれば、そこは空ける）
                for (int i = 0; i < _zones.Count; i++)
                {
                    if (_zones[i] == id)
                    {
                        _zones[i] = string.Empty;
                    }
                }
                _zones[target] = id;
            }
            else if (data.StartsWith(DragZonePrefix, StringComparison.Ordinal))
            {
                // 区画どうしは入れ替える
                int source;
                if (int.TryParse(data.Substring(DragZonePrefix.Length), out source) && source >= 0 && source < _zones.Count)
                {
                    string moved = _zones[source];
                    _zones[source] = _zones[target];
                    _zones[target] = moved;
                }
            }
            // 引っぱった元の区画の部品を作り直すため、ドロップの処理を抜けてから行う
            BeginInvoke(new Action(BuildZones));
        }

        // ================= セット =================

        private void LoadSelectedSet()
        {
            if (_loading)
            {
                return;
            }
            LayoutSet set = _setCombo.SelectedItem as LayoutSet;
            if (set == null)
            {
                return;
            }
            _zones = new List<string>(set.Zones);
            SetShape(set.Shape);
        }

        private void SaveSet(bool asNew)
        {
            LayoutSet set = _setCombo.SelectedItem as LayoutSet;
            if (asNew || set == null)
            {
                using (NameForm form = new NameForm(Lang.T("セットの名前"), set == null ? string.Empty : Lang.F("{0} の写し", set.Name), _theme))
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    set = FindSet(form.Result);
                    if (set != null && MessageBox.Show(this, Lang.F("同じ名前のセット「{0}」があります。上書きしますか。", set.Name), AppPaths.AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        return;
                    }
                    if (set == null)
                    {
                        set = new LayoutSet { Name = form.Result };
                        _config.LayoutSets.Add(set);
                    }
                }
            }
            set.Shape = _shape;
            set.Zones = new List<string>(_zones);
            _config.LastLayoutSet = set.Name;
            Saved();
            Reload(_config);
            SetStatus(Lang.F("セット「{0}」を保存しました", set.Name), false);
            AppLog.Info("区画のセットの保存", set.Name, AreaShapes.Label(set.Shape) + "、区画 " + set.Zones.Count + " 個");
        }

        private void DeleteSet()
        {
            LayoutSet set = _setCombo.SelectedItem as LayoutSet;
            if (set == null)
            {
                return;
            }
            if (MessageBox.Show(this, Lang.F("セット「{0}」を消しますか（フォルダ・アプリの登録は消しません）。", set.Name), AppPaths.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            _config.LayoutSets.Remove(set);
            _config.LastLayoutSet = string.Empty;
            Saved();
            Reload(_config);
            AppLog.Info("区画のセットの削除", set.Name, "消した");
            SetStatus(Lang.F("セット「{0}」を消しました", set.Name), false);
        }

        private LayoutSet FindSet(string name)
        {
            foreach (LayoutSet set in _config.LayoutSets)
            {
                if (set.Name == name)
                {
                    return set;
                }
            }
            return null;
        }

        // ================= 配置を適用 =================

        /// <summary>区画に入っている物の窓を、すべて閉じる（ほかの窓は閉じない）。</summary>
        private void CloseSet()
        {
            if (_owner == null)
            {
                return;
            }
            if (!_zones.Exists(id => !string.IsNullOrEmpty(id)))
            {
                SetStatus(Lang.T("区画に登録した物を入れてください"), true);
                return;
            }
            int closed = _owner.CloseZones(new List<string>(_zones));
            SetStatus(closed == 0 ? Lang.T("閉じる窓がありませんでした") : Lang.F("{0} 件の窓を閉じました", closed), false);
        }

        private void Arrange()
        {
            if (_owner == null)
            {
                return;
            }
            bool any = false;
            foreach (string id in _zones)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    any = true;
                }
            }
            if (!any)
            {
                SetStatus(Lang.T("区画に登録した物を入れてください"), true);
                return;
            }
            _config.AreaShape = _shape;
            // 並べる先は、丸いボタンのいる画面（2026-11-25 の利用者の指示）
            Rectangle area = _owner.ButtonArea();
            // 区画パネルが並べた窓を隠さないよう、先に隠す
            Hide();
            _owner.Arrange(_shape, new List<string>(_zones), area);
        }

        private void Saved()
        {
            if (_owner != null)
            {
                _owner.ConfigChanged();
            }
        }

        private void SetStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.ForeColor = isError ? Theme.ErrorText : _theme.Text;
        }

        private Button SmallButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(64, 30), Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 6, 0) };
            button.Click += click;
            return button;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _zoneMenu.Dispose();
                _zoneTip.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>登録した物の一覧の行（グループと、その中の 1 つ）</summary>
        private sealed class EntryItem
        {
            public EntryItem(Job job, Entry entry)
            {
                Job = job;
                Entry = entry;
            }

            public Job Job { get; private set; }
            public Entry Entry { get; private set; }

            public override string ToString()
            {
                return Job.Name + " / " + Entry.Name;
            }
        }
    }
}
