using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AIUsageOverlay
{
    /// <summary>
    /// claude.ai へのログイン用ウィンドウ。
    /// ClaudeApiClient と同じユーザーデータフォルダを共有するため、
    /// ここでログインした認証情報はバックグラウンドの WebView2 にも引き継がれる。
    /// ログイン完了後にウィンドウを閉じると、次回の FetchUsageAsync() が自動で成功する。
    ///
    /// 黒画面対策:
    ///   AllowsTransparency=true の背景 WebView2 と Environment を共有すると GPU
    ///   レンダリングが失敗し黒画面になることがある。
    ///   NavigationCompleted で失敗を検知し、エラーバナーとシステムブラウザへの
    ///   フォールバックを提供する。
    /// </summary>
    public partial class LoginWindow : Window
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>ログイン対象 URL</summary>
        private const string LoginUrl = "https://claude.ai/";

        /// <summary>
        /// WebView2 描画確認のポーリング間隔（ミリ秒）。
        /// NavigationCompleted 後にページが白紙・黒画面でないかを確認するために使用する。
        /// </summary>
        private const int RenderCheckDelayMs = 3000;

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
        /// 初期化または描画に失敗した場合はエラーバナーを表示してフォールバックを提供する。
        /// </summary>
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 共有 Environment で初期化することで Cookie が同期される
                await LoginWebView.EnsureCoreWebView2Async(_env);

                // ステータスバーを非表示にする（ブラウザらしさを抑える）
                LoginWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // ナビゲーション完了イベントをフックして失敗を検知する
                LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                // claude.ai のトップページを開く（ログイン後に設定ページへ遷移する）
                LoginWebView.CoreWebView2.Navigate(LoginUrl);

                // 描画遅延チェック: NavigationCompleted から一定時間後に
                // 実際にコンテンツが描画されているか JS で確認する
                await Task.Delay(RenderCheckDelayMs);
                await CheckRenderAsync();
            }
            catch (Exception ex)
            {
                // WebView2 初期化自体が失敗した場合（ランタイム未インストール等）
                ShowErrorBanner($"WebView2 の初期化に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ナビゲーション完了イベントハンドラ。
        /// HTTP エラー（4xx/5xx）やネットワークエラー時にエラーバナーを表示する。
        /// </summary>
        private void OnNavigationCompleted(
            object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                // ナビゲーション失敗（ネットワークエラー・DNS解決失敗等）
                ShowErrorBanner($"ページの読み込みに失敗しました（エラーコード: {e.WebErrorStatus}）");
            }
        }

        /// <summary>
        /// WebView2 描画状態を JavaScript で確認する。
        /// body が空（黒画面・白画面）と判断できる場合はエラーバナーを表示する。
        /// </summary>
        private async Task CheckRenderAsync()
        {
            try
            {
                if (LoginWebView.CoreWebView2 == null) return;

                // document.body の有無を確認する（null = 描画されていない可能性が高い）
                var result = await LoginWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.body ? document.body.innerHTML.length.toString() : '0'");

                // JS が "0" または null を返した場合はコンテンツが描画されていない
                var lengthStr = result?.Trim('"') ?? "0";
                if (int.TryParse(lengthStr, out var bodyLength) && bodyLength < 100)
                {
                    ShowErrorBanner("ブラウザの描画に失敗した可能性があります（黒画面）。");
                }
            }
            catch
            {
                // 描画チェック自体のエラーは無視する（WebView2 が正常な場合もある）
            }
        }

        /// <summary>
        /// エラーバナーを表示する。
        /// UI スレッドから安全に呼び出せるよう Dispatcher.Invoke を使用する。
        /// </summary>
        /// <param name="message">ユーザーに表示するエラーメッセージ</param>
        private void ShowErrorBanner(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ErrorMessageText.Text = message;
                ErrorBanner.Visibility = Visibility.Visible;
                // エラーバナー行を Auto 高さに変更して表示する
                ErrorRow.Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Auto);
            });
        }

        /// <summary>
        /// 「ブラウザで claude.ai を開く」ボタンのクリックハンドラ。
        /// WebView2 が描画できない場合のフォールバックとして、
        /// システムの既定ブラウザで claude.ai を開く。
        /// </summary>
        private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // システムの既定ブラウザで claude.ai を開く
                Process.Start(new ProcessStartInfo
                {
                    FileName        = LoginUrl,
                    UseShellExecute = true   // OS のデフォルトブラウザに委譲する
                });
            }
            catch (Exception ex)
            {
                ErrorMessageText.Text = $"ブラウザを開けませんでした: {ex.Message}";
            }
        }
    }
}
