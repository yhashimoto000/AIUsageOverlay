using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClaudeUsageOverlay.Services;

namespace ClaudeUsageOverlay.ViewModels
{
    /// <summary>
    /// MainWindow のデータバインディングを担当する ViewModel クラス。
    /// INotifyPropertyChanged を実装し、プロパティ変更時に UI を自動更新する。
    /// DispatcherTimer によって定期的に使用量データを取得・反映する。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>使用量データの取得・保存を担当するサービス</summary>
        private readonly UsageService _usageService;

        /// <summary>定期更新用タイマー（UI スレッドで動作する DispatcherTimer を使用）</summary>
        private readonly DispatcherTimer _refreshTimer;

        // バッキングフィールド（各バインディングプロパティの実値を保持）
        private double _sessionPercent;
        private string _sessionPercentText = "0%";
        private string _sessionRemainingText = "--";
        private double _weeklyPercent;
        private string _weeklyPercentText = "0%";
        private string _weeklyRemainingText = "--";
        private string _statusText = "取得中...";

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// セッション使用率（0.0 ～ 100.0）。
        /// ProgressBar の Value（Maximum=100）にバインドする。
        /// </summary>
        public double SessionPercent
        {
            get => _sessionPercent;
            set { _sessionPercent = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// セッション使用率のテキスト表示（例: "75%"）。
        /// TextBlock の Text にバインドする。
        /// </summary>
        public string SessionPercentText
        {
            get => _sessionPercentText;
            set { _sessionPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// セッション残り時間のテキスト表示（例: "1時間13分"）。
        /// TextBlock の Text にバインドする。
        /// </summary>
        public string SessionRemainingText
        {
            get => _sessionRemainingText;
            set { _sessionRemainingText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 週間使用率（0.0 ～ 100.0）。
        /// ProgressBar の Value（Maximum=100）にバインドする（将来拡張用）。
        /// </summary>
        public double WeeklyPercent
        {
            get => _weeklyPercent;
            set { _weeklyPercent = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 週間使用率のテキスト表示（例: "5%"）。
        /// TextBlock の Text にバインドする。
        /// </summary>
        public string WeeklyPercentText
        {
            get => _weeklyPercentText;
            set { _weeklyPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 週間リセットまでの残り時間テキスト（例: "6日21時間"）。
        /// TextBlock の Text にバインドする。
        /// </summary>
        public string WeeklyRemainingText
        {
            get => _weeklyRemainingText;
            set { _weeklyRemainingText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// データ取得状態を示すテキスト。
        /// 例: "更新: 14:32"（成功）/ "接続エラー" / "取得中..."
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// MainViewModel を初期化する。
        /// UsageService から設定を読み取り、定期更新タイマーを起動する。
        /// </summary>
        /// <param name="usageService">使用量サービスのインスタンス</param>
        public MainViewModel(UsageService usageService)
        {
            _usageService = usageService;

            // 設定値に基づいた間隔で更新タイマーを設定する
            var settings = _usageService.GetSettings();
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds)
            };
            // 非同期で使用量を更新する（async void はイベントハンドラでは許容される）
            _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();
            _refreshTimer.Start();

            // 起動時に即時データ取得する（非同期で初回フェッチ）
            _ = RefreshUsageAsync();
        }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 使用量データを非同期で取得し、バインディングプロパティを更新する。
        /// Cookie が設定されている場合は claude.ai から実データを、
        /// 未設定の場合はローカル時間計測データを取得する。
        /// タイマーのティック・設定変更後・セッションリセット後に呼び出される。
        /// </summary>
        public async Task RefreshUsageAsync()
        {
            StatusText = "取得中...";

            var (sessionRatio, sessionRemaining, weeklyRatio, weeklyRemaining, isFromApi) =
                await _usageService.UpdateAndGetUsageAsync();

            // セッション表示を更新する
            SessionPercent = sessionRatio * 100.0;
            SessionPercentText = $"{(int)(sessionRatio * 100)}%";
            SessionRemainingText = FormatMinutes(sessionRemaining);

            // 週間表示を更新する
            WeeklyPercent = weeklyRatio * 100.0;
            WeeklyPercentText = $"{(int)(weeklyRatio * 100)}%";
            WeeklyRemainingText = FormatMinutes(weeklyRemaining);

            // ステータステキストを更新する
            // - API 成功 → "API: HH:mm"
            // - API 設定済みだが失敗 → "接続エラー"
            // - API 未設定（ローカル計測）→ "更新: HH:mm"
            if (isFromApi)
                StatusText = $"API: {DateTime.Now:HH:mm}";
            else
            {
                var apiError = _usageService.GetLastApiError();
                StatusText = apiError != null ? $"エラー: {apiError}" : "接続エラー";
            }
        }

        /// <summary>
        /// 同期版リフレッシュ（後方互換・フォールバック用）。
        /// ローカル時間計測データのみを取得する。
        /// </summary>
        public void RefreshUsage()
        {
            _ = RefreshUsageAsync();
        }

        /// <summary>
        /// 設定変更後にタイマーの更新間隔を再設定する。
        /// SettingsWindow で設定を保存した後に MainWindow から呼び出す。
        /// </summary>
        public void UpdateRefreshInterval()
        {
            var settings = _usageService.GetSettings();
            _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        }

        /// <summary>
        /// セッションをリセットして表示を即時更新する。
        /// 右クリックメニューの「セッションリセット」から呼び出される。
        /// </summary>
        public void ResetSession()
        {
            _usageService.ResetSession();
            RefreshUsage();
        }

        /// <summary>
        /// タイマーを停止してリソースを解放する（ウィンドウクローズ時に呼ぶ）。
        /// </summary>
        public void Dispose()
        {
            _refreshTimer.Stop();
        }

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパー
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 分数を「X日Y時間Z分」形式の日本語テキストに変換する。
        /// 最上位の単位が 0 の場合はその桁を省略する（例: "13分"、"1時間13分"、"6日21時間"）。
        /// </summary>
        /// <param name="totalMinutes">変換する合計分数</param>
        /// <returns>フォーマットされた残り時間テキスト</returns>
        private static string FormatMinutes(int totalMinutes)
        {
            if (totalMinutes <= 0)
                return "0分";

            var ts = TimeSpan.FromMinutes(totalMinutes);
            int days = (int)ts.TotalDays;
            int hours = ts.Hours;
            int minutes = ts.Minutes;

            if (days > 0 && hours > 0)
                return $"{days}日{hours}時間";
            else if (days > 0)
                return $"{days}日";
            else if (hours > 0 && minutes > 0)
                return $"{hours}時間{minutes}分";
            else if (hours > 0)
                return $"{hours}時間";
            else
                return $"{minutes}分";
        }

        // ────────────────────────────────────────────────────────────────
        // INotifyPropertyChanged
        // ────────────────────────────────────────────────────────────────

        /// <summary>プロパティ変更時に UI へ通知するイベント</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// プロパティ変更を通知する。
        /// CallerMemberName 属性によりプロパティ名を自動取得する。
        /// </summary>
        /// <param name="propertyName">変更されたプロパティ名（自動取得）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
