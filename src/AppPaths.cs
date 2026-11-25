using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// アプリの名前・置き場所・アイコンなど、全体で使う値。
    /// </summary>
    internal static class AppPaths
    {
        /// <summary>画面・知らせに出すアプリの名前</summary>
        public const string AppName = "PinFolder";

        /// <summary>exe に埋め込んだアイコンのリソース名（build.bat の /resource と同じ）</summary>
        private const string IconResourceName = "PinFolder.ico";

        /// <summary>exe のあるフォルダ（設定ファイルとログはここに置く）</summary>
        public static string ExeDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>アプリの版（AssemblyInfo.cs の AssemblyVersion）</summary>
        public static string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        /// <summary>
        /// 埋め込んだアイコンを、指定の大きさに近い絵で読む。読めない時は Windows の標準のアイコンを返す。
        /// </summary>
        /// <param name="size">ほしい大きさ（通知領域は SystemInformation.SmallIconSize）</param>
        public static Icon LoadIcon(Size size)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
                {
                    if (stream != null)
                    {
                        return new Icon(stream, size);
                    }
                }
                AppLog.Warn("アイコンの読み込み", IconResourceName, "exe にアイコンが入っていない", "build.bat でビルドし直してください（/resource で PinFolder.ico を入れる）");
            }
            catch (Exception ex)
            {
                AppLog.Error("アイコンの読み込み", IconResourceName, ex, "build.bat でビルドし直してください");
            }
            return SystemIcons.Application;
        }

        /// <summary>浮かぶボタンの絵のリソース名（build.bat の /resource と同じ）</summary>
        public const string FloatImageResourceName = "float_magnet.png";

        /// <summary>
        /// exe に埋め込んだ絵を読む。読めない時は null（呼び出し側は文字で代わりに出す）。
        /// </summary>
        /// <param name="resourceName">リソース名</param>
        public static Image LoadImage(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        // ストリームを閉じた後も使えるよう、写しを作る
                        using (Image image = Image.FromStream(stream))
                        {
                            return new Bitmap(image);
                        }
                    }
                }
                AppLog.Warn("絵の読み込み", resourceName, "exe に絵が入っていない", "build.bat でビルドし直してください（/resource で絵を入れる）");
            }
            catch (Exception ex)
            {
                AppLog.Error("絵の読み込み", resourceName, ex, "build.bat でビルドし直してください");
            }
            return null;
        }

        /// <summary>子画面の題名の左に出すアイコン</summary>
        public static Icon WindowIcon()
        {
            return LoadIcon(SystemInformation.SmallIconSize);
        }
    }
}
