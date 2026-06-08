using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;
// WinForms との名前衝突を解消するエイリアス（wpftmp ビルドで両方参照されるため）
using MessageBox = System.Windows.MessageBox;

namespace AIUsageOverlay
{
    /// <summary>
    /// Claude / GitHub / OpenAI 共用のログインウィンドウ。
    /// 呼び出し元から渡された CoreWebView2Environment を共有するため、
    /// ここでログインした Cookie がバックグラウンド WebView2 にも引き継がれる。
    /// serviceName を渡すことで、タイトルバーとガイドメッセージにサービス名を表示する。
    /// </summary>
    public partial class LoginWindow : Window
    {
        private const string DefaultLoginUrl = "https://claude.ai/";
        private const int RenderCheckDelayMs = 3000;

        private readonly CoreWebView2Environment _env;
        private readonly string _loginUrl;

        /// <summary>
        /// ログインウィンドウを初期化する。
        /// </summary>
        /// <param name="env">WebView2 の共有 Environment（Cookie を引き継ぐために共有する）</param>
        /// <param name="loginUrl">最初に開く URL。省略時は claude.ai を開く。</param>
        /// <param name="serviceName">タイトルバー・ガイドメッセージに表示するサービス名（例: "Claude"）</param>
        public LoginWindow(CoreWebView2Environment env,
                           string loginUrl    = DefaultLoginUrl,
                           string serviceName = "Claude")
        {
            InitializeComponent();
            _env      = env;
            _loginUrl = loginUrl;

            // タイトルバーとガイドメッセージをサービス名で上書きして、
            // 利用者がどのサービスにログインしているかを明確にする
            Title = $"{serviceName} ログイン - AI Usage Overlay";
            GuideMessageText.Text =
                $"{serviceName} にログインしてください。" +
                "ログイン完了後、このウィンドウを閉じると自動で使用量が更新されます。";

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoginWebView.EnsureCoreWebView2Async(_env);
                LoginWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Google OAuth は組み込みブラウザ（WebView2）をブロックすることがある。
                // 通常の Chrome と同じ UA を設定することで disallowed_useragent エラーを回避する。
                LoginWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/130.0.0.0 Safari/537.36";

                // ナビゲーション開始時に URL バーを更新する
                LoginWebView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
                LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                LoginWebView.CoreWebView2.Navigate(_loginUrl);

                await Task.Delay(RenderCheckDelayMs);
                await CheckRenderAsync();
            }
            catch (Exception ex)
            {
                ShowErrorBanner($"WebView2 の初期化に失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// ナビゲーション開始時に URL バーを更新し、戻るボタンの有効/無効を切り替える。
        /// </summary>
        private void OnNavigationStarting(
            object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UrlBar.Text     = e.Uri;
                BackButton.IsEnabled = LoginWebView.CoreWebView2?.CanGoBack ?? false;
            });
        }

        /// <summary>
        /// ナビゲーション完了時に URL バーを最終 URL に更新し、エラーを表示する。
        /// </summary>
        private void OnNavigationCompleted(
            object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var url = LoginWebView.CoreWebView2?.Source ?? "";
                UrlBar.Text          = url;
                BackButton.IsEnabled = LoginWebView.CoreWebView2?.CanGoBack ?? false;
            });

            if (!e.IsSuccess)
                ShowErrorBanner($"ページの読み込みに失敗しました（エラーコード: {e.WebErrorStatus}）");
        }

        /// <summary>戻るボタン: ひとつ前のページに戻る。</summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoginWebView.CoreWebView2?.CanGoBack == true)
                LoginWebView.CoreWebView2.GoBack();
        }

        /// <summary>更新ボタン: 現在のページを再読み込みする。</summary>
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            LoginWebView.CoreWebView2?.Reload();
        }

        private async Task CheckRenderAsync()
        {
            try
            {
                if (LoginWebView.CoreWebView2 == null) return;
                var result = await LoginWebView.CoreWebView2.ExecuteScriptAsync(
                    "document.body ? document.body.innerHTML.length.toString() : '0'");
                var lengthStr = result?.Trim('"') ?? "0";
                if (int.TryParse(lengthStr, out var bodyLength) && bodyLength < 100)
                    ShowErrorBanner("ブラウザの描画に失敗した可能性があります（黒画面）。");
            }
            catch { }
        }

        private void ShowErrorBanner(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ErrorMessageText.Text = message;
                ErrorBanner.Visibility = Visibility.Visible;
                ErrorRow.Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Auto);
            });
        }

        private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = _loginUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ブラウザを開けませんでした。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
