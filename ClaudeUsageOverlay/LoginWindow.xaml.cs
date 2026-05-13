using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageOverlay
{
    /// <summary>
    /// claude.ai へのログイン用ウィンドウ。
    /// ClaudeApiClient と同じユーザーデータフォルダを共有するため、
    /// ここでログインした認証情報はバックグラウンドの WebView2 にも引き継がれる。
    /// ログイン完了後にウィンドウを閉じると、次回の FetchUsageAsync() が自動で成功する。
    /// </summary>
    public partial class LoginWindow : Window
    {
        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ClaudeApiClient と共有する CoreWebView2Environment。
        /// 同じユーザーデータフォルダを参照するため Cookie を共有できる。
        /// </summary>
        private readonly CoreWebView2Environment _env;

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// LoginWindow を初期化する。
        /// </summary>
        /// <param name="env">ClaudeApiClient から渡された共有 CoreWebView2Environment</param>
        public LoginWindow(CoreWebView2Environment env)
        {
            InitializeComponent();
            _env = env;

            // ウィンドウ表示後に WebView2 を初期化する
            Loaded += OnLoaded;
        }

        // ────────────────────────────────────────────────────────────────
        // イベントハンドラ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ウィンドウ表示後に WebView2 を初期化して claude.ai を開く。
        /// ClaudeApiClient と同じ CoreWebView2Environment を使うことで Cookie を共有する。
        /// </summary>
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 共有 Environment で初期化することで Cookie が同期される
            await LoginWebView.EnsureCoreWebView2Async(_env);

            // ステータスバーを非表示にする（ブラウザらしさを抑える）
            LoginWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // claude.ai のトップページを開く（ログイン後に設定ページへ遷移する）
            LoginWebView.CoreWebView2.Navigate("https://claude.ai/");
        }
    }
}
