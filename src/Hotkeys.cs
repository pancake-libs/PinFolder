using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// キーの文字（例：Ctrl+Alt+1）と、Windows に登録する値の変換。
    /// </summary>
    internal static class HotkeyText
    {
        private const string Ctrl = "Ctrl";
        private const string Alt = "Alt";
        private const string Shift = "Shift";
        private const string Win = "Win";
        private const string NumPrefix = "Num";

        /// <summary>押したキーを文字にする（Ctrl か Alt を含まない時は空。単独のキーは登録させない）。</summary>
        public static string Format(Keys modifiers, Keys key)
        {
            bool ctrl = (modifiers & Keys.Control) == Keys.Control;
            bool alt = (modifiers & Keys.Alt) == Keys.Alt;
            bool shift = (modifiers & Keys.Shift) == Keys.Shift;
            if (!ctrl && !alt)
            {
                return string.Empty;
            }
            string name = KeyName(key);
            if (name.Length == 0)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder();
            if (ctrl)
            {
                sb.Append(Ctrl).Append('+');
            }
            if (alt)
            {
                sb.Append(Alt).Append('+');
            }
            if (shift)
            {
                sb.Append(Shift).Append('+');
            }
            sb.Append(name);
            return sb.ToString();
        }

        /// <summary>文字を、Windows に登録する修飾キーとキーの値にする。</summary>
        /// <returns>true＝読めた</returns>
        public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (i < parts.Length - 1)
                {
                    if (Same(part, Ctrl))
                    {
                        modifiers |= NativeMethods.MOD_CONTROL;
                    }
                    else if (Same(part, Alt))
                    {
                        modifiers |= NativeMethods.MOD_ALT;
                    }
                    else if (Same(part, Shift))
                    {
                        modifiers |= NativeMethods.MOD_SHIFT;
                    }
                    else if (Same(part, Win))
                    {
                        modifiers |= NativeMethods.MOD_WIN;
                    }
                    else
                    {
                        return false;
                    }
                    continue;
                }
                Keys key;
                if (!TryParseKey(part, out key))
                {
                    return false;
                }
                virtualKey = (uint)key;
            }
            return modifiers != 0 && virtualKey != 0;
        }

        /// <summary>比べるために、文字の形をそろえる（読めない時は空）</summary>
        public static string Normalize(string text)
        {
            uint modifiers;
            uint virtualKey;
            if (!TryParse(text, out modifiers, out virtualKey))
            {
                return string.Empty;
            }
            return text.Replace(" ", string.Empty).ToUpperInvariant();
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                return ((int)(key - Keys.D0)).ToString();
            }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return NumPrefix + (int)(key - Keys.NumPad0);
            }
            if (key >= Keys.A && key <= Keys.Z)
            {
                return key.ToString();
            }
            if (key >= Keys.F1 && key <= Keys.F24)
            {
                return key.ToString();
            }
            switch (key)
            {
                case Keys.Space:
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Insert:
                    return key.ToString();
                default:
                    return string.Empty;
            }
        }

        private static bool TryParseKey(string text, out Keys key)
        {
            key = Keys.None;
            if (text.Length == 1 && char.IsDigit(text[0]))
            {
                key = Keys.D0 + (text[0] - '0');
                return true;
            }
            if (text.Length == 1 && char.IsLetter(text[0]))
            {
                key = Keys.A + (char.ToUpperInvariant(text[0]) - 'A');
                return key >= Keys.A && key <= Keys.Z;
            }
            if (text.StartsWith(NumPrefix, StringComparison.OrdinalIgnoreCase) && text.Length == NumPrefix.Length + 1
                && char.IsDigit(text[NumPrefix.Length]))
            {
                key = Keys.NumPad0 + (text[NumPrefix.Length] - '0');
                return true;
            }
            return Enum.TryParse(text, true, out key) && key != Keys.None;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// どの画面からでも効くキー（RegisterHotKey）を受ける、見えない窓。
    /// </summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int FirstId = 0x1000;

        /// <summary>RegisterHotKey の「ほかで登録済み」のエラー番号</summary>
        private const int ErrorHotkeyAlreadyRegistered = 1409;

        private readonly Dictionary<int, Action> _actions = new Dictionary<int, Action>();
        private int _nextId = FirstId;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        /// <summary>キーを登録する。</summary>
        /// <param name="text">キーの文字（例：Ctrl+Alt+1）</param>
        /// <param name="action">押された時の処理</param>
        /// <param name="reason">登録できなかった理由（登録できた時は null）</param>
        /// <returns>true＝登録できた。false＝読めない・ほかのアプリが使っている</returns>
        public bool Register(string text, Action action, out string reason)
        {
            reason = null;
            uint modifiers;
            uint virtualKey;
            if (!HotkeyText.TryParse(text, out modifiers, out virtualKey))
            {
                reason = "キーの書き方を読めない（例：Ctrl+Alt+1）";
                return false;
            }
            int id = _nextId++;
            if (!NativeMethods.RegisterHotKey(Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
            {
                int error = Marshal.GetLastWin32Error();
                reason = "Windows のエラー " + error + (error == ErrorHotkeyAlreadyRegistered
                    ? "（ほかのアプリが同じキーを使っている）"
                    : "（" + new Win32Exception(error).Message + "）");
                return false;
            }
            _actions[id] = action;
            return true;
        }

        /// <summary>登録したキーをすべて外す。</summary>
        public void UnregisterAll()
        {
            foreach (int id in _actions.Keys)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
            _actions.Clear();
            _nextId = FirstId;
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Action action;
                if (_actions.TryGetValue(m.WParam.ToInt32(), out action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("キーの処理", "キー番号 " + m.WParam.ToInt32(), ex, "想定外の誤りです。このログのファイルを添えて開発の担当へ連絡してください");
                    }
                }
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// キーを押すと、その組み合わせが入る欄（Back・Delete で空にする）。
    /// </summary>
    internal sealed class HotkeyBox : TextBox
    {
        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            Cursor = Cursors.Hand;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Tab だけは次の欄へ移るのに使い、それ以外の組み合わせは受け取る
            return (keyData & Keys.KeyCode) != Keys.Tab || (keyData & Keys.Modifiers) != Keys.None;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.Modifiers == Keys.None && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete))
            {
                Text = string.Empty;
                return;
            }
            string text = HotkeyText.Format(e.Modifiers, e.KeyCode);
            if (text.Length > 0)
            {
                Text = text;
            }
        }
    }
}
