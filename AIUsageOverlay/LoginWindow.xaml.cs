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

        /// <summary>
        /// 初期化・ナビゲーション失敗時の自動リトライ最大回数。
        /// PC起動直後はWebView2ランタイムやネットワークスタックの準備が数秒遅れることがあり、
        /// その間に EnsureCoreWebView2Async / Navigate が一時的に失敗するケースがある。
        /// 1回の失敗で「ログインボタンを押しても直らない」状態にしないために設ける。
        /// </summary>
        private const int MaxAutoRetryCount = 3;

        /// <summary>自動リトライ間の待機時間（ミリ秒）。</summary>
        private const int AutoRetryDelayMs = 2000;

        private readonly CoreWebView2Environment _env;
        private readonly string _loginUrl;

        /// <summary>
        /// 初期化・ナビゲーション失敗時の自動リトライ実行回数。
        /// 成功時、またはユーザーが「更新」ボタンを押したときにリセットされる。
        /// </summary>
        private int _autoRetryCount;

        /// <summary>NavigationStarting/NavigationCompleted の多重登録防止フラグ。</summary>
        private bool _navigationEventsAttached;

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
            await InitializeAndNavigateAsync();
        }

        /// <summary>
        /// WebView2 を初期化し、ログイン URL へ Navigate する。
        ///
        /// PC起動直後はWebView2ランタイムやネットワークスタックの準備が数秒遅れることがあり、
        /// EnsureCoreWebView2Async が一時的に失敗する場合がある。1回の失敗で
        /// 「ログインボタンを押しても直らない」状態に陥らないよう、最大
        /// <see cref="MaxAutoRetryCount"/> 回まで <see cref="AutoRetryDelayMs"/> ミリ秒の
        /// 待機を挟んで自動的に再試行する。
        /// </summary>
        private async Task InitializeAndNavigateAsync()
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

                // ナビゲーション開始時に URL バーを更新する（再試行時に多重登録しない）
                if (!_navigationEventsAttached)
                {
                    LoginWebView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
                    LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    _navigationEventsAttached = true;
                }
                LoginWebView.CoreWebView2.Navigate(_loginUrl);

                await Task.Delay(RenderCheckDelayMs);
                await CheckRenderAsync();

                // 初期化に成功したのでリトライ回数をリセットする
                _autoRetryCount = 0;
            }
            catch (Exception ex)
            {
                if (await TryScheduleAutoRetryAsync($"WebView2 の初期化に失敗しました: {ex.Message}"))
                    await InitializeAndNavigateAsync();
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

            if (e.IsSuccess)
            {
                // 読み込みに成功したのでリトライ回数をリセットする
                _autoRetryCount = 0;
                return;
            }

            // ページ読み込み失敗。PC起動直後はネットワークが安定するまで同様の
            // 失敗が続くことがあるため、即座にエラー表示する前に自動リトライする。
            _ = RetryNavigationAsync(e.WebErrorStatus);
        }

        /// <summary>
        /// ページ読み込み失敗時の自動リトライ。リトライ上限に達した場合のみ
        /// 最終的なエラーバナーを表示する。
        /// </summary>
        private async Task RetryNavigationAsync(CoreWebView2WebErrorStatus errorStatus)
        {
            if (await TryScheduleAutoRetryAsync($"ページの読み込みに失敗しました（エラーコード: {errorStatus}）"))
                LoginWebView.CoreWebView2?.Navigate(_loginUrl);
        }

        /// <summary>
        /// 初期化・ナビゲーション失敗時の共通リトライ判定。
        /// リトライ上限に達していなければ <see cref="AutoRetryDelayMs"/> 待機して true を返す
        /// （呼び出し元が再試行する）。上限に達した場合は最終的なエラーバナーを表示して false を返す。
        /// </summary>
        /// <param name="failureMessage">リトライ上限到達時にエラーバナーへ表示するメッセージ</param>
        /// <returns>呼び出し元がリトライすべき場合 true、これ以上リトライしない場合 false</returns>
        private async Task<bool> TryScheduleAutoRetryAsync(string failureMessage)
        {
            _autoRetryCount++;
            if (_autoRetryCount > MaxAutoRetryCount)
            {
                ShowErrorBanner(
                    $"{failureMessage}\n" +
                    $"（{MaxAutoRetryCount}回再試行しましたが失敗しました。ネットワーク接続を確認して「更新」ボタンを押してください）");
                return false;
            }

            await Task.Delay(AutoRetryDelayMs);
            return true;
        }

        /// <summary>戻るボタン: ひとつ前のページに戻る。</summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoginWebView.CoreWebView2?.CanGoBack == true)
                LoginWebView.CoreWebView2.GoBack();
        }

        /// <summary>
        /// 更新ボタン: 現在のページを再読み込みする。
        /// ユーザーによる明示的な再試行のため、自動リトライ回数をリセットし、
        /// 古いエラーバナーが残っていれば隠してから再読み込みする。
        /// </summary>
        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            _autoRetryCount = 0;
            HideErrorBanner();
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

        /// <summary>
        /// エラーバナーを隠して行の高さを 0 に戻す。手動更新（再試行）の直前に呼び出し、
        /// 前回失敗時の表示を残さないようにする。
        /// </summary>
        private void HideErrorBanner()
        {
            Dispatcher.Invoke(() =>
            {
                ErrorBanner.Visibility = Visibility.Collapsed;
                ErrorRow.Height = new System.Windows.GridLength(0);
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
