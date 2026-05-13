using System.Windows;

namespace ClaudeUsageOverlay
{
    /// <summary>
    /// App のエントリーポイント。
    /// StartupUri="MainWindow.xaml" により MainWindow が自動起動する。
    /// 未処理例外をグローバルにキャッチしてユーザーに通知する。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// アプリケーション起動時に呼び出されるオーバーライドメソッド。
        /// グローバル例外ハンドラを登録する。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // UI スレッドの未処理例外をキャッチする
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    $"予期しないエラーが発生しました:\n{ex.Exception.Message}",
                    "ClaudeUsageOverlay - エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
