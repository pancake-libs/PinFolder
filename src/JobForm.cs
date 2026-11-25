using System;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// グループの追加・名前の変更の子画面（大きな画面の［＋ グループを追加］と、グループの右クリックの［名前を変える］で開く）。
    /// </summary>
    internal sealed class JobForm : Form, IFitDialog
    {
        private const int LabelX = 16;
        private const int FieldX = 80;
        private const int FieldWidth = 384;

        private readonly Panel _body;
        private readonly FlowLayoutPanel _footer;
        private readonly TextBox _nameBox;
        private readonly Label _nameError;

        /// <summary>画面を作る。</summary>
        /// <param name="job">名前を変えるグループ（足す時は null）</param>
        /// <param name="theme">見た目のスタイル</param>
        public JobForm(Job job, Theme theme)
        {
            SuspendLayout();
            Ui.SetupDialog(this, job == null ? Lang.T("グループの追加") : Lang.T("名前の変更"), 480, 120);
            _footer = Ui.CreateDialogLayout(this, out _body);

            _body.Controls.Add(Ui.Label(Lang.T("名前"), LabelX, 20));
            _nameBox = Ui.TextBox(FieldX, 16, FieldWidth);
            _body.Controls.Add(_nameBox);
            _nameError = Ui.ErrorLabel(FieldX, 42, FieldWidth);
            _body.Controls.Add(_nameError);

            Button cancel = Ui.AddFooterButton(_footer, Lang.T("キャンセル"), null);
            cancel.DialogResult = DialogResult.Cancel;
            Button save = Ui.AddFooterButton(_footer, Lang.T("保存"), (s, e) => Save());
            save.Tag = UiTags.Primary;
            AcceptButton = save;
            CancelButton = cancel;

            Result = job ?? new Job();
            _nameBox.Text = Result.Name ?? string.Empty;

            theme.Apply(this);
            // 画面ごとの倍率に合わせる（倍率の違う画面へ動かした時に作り直す）
            DpiWatcher.Attach(this);
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>保存した後のグループ</summary>
        public Job Result { get; private set; }

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

        private void Save()
        {
            string name = _nameBox.Text.Trim();
            if (name.Length == 0)
            {
                _nameError.Text = Lang.T("名前を入れてください");
                return;
            }
            Result.Name = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
