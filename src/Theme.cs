using System;
using System.Drawing;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>部品の役割（Control.Tag に入れて、スタイルの色の当て方を決める）</summary>
    internal static class UiTags
    {
        /// <summary>主なボタン（1 画面に 1 つ）</summary>
        public const string Primary = "primary";

        /// <summary>画面の上の帯（題名とボタン）</summary>
        public const string Header = "header";

        /// <summary>白い面（一覧・表の下地）</summary>
        public const string Surface = "surface";

        /// <summary>誤りの文（赤）</summary>
        public const string Error = "error";

        /// <summary>太字の文字（グループの名前・題名）</summary>
        public const string Bold = "bold";

        /// <summary>いちばん目立たせるボタン（区画パネルの［並べる］。赤・大きな太字）</summary>
        public const string Strong = "strong";

        /// <summary>控えめに目立たせるボタン（大きな画面の［すべて開く］。薄い地に主の色の文字と枠）</summary>
        public const string Soft = "soft";

        /// <summary>消す操作のボタン（赤い文字と薄い赤の枠。押し間違いを防ぐ）</summary>
        public const string Danger = "danger";
    }

    /// <summary>
    /// 見た目のスタイル（画面イメージの 3 つ：標準オフィス・スチール道具箱・コンパクト卓上メモ）。
    /// 設定画面の「全般」で切り替える。
    /// </summary>
    internal sealed class Theme
    {
        /// <summary>初期のスタイル</summary>
        public const string DefaultId = "office";

        /// <summary>差し色の赤（アイコンの赤と同じ）</summary>
        public static readonly Color AccentRed = ColorTranslator.FromHtml("#D32F2F");

        /// <summary>開いている物の下地（薄い赤）</summary>
        public static readonly Color AccentRedBack = ColorTranslator.FromHtml("#FFEBEE");

        /// <summary>目立たせるボタンにマウスを乗せた時の濃い赤</summary>
        public static readonly Color AccentRedDark = ColorTranslator.FromHtml("#B71C1C");

        /// <summary>目立たせるボタンの文字の大きさ（ふつうの文字に対する倍率）</summary>
        private const float StrongFontRatio = 1.4f;

        /// <summary>誤りの文の色</summary>
        public static readonly Color ErrorText = ColorTranslator.FromHtml("#C62828");

        /// <summary>消す操作のボタンの枠（薄い赤）</summary>
        private static readonly Color DangerBorder = ColorTranslator.FromHtml("#EF9A9A");

        /// <summary>小さな文字（札の場所など）の大きさ（ふつうの文字からポイントで引く）</summary>
        private const float SmallFontDelta = 1f;

        /// <summary>上の帯のボタンにマウスを当てた時に、帯の色へ混ぜる文字の色の割合</summary>
        private const float HeaderHoverMix = 0.15f;

        private const string FallbackFontName = "Meiryo UI";

        private static readonly Theme[] AllThemes =
        {
            new Theme("office", "標準オフィス", 9.75f,
                "#F3F4F6", "#FFFFFF", "#FFFFFF", "#1A1A1A", "#90A4AE", "#B0BEC5", "#FFFFFF", "#ECEFF1", "#E3F2FD",
                "#1A1A1A", "#37474F", "#455A64", "#37474F"),
            new Theme("steel", "スチール道具箱", 9.75f,
                "#E8ECEF", "#FFFFFF", "#455A64", "#FFFFFF", "#455A64", "#78909C", "#F7F9FA", "#E1E7EB", "#CFD8DC",
                "#111619", "#263238", "#D32F2F", "#B71C1C"),
            new Theme("memo", "コンパクト卓上メモ", 9f,
                "#F7F7F7", "#FFFFFF", "#ECEFF1", "#1A1A1A", "#78909C", "#90A4AE", "#FFFFFF", "#E0E0E0", "#FFF3E0",
                "#141414", "#333333", "#37474F", "#263238")
        };

        private Font _font;
        private Font _boldFont;
        private Font _strongFont;
        private Font _smallFont;

        private Theme(string id, string name, float fontSize,
            string windowBack, string surface, string header, string headerText, string border, string controlBorder,
            string controlBack, string controlHover, string rowSelected, string text, string subText,
            string primaryBack, string primaryHover)
        {
            Id = id;
            Name = name;
            FontSize = fontSize;
            WindowBack = ColorTranslator.FromHtml(windowBack);
            Surface = ColorTranslator.FromHtml(surface);
            Header = ColorTranslator.FromHtml(header);
            HeaderText = ColorTranslator.FromHtml(headerText);
            Border = ColorTranslator.FromHtml(border);
            ControlBorder = ColorTranslator.FromHtml(controlBorder);
            ControlBack = ColorTranslator.FromHtml(controlBack);
            ControlHover = ColorTranslator.FromHtml(controlHover);
            RowSelected = ColorTranslator.FromHtml(rowSelected);
            Text = ColorTranslator.FromHtml(text);
            SubText = ColorTranslator.FromHtml(subText);
            PrimaryBack = ColorTranslator.FromHtml(primaryBack);
            PrimaryHover = ColorTranslator.FromHtml(primaryHover);
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public float FontSize { get; private set; }
        public Color WindowBack { get; private set; }
        public Color Surface { get; private set; }
        public Color Header { get; private set; }
        public Color HeaderText { get; private set; }
        public Color Border { get; private set; }
        public Color ControlBorder { get; private set; }
        public Color ControlBack { get; private set; }
        public Color ControlHover { get; private set; }
        public Color RowSelected { get; private set; }
        public Color Text { get; private set; }
        public Color SubText { get; private set; }
        public Color PrimaryBack { get; private set; }
        public Color PrimaryHover { get; private set; }

        /// <summary>すべてのスタイル（設定画面の選択肢の並び）</summary>
        public static Theme[] All
        {
            get { return AllThemes; }
        }

        /// <summary>ID でスタイルを探す（無ければ null）</summary>
        public static Theme Find(string id)
        {
            foreach (Theme theme in AllThemes)
            {
                if (theme.Id == id)
                {
                    return theme;
                }
            }
            return null;
        }

        /// <summary>ID でスタイルを返す（無ければ初期のスタイル）</summary>
        public static Theme Get(string id)
        {
            return Find(id) ?? Find(DefaultId);
        }

        /// <summary>このスタイルの文字</summary>
        public Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = CreateFont(FontStyle.Regular);
                }
                return _font;
            }
        }

        /// <summary>このスタイルの太字</summary>
        public Font BoldFont
        {
            get
            {
                if (_boldFont == null)
                {
                    _boldFont = CreateFont(FontStyle.Bold);
                }
                return _boldFont;
            }
        }

        /// <summary>目立たせるボタンの文字（大きな太字）</summary>
        public Font StrongFont
        {
            get
            {
                if (_strongFont == null)
                {
                    _strongFont = new Font(Font.FontFamily, FontSize * StrongFontRatio, FontStyle.Bold);
                }
                return _strongFont;
            }
        }

        /// <summary>このスタイルの小さな文字（札の場所・種類など、補いの文）</summary>
        public Font SmallFont
        {
            get
            {
                if (_smallFont == null)
                {
                    _smallFont = new Font(Font.FontFamily, FontSize - SmallFontDelta, FontStyle.Regular);
                }
                return _smallFont;
            }
        }

        /// <summary>設定画面の選択肢に出す名前</summary>
        public override string ToString()
        {
            return Lang.T(Name);
        }

        /// <summary>
        /// 言語を切り替えた時に、覚えた文字を捨てる（次に使う時に、その言語の書体で作り直す）。
        /// 捨てた文字は、作り直す前の画面がまだ使っていることがあるため Dispose しない（数個だけで、常駐の間に増え続けない）。
        /// </summary>
        public static void ResetFonts()
        {
            foreach (Theme theme in AllThemes)
            {
                theme._font = null;
                theme._boldFont = null;
                theme._strongFont = null;
                theme._smallFont = null;
            }
        }

        /// <summary>画面と、その中の部品すべてに色と文字を当てる。</summary>
        public void Apply(Control root)
        {
            Form form = root as Form;
            if (form != null)
            {
                form.BackColor = WindowBack;
                form.ForeColor = Text;
                form.Font = Font;
            }
            ApplyChildren(root, false);
            if (form != null)
            {
                // 倍率の違う画面にある時は、文字をその倍率に合わせ直す
                DpiWatcher.ReapplyFonts(form);
            }
        }

        private void ApplyChildren(Control parent, bool inHeader)
        {
            foreach (Control control in parent.Controls)
            {
                bool header = inHeader || Is(control, UiTags.Header);
                ApplyOne(control, header);
                ApplyChildren(control, header);
            }
        }

        private void ApplyOne(Control control, bool inHeader)
        {
            if (Is(control, UiTags.Header))
            {
                control.BackColor = Header;
                control.ForeColor = HeaderText;
                return;
            }

            Button button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                if (Is(button, UiTags.Strong))
                {
                    button.BackColor = AccentRed;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = AccentRedDark;
                    button.FlatAppearance.MouseOverBackColor = AccentRedDark;
                    button.Font = StrongFont;
                }
                else if (Is(button, UiTags.Primary))
                {
                    button.BackColor = PrimaryBack;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.BorderColor = PrimaryBack;
                    button.FlatAppearance.MouseOverBackColor = PrimaryHover;
                    button.Font = BoldFont;
                }
                else if (Is(button, UiTags.Soft))
                {
                    button.BackColor = RowSelected;
                    button.ForeColor = PrimaryBack;
                    button.FlatAppearance.BorderColor = PrimaryBack;
                    button.FlatAppearance.MouseOverBackColor = ControlHover;
                    button.Font = BoldFont;
                }
                else if (Is(button, UiTags.Danger))
                {
                    button.BackColor = ControlBack;
                    button.ForeColor = ErrorText;
                    button.FlatAppearance.BorderColor = DangerBorder;
                    button.FlatAppearance.MouseOverBackColor = AccentRedBack;
                }
                else if (inHeader)
                {
                    // 上の帯のボタン（［設定］）も、押せると分かるよう枠を付ける
                    button.BackColor = Header;
                    button.ForeColor = HeaderText;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = ControlBorder;
                    // マウスを当てた色は帯の色に文字の色を少し混ぜる（暗い帯のスタイルでも、白い文字が読める）
                    button.FlatAppearance.MouseOverBackColor = Blend(Header, HeaderText, HeaderHoverMix);
                }
                else
                {
                    button.BackColor = ControlBack;
                    button.ForeColor = Text;
                    button.FlatAppearance.BorderColor = ControlBorder;
                    button.FlatAppearance.MouseOverBackColor = ControlHover;
                }
                return;
            }

            if (control is TextBox || control is ListBox || control is ListView || control is ComboBox || control is TreeView)
            {
                control.BackColor = ControlBack;
                control.ForeColor = Text;
                return;
            }

            TabPage page = control as TabPage;
            if (page != null)
            {
                page.UseVisualStyleBackColor = false;
                page.BackColor = Surface;
                page.ForeColor = Text;
                return;
            }

            if (Is(control, UiTags.Surface))
            {
                control.BackColor = Surface;
                control.ForeColor = Text;
                return;
            }

            if (control is Label || control is CheckBox || control is RadioButton)
            {
                control.ForeColor = Is(control, UiTags.Error) ? ErrorText : (inHeader ? HeaderText : Text);
                if (Is(control, UiTags.Bold))
                {
                    control.Font = BoldFont;
                }
            }
        }

        /// <summary>2 つの色を混ぜる（amount は 0＝from のまま、1＝to）。</summary>
        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        private static bool Is(Control control, string tag)
        {
            return (control.Tag as string) == tag;
        }

        private Font CreateFont(FontStyle style)
        {
            // 書体は言語ごと（日本語＝Yu Gothic UI、英語＝Segoe UI、中国語＝Microsoft YaHei UI）。無ければ Meiryo UI
            string name = Lang.FontName;
            Font font = new Font(name, FontSize, style);
            if (font.Name != name)
            {
                font.Dispose();
                font = new Font(FallbackFontName, FontSize, style);
            }
            return font;
        }
    }
}
