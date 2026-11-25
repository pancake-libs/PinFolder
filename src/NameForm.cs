using System;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 名前を 1 つ入れる子画面（区画のセットの［別名で保存］で開く）。
    /// </summary>
    internal sealed class NameForm : Form, IFitDialog
    {
        private const int LabelX = 16;
        private const int FieldX = 80;
        private const int FieldWidth = 384;

        private readonly Panel _body;
        private readonly FlowLayoutPanel _footer;
        private readonly TextBox _nameBox;
        private readonly Label _nameError;

        /// <summary>画面を作る。</summary>
        /// <param name="title">題名</param>
        /// <param name="initial">初めに入れておく名前</param>
        /// <param name="theme">見た目のスタイル</param>
        public NameForm(string title, string initial, Theme theme)
        {
            SuspendLayout();
            Ui.SetupDialog(this, title, 480, 120);
            _footer = Ui.CreateDialogLayout(this, out _body);

            _body.Controls.Add(Ui.Label(Lang.T("名前"), LabelX, 20));
            _nameBox = Ui.TextBox(FieldX, 16, FieldWidth);
            _nameBox.Text = initial ?? string.Empty;
            _body.Controls.Add(_nameBox);
            _nameError = Ui.ErrorLabel(FieldX, 42, FieldWidth);
            _body.Controls.Add(_nameError);

            Button cancel = Ui.AddFooterButton(_footer, Lang.T("キャンセル"), null);
            cancel.DialogResult = DialogResult.Cancel;
            Button save = Ui.AddFooterButton(_footer, Lang.T("保存"), (s, e) => Save());
            save.Tag = UiTags.Primary;
            AcceptButton = save;
            CancelButton = cancel;

            theme.Apply(this);
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>入れた名前（［保存］で閉じた時だけ使う）</summary>
        public string Result { get; private set; }

        /// <summary>大きさの確かめ（--check-layout）で使う：大きさを中身に合わせる。</summary>
        public void FitForCheck()
        {
            Ui.FitToContent(this, _body, _footer);
        }

        protected override void OnLoad(EventArgs e)
        {
            Ui.FitToContent(this, _body, _footer);
            base.OnLoad(e);
        }

        private void Save()
        {
            string name = _nameBox.Text.Trim();
            if (name.Length == 0)
            {
                _nameError.Text = Lang.T("名前を入れてください");
                return;
            }
            Result = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
