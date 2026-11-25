using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// PinFolder の大きな画面（浮かぶボタンの左クリックで開く）。
    /// 左にグループの一覧、右に選んだグループの登録を大きな札で並べる。札を押すと開く（開いていれば前面に出す）。
    /// フォルダ・EXE を引っぱって入れるか、［＋ フォルダを追加］で登録する。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int FormWidth = 1000;
        private const int FormHeight = 680;
        private const int MinWidth = 720;
        private const int MinHeight = 480;
        private const int LeftWidth = 280;
        private const int JobRowHeight = 40;
        private const int CardWidth = 320;
        private const int CardHeight = 84;
        private const int CardIconSize = 32;
        private const int OpenStripWidth = 5;
        private const int SearchIconSize = 16;
        private const int SearchIconColumn = 32;
        private const int SearchHeight = 32;
        private const int SearchPadding = 6;
        private const int ActionGroupGap = 16;
        private const int CardGap = 6;
        private const string DropText = "＋ フォルダ・EXE をドロップ";

        private readonly PinFolderContext _owner;
        private readonly TextBox _search;
        private readonly Panel _searchBox;
        private readonly Panel _jobListFrame;
        private readonly TableLayoutPanel _right;
        private readonly FlowLayoutPanel _actions;
        private readonly ListBox _jobList;
        private readonly Label _jobTitle;
        private readonly Button _openAllButton;
        private readonly Button _closeAllButton;
        private readonly Panel _content;
        private readonly FlowLayoutPanel _cards;
        private readonly Panel _empty;
        private readonly Label _status;
        private readonly ToolTip _tip = new ToolTip();
        private readonly ContextMenuStrip _jobMenu;
        private readonly ContextMenuStrip _cardMenu;
        private Theme _theme;
        private OpenSnapshot _snapshot;
        private Entry _menuEntry;
        private bool _positioned;

        /// <summary>画面を作る。</summary>
        /// <param name="owner">常駐の本体（設定・開く・閉じる・保存を頼む先）</param>
        /// <param name="theme">見た目のスタイル</param>
        public MainForm(PinFolderContext owner, Theme theme)
        {
            _owner = owner;
            _theme = theme;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = AppPaths.AppName;
            Icon = AppPaths.WindowIcon();
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(FormWidth, FormHeight);
            MinimumSize = new Size(MinWidth, MinHeight);
            KeyPreview = true;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftWidth));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- 上：検索と設定 ----
            TableLayoutPanel top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Padding = new Padding(12, 10, 12, 10), Tag = UiTags.Header };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _search = new TextBox { BorderStyle = BorderStyle.None, Margin = new Padding(0) };
            _search.TextChanged += (s, e) => FillJobs(SelectedJob);
            _searchBox = BuildSearchBox(_search);
            Button settings = new Button { Text = Lang.T("設定"), AutoSize = true, Padding = new Padding(10, 2, 10, 2), Anchor = AnchorStyles.Left };
            settings.Click += (s, e) => _owner.OpenSettings();
            top.Controls.Add(_searchBox, 0, 0);
            top.Controls.Add(settings, 1, 0);
            root.Controls.Add(top, 0, 0);
            root.SetColumnSpan(top, 2);

            // ---- 左：グループの一覧 ----
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12, 8, 6, 8) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label { Text = Lang.T("グループ"), AutoSize = true, Tag = UiTags.Bold, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
            _jobList = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                IntegralHeight = false,
                AllowDrop = true,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0)
            };
            _jobList.DrawItem += DrawJob;
            _jobList.SelectedIndexChanged += (s, e) => FillCards();
            _jobList.DragEnter += OnDragEnter;
            _jobList.DragDrop += OnJobListDrop;
            _jobList.MouseDown += OnJobListMouseDown;
            // 一覧の枠は薄い線（枠の色の下地に 1px の余白を取って一覧を置く）。下のボタンと左右の端をそろえる
            _jobListFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            _jobListFrame.Controls.Add(_jobList);
            left.Controls.Add(_jobListFrame, 0, 1);
            Button addJob = new Button { Text = Lang.T("＋ グループを追加"), Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(4) };
            addJob.Click += (s, e) => AddJob();
            left.Controls.Add(addJob, 0, 2);
            root.Controls.Add(left, 0, 1);

            // ---- 右：登録した物 ----
            // 題名・ボタンの帯・札の 3 段。ボタンの帯の高さは、今の幅で折り返した時の高さを毎回計算して決める
            // （自動の高さに任せると、狭い幅で折り返した時の高さが残り、札との間が大きく空いた）
            _right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6, 8, 12, 8) };
            _right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            _right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _jobTitle = new Label { AutoSize = true, Tag = UiTags.Bold, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 0, 6) };
            _right.Controls.Add(_jobTitle, 0, 0);
            _actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            Button addFolder = ActionButton(Lang.T("＋ フォルダを追加"), (s, e) => AddFolder());
            Button addApp = ActionButton(Lang.T("＋ アプリを追加"), (s, e) => AddApp());
            // 「足す」2 つと「まとめて開く・閉じる」2 つの間を空けて、組に分ける
            addApp.Margin = new Padding(0, 0, ActionGroupGap, 6);
            _actions.Controls.Add(addFolder);
            _actions.Controls.Add(addApp);
            _openAllButton = ActionButton(Lang.T("すべて開く"), (s, e) => OpenAll());
            _openAllButton.Tag = UiTags.Soft;
            _closeAllButton = ActionButton(Lang.T("すべて閉じる"), (s, e) => CloseAll());
            _actions.Controls.Add(_openAllButton);
            _actions.Controls.Add(_closeAllButton);
            _right.Controls.Add(_actions, 0, 1);
            _right.SizeChanged += (s, e) => FitActionsHeight();
            _actions.FontChanged += (s, e) => FitActionsHeight();

            _content = new Panel { Dock = DockStyle.Fill, AllowDrop = true, Tag = UiTags.Surface, Margin = new Padding(0, 2, 0, 0) };
            _content.DragEnter += OnDragEnter;
            _content.DragDrop += OnContentDrop;
            _cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8), AllowDrop = true, Tag = UiTags.Surface };
            _cards.ClientSizeChanged += (s, e) => FitCards();
            _cards.DragEnter += OnDragEnter;
            _cards.DragDrop += OnContentDrop;
            _empty = BuildEmptyPanel();
            _content.Controls.Add(_cards);
            _content.Controls.Add(_empty);
            _right.Controls.Add(_content, 0, 2);
            root.Controls.Add(_right, 1, 1);

            // ---- 下：知らせ ----
            _status = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12, 6, 12, 8) };
            root.Controls.Add(_status, 0, 2);
            root.SetColumnSpan(_status, 2);

            Controls.Add(root);

            _jobMenu = new ContextMenuStrip();
            _jobMenu.Items.Add(Lang.T("名前を変える"), null, (s, e) => RenameJob());
            _jobMenu.Items.Add(Lang.T("消す"), null, (s, e) => DeleteJob());
            _cardMenu = new ContextMenuStrip();
            _cardMenu.Items.Add(Lang.T("編集"), null, (s, e) => EditEntry(_menuEntry));
            _cardMenu.Items.Add(Lang.T("消す"), null, (s, e) => DeleteEntry(_menuEntry));

            _theme.Apply(this);
            ApplyFrameColors();
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
            _jobList.ItemHeight = DpiWatcher.Scale(this, JobRowHeight);
            FitSearchHeight(_searchBox, _search);
            FitActionsHeight();
        }

        /// <summary>検索の欄（枠と虫めがねの印の中に、枠の無い入力の欄を置く）。</summary>
        private Panel BuildSearchBox(TextBox input)
        {
            Panel box = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 12, 2), Cursor = Cursors.IBeam };
            TableLayoutPanel inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(1) };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SearchIconColumn));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Panel icon = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.Transparent };
            icon.Paint += (s, e) => DrawSearchIcon(e.Graphics, icon.ClientRectangle);
            icon.Click += (s, e) => input.Focus();
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            inner.Controls.Add(icon, 0, 0);
            inner.Controls.Add(input, 1, 0);
            inner.Click += (s, e) => input.Focus();
            box.Controls.Add(inner);
            box.Click += (s, e) => input.Focus();
            // 高さは文字の高さに合わせる（倍率が変わって文字が大きくなった時も）
            input.FontChanged += (s, e) => FitSearchHeight(box, input);
            box.Paint += (s, e) =>
            {
                Rectangle rect = box.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                using (Pen pen = new Pen(input.Focused ? _theme.PrimaryBack : _theme.ControlBorder))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            };
            input.GotFocus += (s, e) => box.Invalidate();
            input.LostFocus += (s, e) => box.Invalidate();
            // 初めは 96dpi の値（画面の倍率の掛け算は、作り終えた時に Windows Forms が行う）。作り終えた後に FitSearchHeight で合わせ直す
            box.Height = SearchHeight;
            return box;
        }

        /// <summary>検索の欄の高さを、入力の文字の高さ＋上下の余白にする（画面を作り終えた後・文字が変わった時）。</summary>
        private void FitSearchHeight(Panel box, TextBox input)
        {
            int pad = DpiWatcher.Scale(this, SearchPadding);
            box.Height = input.PreferredHeight + pad * 2;
        }

        /// <summary>虫めがねの印（円と柄）を描く。</summary>
        private void DrawSearchIcon(Graphics g, Rectangle area)
        {
            int size = DpiWatcher.Scale(this, SearchIconSize);
            float width = Math.Max(1.5f, size / 8f);
            float ring = size * 0.62f;
            float left = area.Left + (area.Width - size) / 2f;
            float top = area.Top + (area.Height - size) / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(_theme.SubText, width))
            {
                line.StartCap = LineCap.Round;
                line.EndCap = LineCap.Round;
                g.DrawEllipse(line, left, top, ring, ring);
                float handle = ring * 0.85f;
                g.DrawLine(line, left + handle, top + handle, left + size - width / 2f, top + size - width / 2f);
            }
        }

        /// <summary>ボタンの帯の高さを、今の幅で折り返した時の高さに合わせる（札との間を空けない）。</summary>
        private void FitActionsHeight()
        {
            int width = _right.ClientSize.Width - _right.Padding.Horizontal;
            if (width <= 0)
            {
                return;
            }
            int height = _actions.GetPreferredSize(new Size(width, 0)).Height;
            if (Math.Abs(_right.RowStyles[1].Height - height) > 0.5f)
            {
                _right.RowStyles[1].Height = height;
            }
        }

        /// <summary>札の幅を、一覧の幅いっぱいに同じ幅で並ぶように合わせる（右に余りを残さない）。</summary>
        private void FitCards()
        {
            if (_cards.Controls.Count == 0)
            {
                return;
            }
            int gap = DpiWatcher.Scale(this, CardGap);
            // 縦のスクロールバーが後から出ても折り返さないよう、その幅を先に引いておく
            int scrollBar = (int)Math.Round(SystemInformation.VerticalScrollBarWidth * DpiWatcher.FactorOf(this));
            int available = _cards.ClientSize.Width - _cards.Padding.Horizontal - (_cards.VerticalScroll.Visible ? 0 : scrollBar);
            int minWidth = DpiWatcher.Scale(this, CardWidth);
            int columns = Math.Max(1, available / (minWidth + gap * 2));
            int width = Math.Max(minWidth / 2, available / columns - gap * 2);
            _cards.SuspendLayout();
            foreach (Control card in _cards.Controls)
            {
                if (card.Width != width)
                {
                    card.Width = width;
                }
            }
            _cards.ResumeLayout(true);
        }

        /// <summary>スタイルの色で、一覧と検索の欄の枠の色を当てる（スタイルを替えた時も）。</summary>
        private void ApplyFrameColors()
        {
            _jobListFrame.BackColor = _theme.ControlBorder;
            _searchBox.BackColor = _theme.ControlBack;
            _search.BackColor = _theme.ControlBack;
            _searchBox.Invalidate();
        }

        private Job SelectedJob
        {
            get { return _jobList.SelectedItem as Job; }
        }

        /// <summary>見た目のスタイルを替える。</summary>
        public void SetTheme(Theme theme)
        {
            _theme = theme;
            _theme.Apply(this);
            ApplyFrameColors();
            _status.ForeColor = _theme.Text;
            // スタイルで文字の大きさが変わるため、検索の欄とボタンの帯の高さを合わせ直す
            FitSearchHeight(_searchBox, _search);
            FitActionsHeight();
            RefreshAll();
        }

        /// <summary>画面を出す（初めはマウスのある画面の中央。最小化していれば戻す）。</summary>
        public void ShowMain()
        {
            if (!_positioned)
            {
                // 画面の倍率が大きいと 1000×680 が作業領域より大きくなるため、作業領域に収める
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                // 最小の大きさも作業領域を超えないようにする（超えると縮められず、はみ出す）
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
            RefreshAll();
            Show();
            Activate();
        }

        /// <summary>設定が替わった時などに、一覧と札を作り直す（選んでいたグループは名前で選び直す）。</summary>
        public void RefreshAll()
        {
            Job selected = SelectedJob;
            string selectedName = selected == null ? null : selected.Name;
            Job again = null;
            foreach (Job job in _owner.Config.Jobs)
            {
                if (job == selected || (again == null && job.Name == selectedName))
                {
                    again = job;
                }
            }
            FillJobs(again);
        }

        /// <summary>下の帯に知らせを出す。</summary>
        /// <param name="text">知らせの文</param>
        /// <param name="isError">true＝誤り（赤）</param>
        public void SetStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.ForeColor = isError ? Theme.ErrorText : _theme.Text;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // ほかの画面で窓を開け閉めした後に戻った時、開いている印を付け直す
            if (Visible)
            {
                FillCards();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // ✕ は隠すだけ（終了は浮かぶボタンの右クリック）
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

        // ================= グループの一覧 =================

        private void FillJobs(Job select)
        {
            string filter = _search.Text.Trim();
            _jobList.ItemHeight = DpiWatcher.Scale(this, JobRowHeight);
            _jobList.BeginUpdate();
            _jobList.Items.Clear();
            foreach (Job job in _owner.JobsByRecent())
            {
                if (Matches(job, filter))
                {
                    _jobList.Items.Add(job);
                }
            }
            _jobList.EndUpdate();
            if (select != null && _jobList.Items.Contains(select))
            {
                _jobList.SelectedItem = select;
            }
            else if (_jobList.Items.Count > 0)
            {
                _jobList.SelectedIndex = 0;
            }
            else
            {
                FillCards();
            }
        }

        private static bool Matches(Job job, string filter)
        {
            if (filter.Length == 0)
            {
                return true;
            }
            if ((job.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            foreach (Entry entry in job.Entries)
            {
                if ((entry.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawJob(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _jobList.Items.Count)
            {
                return;
            }
            Job job = (Job)_jobList.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (SolidBrush back = new SolidBrush(selected ? _theme.RowSelected : _theme.ControlBack))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            if (selected)
            {
                using (SolidBrush strip = new SolidBrush(_theme.PrimaryBack))
                {
                    e.Graphics.FillRectangle(strip, e.Bounds.Left, e.Bounds.Top, DpiWatcher.Scale(this, 4), e.Bounds.Height);
                }
            }
            Rectangle textRect = new Rectangle(e.Bounds.Left + DpiWatcher.Scale(this, 12), e.Bounds.Top, e.Bounds.Width - DpiWatcher.Scale(this, 60), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, job.Name, _theme.BoldFont, textRect, _theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            Rectangle countRect = new Rectangle(e.Bounds.Right - DpiWatcher.Scale(this, 52), e.Bounds.Top, DpiWatcher.Scale(this, 44), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, Lang.F("{0} 件", job.Entries.Count), _theme.Font, countRect, _theme.SubText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        private void OnJobListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            int index = _jobList.IndexFromPoint(e.Location);
            if (index < 0)
            {
                return;
            }
            _jobList.SelectedIndex = index;
            _jobMenu.Show(_jobList, e.Location);
        }

        private void AddJob()
        {
            using (JobForm form = new JobForm(null, _theme))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _search.Text = string.Empty;
                _owner.Config.Jobs.Add(form.Result);
                _owner.ConfigChanged();
                AppLog.Info("グループの追加", form.Result.Name, "足した");
                FillJobs(form.Result);
                SetStatus(Lang.F("「{0}」を足しました", form.Result.Name), false);
            }
        }

        private void RenameJob()
        {
            Job job = SelectedJob;
            if (job == null)
            {
                return;
            }
            string oldName = job.Name;
            using (JobForm form = new JobForm(job, _theme))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _owner.ConfigChanged();
                    AppLog.Info("グループの名前の変更", oldName, "「" + job.Name + "」に変えた");
                    FillJobs(job);
                }
            }
        }

        private void DeleteJob()
        {
            Job job = SelectedJob;
            if (job == null)
            {
                return;
            }
            if (MessageBox.Show(this, Lang.F("「{0}」と、その登録をすべて消しますか。", job.Name), AppPaths.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            _owner.Config.Jobs.Remove(job);
            _owner.ConfigChanged();
            AppLog.Info("グループの削除", job.Name, "登録 " + job.Entries.Count + " 件と一緒に消した");
            FillJobs(null);
            SetStatus(Lang.F("「{0}」を消しました", job.Name), false);
        }

        // ================= 登録した物（札） =================

        private void FillCards()
        {
            Job job = SelectedJob;
            _jobTitle.Text = job == null ? Lang.T("グループがありません") : job.Name;
            _openAllButton.Enabled = job != null && job.Entries.Count > 0;
            _closeAllButton.Enabled = job != null && job.Entries.Count > 0;

            _cards.SuspendLayout();
            // 作り直す札の説明（ToolTip）を外す（消した札を ToolTip が持ち続けないように）
            _tip.RemoveAll();
            Control[] old = new Control[_cards.Controls.Count];
            _cards.Controls.CopyTo(old, 0);
            _cards.Controls.Clear();
            foreach (Control control in old)
            {
                control.Dispose();
            }
            bool hasEntries = job != null && job.Entries.Count > 0;
            if (hasEntries)
            {
                _snapshot = OpenSnapshot.Take();
                foreach (Entry entry in job.Entries)
                {
                    _cards.Controls.Add(BuildCard(job, entry));
                }
            }
            _cards.ResumeLayout(true);
            FitCards();
            _cards.Visible = hasEntries;
            _empty.Visible = !hasEntries;
            if (hasEntries)
            {
                _cards.BringToFront();
            }
            else
            {
                _empty.BringToFront();
            }
        }

        private Control BuildCard(Job job, Entry entry)
        {
            bool open = _snapshot != null && _snapshot.IsOpen(entry);
            Panel card = new Panel
            {
                Size = new Size(DpiWatcher.Scale(this, CardWidth), DpiWatcher.Scale(this, CardHeight)),
                Margin = new Padding(DpiWatcher.Scale(this, CardGap)),
                BackColor = open ? Theme.AccentRedBack : _theme.ControlBack,
                Cursor = Cursors.Hand
            };
            Color restBack = card.BackColor;
            bool hover = false;
            // 枠は薄い線。マウスを当てている間は、地の色を変えて枠を主の色にする
            card.Paint += (s, e) =>
            {
                Rectangle rect = card.ClientRectangle;
                rect.Width -= 1;
                rect.Height -= 1;
                using (Pen pen = new Pen(hover ? _theme.PrimaryBack : _theme.ControlBorder))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            };
            if (open)
            {
                card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = DpiWatcher.Scale(this, OpenStripWidth), BackColor = Theme.AccentRed });
            }
            int pad = DpiWatcher.Scale(this, 12);
            int iconSize = DpiWatcher.Scale(this, CardIconSize);
            PictureBox icon = new PictureBox
            {
                Image = IconCache.Get(entry, iconSize),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(pad + (open ? DpiWatcher.Scale(this, OpenStripWidth) : 0), (card.Height - iconSize) / 2),
                Size = new Size(iconSize, iconSize)
            };
            int textLeft = icon.Right + pad;
            int textWidth = card.Width - textLeft - pad;
            Label name = new Label
            {
                Text = entry.Name,
                Font = _theme.BoldFont,
                ForeColor = _theme.Text,
                AutoEllipsis = true,
                Location = new Point(textLeft, DpiWatcher.Scale(this, 10)),
                Size = new Size(textWidth, DpiWatcher.Scale(this, 22))
            };
            // 場所は長いので、途中を省いて末尾（フォルダの名前の側）を見せる。小さく薄い文字にする
            Label path = new PathLabel
            {
                Text = entry.Path,
                Font = _theme.SmallFont,
                ForeColor = _theme.SubText,
                Location = new Point(textLeft, DpiWatcher.Scale(this, 34)),
                Size = new Size(textWidth, DpiWatcher.Scale(this, 20))
            };
            string stateText = EntryKinds.Label(entry.Kind);
            if (!string.IsNullOrEmpty(entry.Hotkey))
            {
                stateText += "　" + entry.Hotkey;
            }
            if (open)
            {
                stateText += "　" + Lang.T("● 開いている");
            }
            Label state = new Label
            {
                Text = stateText,
                Font = _theme.SmallFont,
                ForeColor = open ? Theme.AccentRed : _theme.SubText,
                AutoEllipsis = true,
                Location = new Point(textLeft, DpiWatcher.Scale(this, 56)),
                Size = new Size(textWidth, DpiWatcher.Scale(this, 20))
            };
            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(path);
            card.Controls.Add(state);
            _tip.SetToolTip(card, entry.Path);

            // 札のどこを押しても開く。右クリックは編集・消すのメニュー
            MouseEventHandler click = (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_owner.OpenEntry(job, entry))
                    {
                        // 押された札そのものを作り直すため、クリックの処理を抜けてから行う
                        BeginInvoke(new Action(FillCards));
                    }
                }
                else if (e.Button == MouseButtons.Right)
                {
                    _menuEntry = entry;
                    _cardMenu.Show(Cursor.Position);
                }
            };
            // マウスが札の中にある間だけ色を変える（中の文字・絵に移った時は、札から出たとみなさない）
            EventHandler enter = (s, e) =>
            {
                if (!hover)
                {
                    hover = true;
                    card.BackColor = open ? restBack : _theme.ControlHover;
                    card.Invalidate();
                }
            };
            EventHandler leave = (s, e) =>
            {
                if (hover && !card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)))
                {
                    hover = false;
                    card.BackColor = restBack;
                    card.Invalidate();
                }
            };
            card.MouseClick += click;
            card.MouseEnter += enter;
            card.MouseLeave += leave;
            foreach (Control child in card.Controls)
            {
                child.MouseClick += click;
                child.MouseEnter += enter;
                child.MouseLeave += leave;
                child.Cursor = Cursors.Hand;
                // 札の幅を一覧に合わせて変えた時に、文字の幅も広げる
                if (child is Label)
                {
                    child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                }
            }
            return card;
        }

        private Panel BuildEmptyPanel()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, AllowDrop = true, Tag = UiTags.Surface };
            panel.DragEnter += OnDragEnter;
            panel.DragDrop += OnContentDrop;
            panel.Paint += (s, e) =>
            {
                Rectangle rect = panel.ClientRectangle;
                rect.Inflate(-DpiWatcher.Scale(this, 24), -DpiWatcher.Scale(this, 24));
                using (Pen pen = new Pen(_theme.ControlBorder, DpiWatcher.Scale(this, 2)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(pen, rect);
                }
            };
            TableLayoutPanel center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent };
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            center.AllowDrop = true;
            center.DragEnter += OnDragEnter;
            center.DragDrop += OnContentDrop;
            Label drop = new Label { Text = Lang.T(DropText), AutoSize = true, Anchor = AnchorStyles.None, Tag = UiTags.Bold, Margin = new Padding(0, 0, 0, 12) };
            Button add = new Button { Text = Lang.T("＋ フォルダを追加"), AutoSize = true, Anchor = AnchorStyles.None, Tag = UiTags.Primary, Padding = new Padding(12, 6, 12, 6) };
            add.Click += (s, e) => AddFolder();
            center.Controls.Add(drop, 0, 1);
            center.Controls.Add(add, 0, 2);
            panel.Controls.Add(center);
            return panel;
        }

        private Button ActionButton(string text, EventHandler click)
        {
            Button button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 6, 6) };
            button.Click += click;
            return button;
        }

        // ================= 足す・直す・消す =================

        private void AddFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(SelectedJob, new[] { dialog.SelectedPath });
                }
            }
        }

        private void AddApp()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Lang.T("アプリ (*.exe)|*.exe|すべてのファイル (*.*)|*.*");
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddPaths(SelectedJob, dialog.FileNames);
                }
            }
        }

        /// <summary>
        /// フォルダ・ファイルをグループに登録する。グループが無い時は、最初の物の名前でグループを作る。
        /// 同じ場所が既に登録されていれば足さない。
        /// </summary>
        private void AddPaths(Job target, string[] paths)
        {
            List<Entry> entries = new List<Entry>();
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    entries.Add(new Entry { Kind = EntryKinds.Folder, Name = EntryForm.DefaultName(path, true), Path = path });
                }
                else if (File.Exists(path))
                {
                    entries.Add(new Entry { Kind = EntryKinds.App, Name = EntryForm.DefaultName(path, false), Path = path });
                }
            }
            if (entries.Count == 0)
            {
                SetStatus(Lang.T("フォルダ・ファイルではないため、登録できませんでした"), true);
                return;
            }
            if (target == null)
            {
                target = new Job { Name = entries[0].Name };
                _owner.Config.Jobs.Add(target);
            }
            int added = 0;
            foreach (Entry entry in entries)
            {
                if (Contains(target, entry.Path))
                {
                    continue;
                }
                target.Entries.Add(entry);
                added++;
            }
            _owner.ConfigChanged();
            _search.Text = string.Empty;
            FillJobs(target);
            AppLog.Info("登録", target.Name, added + " 件を足した");
            SetStatus(added == 0 ? Lang.T("もう登録してあります") : Lang.F("「{0}」に {1} 件を登録しました", target.Name, added), false);
        }

        private static bool Contains(Job job, string path)
        {
            string normalized = WindowFinder.NormalizePath(path);
            foreach (Entry entry in job.Entries)
            {
                if (WindowFinder.NormalizePath(entry.Path) == normalized)
                {
                    return true;
                }
            }
            return false;
        }

        private void EditEntry(Entry entry)
        {
            if (entry == null)
            {
                return;
            }
            using (EntryForm form = new EntryForm(entry, _theme))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _owner.ConfigChanged();
                    AppLog.Info("登録の編集", entry.Name, "場所 " + entry.Path);
                    // 場所が変わった時に新しい絵を読むため、覚えた絵を捨ててから作り直す
                    IconCache.Clear();
                    FillCards();
                }
            }
        }

        private void DeleteEntry(Entry entry)
        {
            Job job = SelectedJob;
            if (job == null || entry == null)
            {
                return;
            }
            if (MessageBox.Show(this, Lang.F("「{0}」の登録を消しますか（フォルダ・ファイルそのものは消しません）。", entry.Name), AppPaths.AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            job.Entries.Remove(entry);
            _owner.ConfigChanged();
            AppLog.Info("登録の削除", job.Name + " / " + entry.Name, "登録を消した（フォルダ・ファイルはそのまま）");
            FillJobs(job);
        }

        private void OpenAll()
        {
            Job job = SelectedJob;
            if (job != null)
            {
                _owner.OpenJobs(new List<Job> { job });
                FillCards();
            }
        }

        private void CloseAll()
        {
            Job job = SelectedJob;
            if (job != null)
            {
                _owner.CloseJobs(new List<Job> { job });
                SetStatus(Lang.F("「{0}」の窓を閉じました", job.Name), false);
            }
        }

        // ================= 引っぱって入れる =================

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnJobListDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null)
            {
                return;
            }
            // グループの行の上なら、そのグループに。空いた所なら、新しいグループを作る
            int index = _jobList.IndexFromPoint(_jobList.PointToClient(new Point(e.X, e.Y)));
            Job target = index >= 0 ? (Job)_jobList.Items[index] : null;
            AddPaths(target, paths);
        }

        private void OnContentDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null)
            {
                AddPaths(SelectedJob, paths);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                _jobMenu.Dispose();
                _cardMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 場所（パス）を出す札の文字。入りきらない時は途中を「…」で省き、末尾（フォルダの名前の側）を見せる
        /// （Label の AutoEllipsis は末尾を省くため、どの札も同じ先頭の部分しか見えなかった）。
        /// </summary>
        private sealed class PathLabel : Label
        {
            private const TextFormatFlags PathFlags = TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, PathFlags);
            }
        }
    }
}
