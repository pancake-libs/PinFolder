using System;
using System.Threading;
using System.Windows.Forms;

namespace PinFolder
{
    /// <summary>
    /// PinFolder の入口。二重起動を防ぎ、通知領域への常駐を始める。
    /// </summary>
    internal static class Program
    {
        /// <summary>二重起動を防ぐための名前（同じ利用者のセッションの中だけで効く）</summary>
        private const string MutexName = "Local\\PinFolder_SingleInstance";

        /// <summary>想定外の誤りの時に、ログの「次に何をするか」に書く文</summary>
        private const string UnexpectedNext = "想定外の誤りです。このログのファイルを添えて開発の担当へ連絡してください。PinFolder を起動し直すと、多くの場合は続けて使えます";

        /// <summary>画面の大きさの確かめを流す引数（画面は出さず、結果を layout_check.txt に書いて終わる）</summary>
        private const string CheckLayoutArgument = "--check-layout";

        /// <summary>区画のセットを並べて終わる引数（例：PinFolder.exe --arrange "朝の作業"。デスクトップのショートカットから使う）</summary>
        private const string ArrangeArgument = "--arrange";

        /// <summary>区画のセットの窓を閉じて終わる引数（例：PinFolder.exe --close "朝の作業"）</summary>
        private const string CloseArgument = "--close";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == CheckLayoutArgument)
            {
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppLog.Start();
                // 確かめの途中の画面の誤りで、標準の誤りの画面が出て止まらないよう、例外は下の catch で受ける
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                // 終了コード：0＝すべてのボタンに届く、1＝届かない・切れるボタンがある、2＝確かめそのものが失敗した
                int code;
                try
                {
                    code = LayoutCheck.Run();
                }
                catch (Exception ex)
                {
                    AppLog.Error("画面の大きさの確かめ", "PinFolder", ex, UnexpectedNext);
                    code = 2;
                }
                AppLog.End(code == 0);
                return code;
            }

            if (args.Length > 1 && args[0] == ArrangeArgument)
            {
                return ArrangeSet(args[1], false);
            }
            if (args.Length > 1 && args[0] == CloseArgument)
            {
                return ArrangeSet(args[1], true);
            }

            // 「既に起動しています」などを設定の言語で出すため、先に言語だけを読む
            Lang.LoadEarly(ConfigStore.FilePath);
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show(Lang.T("PinFolder は既に起動しています。"), AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                // 画面の倍率が 100% 以外でも、文字と部品がぼやけないようにする
                NativeMethods.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppLog.Start();
                Application.ThreadException += (sender, e) =>
                {
                    AppLog.Error("画面の処理", "PinFolder", e.Exception, UnexpectedNext);
                };
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    AppLog.Error("想定外の終了", "PinFolder", e.ExceptionObject as Exception, UnexpectedNext);
                    AppLog.End(false);
                };

                bool normal = true;
                try
                {
                    Application.Run(new PinFolderContext());
                }
                catch (Exception ex)
                {
                    normal = false;
                    AppLog.Error("常駐", "PinFolder", ex, UnexpectedNext);
                    MessageBox.Show(Lang.F("PinFolder を続けられませんでした。ログ：{0}", AppLog.FilePath), AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    AppLog.End(normal);
                }
                return normal ? 0 : 1;
            }
        }

        /// <summary>
        /// 区画のセットを、マウスのある画面に並べて（close＝true の時は、そのセットの窓を閉じて）終わる（PinFolder が常駐していなくても使える）。
        /// 終了コード：0＝できた、1＝開けなかった物がある、2＝セットが無い・失敗した。
        /// </summary>
        private static int ArrangeSet(string setName, bool close)
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            AppLog.Start();
            int code = 2;
            try
            {
                string warning;
                AppConfig config = ConfigStore.Load(out warning);
                LayoutSet found = null;
                foreach (LayoutSet set in config.LayoutSets)
                {
                    if (set.Name == setName)
                    {
                        found = set;
                    }
                }
                if (found == null)
                {
                    AppLog.Warn("並べる", setName, "その名前の区画のセットが無い", "区画パネルでセットの名前を確かめてください（大文字・小文字・空白も同じにする）");
                }
                else if (close)
                {
                    int closed = 0;
                    foreach (string id in found.Zones)
                    {
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }
                        Job job;
                        Entry entry = Arranger.FindEntry(config, id, out job);
                        if (entry != null && Launcher.Close(job.Name, entry))
                        {
                            closed++;
                        }
                    }
                    AppLog.Info("一式を閉じる", setName, closed + " 件の窓を閉じた");
                    code = 0;
                }
                else
                {
                    ArrangeResult result = Arranger.Arrange(config, found.Shape, found.Zones, ScreenTargets.ForButton(config.FloatX, config.FloatY));
                    code = result.Failed.Count == 0 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("並べる", setName, ex, UnexpectedNext);
            }
            AppLog.End(code != 2);
            return code;
        }
    }
}
