using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// 画面の大きさの確かめ（PinFolder.exe --check-layout）。
    /// 画面の倍率 100%〜200% を見立てて子画面と大きな画面を作り、すべてのボタンに届くか（画面の中に見えるか、巻き取りの面の中なら面が見えるか）、文字が切れないかを調べる。
    /// 画面は出さない。結果は exe の横の layout_check.txt とログに書き、終了コードで返す（0＝問題なし、1＝隠れるボタンあり）。
    /// </summary>
    internal static class LayoutCheck
    {
        /// <summary>見立てる画面の倍率</summary>
        private static readonly float[] Factors = { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f };

        private const string ResultFileName = "layout_check.txt";
        private const string ShotFolderName = "layout_shots";

        /// <summary>撮る時に画面を出す位置（どの画面にも入らない所）</summary>
        private const int OffScreen = -32000;

        /// <summary>確かめを流す。</summary>
        /// <returns>0＝問題なし、1＝届かない・切れるボタンがある</returns>
        public static int Run()
        {
            StringBuilder report = new StringBuilder();
            // 主画面で測る（PinFolder は、マウスのある画面に子画面を出す。画面ごとに倍率が違う構成は対象外）
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            report.Append("PinFolder 画面の大きさの確かめ ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            report.Append("作業領域 ").Append(area.Width).Append('×').Append(area.Height).Append("（この大きさに収まるかを見る）、この PC の画面の倍率 ")
                .Append(Ui.Scale(100)).Append("%（下の倍率は、この倍率にさらに掛ける）\n\n");
            int problems = 0;
            AppConfig sample = SampleConfig();

            // 言語ごとに（英語・中国語は文字の幅が違い、ボタンが切れることがあるため）
            string savedLanguage = Lang.Current;
            try
            {
            foreach (string language in Lang.Ids)
            {
                Lang.Set(language);
                Theme.ResetFonts();
                foreach (float factor in Factors)
                {
                    foreach (Theme style in Theme.All)
                    {
                        problems += CheckForm("登録の編集", "entry", factor, style, area, report, () => new EntryForm(new Entry { Kind = EntryKinds.App, Name = "とても長い表示名の見本のアプリ", Path = @"C:\Program Files\Example\example.exe" }, style));
                        problems += CheckForm("グループの追加", "job", factor, style, area, report, () => new JobForm(null, style));
                        problems += CheckForm("設定", "settings", factor, style, area, report, () => new SettingsForm(ConfigStore.Clone(sample), style, new HashSet<string>()));
                        problems += CheckForm("大きな画面", "main", factor, style, area, report, () => new MainForm(null, style));
                        problems += CheckForm("区画パネル", "area", factor, style, area, report, () => new AreaPanel(null, ConfigStore.Clone(sample), style));
                        problems += CheckForm("名前の入力", "name", factor, style, area, report, () => new NameForm(Lang.T("セットの名前"), "とても長いセットの名前の見本", style));
                    }
                }
            }
            }
            finally
            {
                // 途中で失敗しても、言語を元に戻す
                Lang.Set(savedLanguage);
                Theme.ResetFonts();
            }
            report.Append('\n').Append(problems == 0 ? "結果：すべてのボタンに届く" : "結果：届かない・切れるボタンが " + problems + " 件").Append('\n');
            try
            {
                File.WriteAllText(Path.Combine(AppPaths.ExeDir, ResultFileName), report.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLog.Error("画面の大きさの確かめ", ResultFileName, ex, "exe のフォルダに書き込めるか確かめてください");
            }
            AppLog.Info("画面の大きさの確かめ", "子画面と大きな画面", problems == 0 ? "すべてのボタンに届く" : "届かない・切れるボタンが " + problems + " 件（" + ResultFileName + "）");
            return problems == 0 ? 0 : 1;
        }

        private static int CheckForm(string name, string fileKey, float factor, Theme theme, Rectangle area, StringBuilder report, Func<Form> create)
        {
            int problems = 0;
            using (Form form = create())
            {
                IntPtr handle = form.Handle;
                // 倍率を見立てる：文字と部品の位置・大きさを同じ割合で大きくする
                Font scaled = new Font(form.Font.FontFamily, form.Font.Size * factor, form.Font.Style);
                form.Font = scaled;
                ScaleExplicitFonts(form, factor);
                form.Scale(new SizeF(factor, factor));
                IFitDialog dialog = form as IFitDialog;
                if (dialog != null)
                {
                    dialog.FitForCheck();
                }
                else
                {
                    // 大きな画面は作業領域に収める（ShowMain と同じ）
                    form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
                }
                form.PerformLayout();

                string label = name + "（" + Lang.Current + "・倍率 " + (int)(factor * 100) + "%・" + theme.Name + "）";
                if (form.Width > area.Width || form.Height > area.Height)
                {
                    report.Append("NG ").Append(label).Append("：画面が作業領域より大きい ").Append(form.Width).Append('×').Append(form.Height).Append('\n');
                    problems++;
                }
                foreach (Button button in Buttons(form))
                {
                    string problem = ButtonProblem(form, button);
                    if (problem != null)
                    {
                        report.Append("NG ").Append(label).Append("：［").Append(button.Text).Append("］").Append(problem).Append('\n');
                        problems++;
                    }
                }
                if (problems == 0)
                {
                    report.Append("OK ").Append(label).Append("：").Append(form.Width).Append('×').Append(form.Height).Append('\n');
                }
                if (Math.Abs(factor - 1f) < 0.01f && theme.Id == Theme.DefaultId)
                {
                    SaveShot(form, fileKey);
                }
                form.Font = Control.DefaultFont;
                scaled.Dispose();
            }
            return problems;
        }

        /// <summary>画面の絵を exe の横の layout_shots に残す（言語ごとの見た目を確かめるため。倍率 100%・標準のスタイルだけ）。</summary>
        private static void SaveShot(Form form, string fileKey)
        {
            try
            {
                string folder = Path.Combine(AppPaths.ExeDir, ShotFolderName);
                Directory.CreateDirectory(folder);
                // 出していない画面は中の部品が描かれないため、画面の外の位置に出してから撮り、すぐ隠す
                Size size = form.Size;
                form.StartPosition = FormStartPosition.Manual;
                form.ShowInTaskbar = false;
                form.Location = new Point(OffScreen, OffScreen);
                form.Show();
                // 子画面は出す時（OnLoad）に作業領域の中央へ動くため、画面の外へ戻してから撮る
                form.Location = new Point(OffScreen, OffScreen);
                form.Size = size;
                Application.DoEvents();
                using (Bitmap shot = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
                {
                    form.DrawToBitmap(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                    shot.Save(Path.Combine(folder, Lang.Current + "_" + fileKey + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                form.Hide();
            }
            catch (Exception ex)
            {
                AppLog.Warn("画面の大きさの確かめ", fileKey, "画面の絵を残せなかった：" + ex.Message, "確かめの結果（layout_check.txt）には影響しません");
            }
        }

        /// <summary>ボタンの問題（見えない・切れる）。問題が無ければ null。</summary>
        private static string ButtonProblem(Form form, Button button)
        {
            Size preferred = button.GetPreferredSize(Size.Empty);
            if (button.Width + 1 < preferred.Width || button.Height + 1 < preferred.Height)
            {
                return "の文字が切れる（" + button.Width + "×" + button.Height + "、要る大きさ " + preferred.Width + "×" + preferred.Height + "）";
            }
            // 巻き取りの面の中のボタンは、巻き取れば届くので、面そのものが画面の中に見えるかを見る
            Control target = ScrollParent(button, form) ?? (Control)button;
            Rectangle inForm = BoundsIn(target, form);
            if (!form.ClientRectangle.Contains(inForm))
            {
                return "が画面の外にはみ出す（位置 " + inForm.X + "," + inForm.Y + "、大きさ " + inForm.Width + "×" + inForm.Height
                    + "、画面 " + form.ClientSize.Width + "×" + form.ClientSize.Height + "）";
            }
            return null;
        }

        /// <summary>部品の、画面（form の中身）の中での位置と大きさ（画面を出さずに測れるよう、親までの位置を足す）。</summary>
        private static Rectangle BoundsIn(Control control, Form form)
        {
            Point location = control.Location;
            Control parent = control.Parent;
            while (parent != null && parent != form)
            {
                location.Offset(parent.Location);
                parent = parent.Parent;
            }
            return new Rectangle(location, control.Size);
        }

        private static ScrollableControl ScrollParent(Control control, Form form)
        {
            Control parent = control.Parent;
            while (parent != null && parent != form)
            {
                ScrollableControl scrollable = parent as ScrollableControl;
                if (scrollable != null && scrollable.AutoScroll)
                {
                    return scrollable;
                }
                parent = parent.Parent;
            }
            return null;
        }

        private static IEnumerable<Button> Buttons(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null)
                {
                    yield return button;
                }
                foreach (Button inner in Buttons(child))
                {
                    yield return inner;
                }
            }
        }

        /// <summary>太字など、部品に直に付けた文字も同じ割合で大きくする。</summary>
        private static void ScaleExplicitFonts(Control parent, float factor)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.Font != parent.Font && child.Font.Bold)
                {
                    child.Font = new Font(child.Font.FontFamily, child.Font.Size * factor, child.Font.Style);
                }
                ScaleExplicitFonts(child, factor);
            }
        }

        private static AppConfig SampleConfig()
        {
            AppConfig config = new AppConfig();
            for (int i = 1; i <= 6; i++)
            {
                Job job = new Job { Name = "見本のグループ " + i };
                for (int j = 1; j <= 4; j++)
                {
                    job.Entries.Add(new Entry { Kind = EntryKinds.Folder, Name = "フォルダ " + j, Path = @"C:\Example\" + i + @"\" + j, Hotkey = "Ctrl+Alt+" + j });
                }
                config.Jobs.Add(job);
            }
            return config;
        }
    }
}
