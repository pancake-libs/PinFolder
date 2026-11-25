using System;
using System.IO;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 画面 D：登録の編集（設定画面の［追加］［編集］で開く子画面）。
    /// </summary>
    internal sealed class EntryForm : Form, IFitDialog
    {
        private const int LabelX = 16;
        private const int FieldX = 104;
        private const int FieldWidth = 360;

        private readonly RadioButton _folderRadio;
        private readonly RadioButton _appRadio;
        private readonly TextBox _nameBox;
        private readonly TextBox _pathBox;
        private readonly Label _pathError;
        private readonly Label _argsLabel;
        private readonly TextBox _argsBox;
        private readonly HotkeyBox _hotkeyBox;
        private readonly Label _nameError;

        /// <summary>画面を作る。</summary>
        /// <param name="entry">直す物（足す時は null）</param>
        /// <param name="theme">見た目のスタイル</param>
        public EntryForm(Entry entry, Theme theme)
        {
            SuspendLayout();
            Ui.SetupDialog(this, Lang.T("登録の編集"), 480, 262);
            Panel body;
            _footer = Ui.CreateDialogLayout(this, out body);
            _body = body;

            body.Controls.Add(Ui.Label(Lang.T("種類"), LabelX, 18));
            _folderRadio = new RadioButton { Text = Lang.T("フォルダ"), Location = new System.Drawing.Point(FieldX, 15), AutoSize = true };
            _appRadio = new RadioButton { Text = Lang.T("アプリ"), Location = new System.Drawing.Point(FieldX + 96, 15), AutoSize = true };
            _folderRadio.CheckedChanged += (s, e) => UpdateKind();
            body.Controls.Add(_folderRadio);
            body.Controls.Add(_appRadio);

            body.Controls.Add(Ui.Label(Lang.T("表示名"), LabelX, 52));
            _nameBox = Ui.TextBox(FieldX, 48, FieldWidth);
            body.Controls.Add(_nameBox);
            _nameError = Ui.ErrorLabel(FieldX, 72, FieldWidth);
            body.Controls.Add(_nameError);

            body.Controls.Add(Ui.Label(Lang.T("場所"), LabelX, 98));
            _pathBox = Ui.TextBox(FieldX, 94, FieldWidth - 80);
            body.Controls.Add(_pathBox);
            body.Controls.Add(Ui.Button(Lang.T("参照"), FieldX + FieldWidth - 72, 92, 72, (s, e) => Browse()));
            _pathError = Ui.ErrorLabel(FieldX, 118, FieldWidth);
            body.Controls.Add(_pathError);

            _argsLabel = Ui.Label(Lang.T("引数"), LabelX, 144);
            body.Controls.Add(_argsLabel);
            _argsBox = Ui.TextBox(FieldX, 140, FieldWidth);
            body.Controls.Add(_argsBox);

            body.Controls.Add(Ui.Label(Lang.T("ホットキー"), LabelX, 180));
            _hotkeyBox = new HotkeyBox { Location = new System.Drawing.Point(FieldX, 176), Width = 180 };
            body.Controls.Add(_hotkeyBox);

            Button cancel = Ui.AddFooterButton(_footer, Lang.T("キャンセル"), null);
            cancel.DialogResult = DialogResult.Cancel;
            Button save = Ui.AddFooterButton(_footer, Lang.T("保存"), (s, e) => Save());
            save.Tag = UiTags.Primary;
            AcceptButton = save;
            CancelButton = cancel;

            Result = entry == null ? new Entry { Kind = EntryKinds.Folder } : entry;
            _folderRadio.Checked = Result.Kind == EntryKinds.Folder;
            _appRadio.Checked = Result.Kind != EntryKinds.Folder;
            _nameBox.Text = Result.Name ?? string.Empty;
            _pathBox.Text = Result.Path ?? string.Empty;
            _argsBox.Text = Result.Args ?? string.Empty;
            _hotkeyBox.Text = Result.Hotkey ?? string.Empty;
            UpdateKind();

            theme.Apply(this);
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>保存した後の登録（［保存］で閉じた時だけ使う）</summary>
        public Entry Result { get; private set; }

        private readonly Panel _body;
        private readonly FlowLayoutPanel _footer;

        protected override void OnLoad(EventArgs e)
        {
            // 画面の倍率・文字の大きさで項目やボタンが隠れないよう、中身に合わせて大きさを決める
            Ui.FitToContent(this, _body, _footer);
            base.OnLoad(e);
        }

        /// <summary>大きさの確かめ（--check-layout）で使う：大きさを中身に合わせる。</summary>
        public void FitForCheck()
        {
            Ui.FitToContent(this, _body, _footer);
        }

        private bool IsFolder
        {
            get { return _folderRadio.Checked; }
        }

        private void UpdateKind()
        {
            _argsLabel.Visible = !IsFolder;
            _argsBox.Visible = !IsFolder;
        }

        private void Browse()
        {
            if (IsFolder)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    if (Directory.Exists(_pathBox.Text))
                    {
                        dialog.SelectedPath = _pathBox.Text;
                    }
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        SetPath(dialog.SelectedPath);
                    }
                }
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Lang.T("アプリ (*.exe)|*.exe|すべてのファイル (*.*)|*.*");
                if (File.Exists(_pathBox.Text))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(_pathBox.Text);
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetPath(dialog.FileName);
                }
            }
        }

        private void SetPath(string path)
        {
            _pathBox.Text = path;
            if (_nameBox.Text.Trim().Length == 0)
            {
                _nameBox.Text = DefaultName(path, IsFolder);
            }
        }

        /// <summary>場所から表示名の初めの値を作る（フォルダはフォルダ名、ファイルは拡張子を除いた名前）</summary>
        /// <param name="path">場所</param>
        /// <param name="isFolder">true＝フォルダ（名前の「.」を拡張子として扱わない）</param>
        public static string DefaultName(string path, bool isFolder)
        {
            string trimmed = path.TrimEnd('\\');
            string name = isFolder ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        private void Save()
        {
            _nameError.Text = string.Empty;
            _pathError.Text = string.Empty;
            string name = _nameBox.Text.Trim();
            string path = _pathBox.Text.Trim();
            bool ok = true;
            if (name.Length == 0)
            {
                _nameError.Text = Lang.T("表示名を入れてください");
                ok = false;
            }
            if (IsFolder && !Directory.Exists(path))
            {
                _pathError.Text = Lang.T("フォルダがありません");
                ok = false;
            }
            else if (!IsFolder && !File.Exists(path))
            {
                _pathError.Text = Lang.T("ファイルがありません");
                ok = false;
            }
            if (!ok)
            {
                return;
            }
            Result.Kind = IsFolder ? EntryKinds.Folder : EntryKinds.App;
            Result.Name = name;
            Result.Path = path;
            Result.Args = IsFolder ? string.Empty : _argsBox.Text.Trim();
            Result.Hotkey = _hotkeyBox.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
