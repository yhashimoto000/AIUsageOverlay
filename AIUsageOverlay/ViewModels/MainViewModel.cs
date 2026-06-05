using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIUsageOverlay.Services;

namespace AIUsageOverlay.ViewModels
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

        // GitHub Copilot バッキングフィールド
        private Visibility _gitHubSectionVisibility = Visibility.Collapsed;
        private Visibility _gitHubOrgBarVisibility  = Visibility.Collapsed;
        private Visibility _gitHubIndividualDotVisibility = Visibility.Visible;
        private double     _gitHubSeatsPercent;
        private string     _gitHubSeatsPercentText = "0%";
        private string     _gitHubSeatsText        = "";
        private string     _gitHubStatusText       = "--";
        private string     _gitHubUserText         = "";

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

        // ────────────────────────────────────────────────────────────────
        // GitHub Copilot バインディングプロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GitHub Copilot セクションの表示/非表示。
        /// PAT が設定済みのとき Visible、未設定のとき Collapsed。
        /// </summary>
        public Visibility GitHubSectionVisibility
        {
            get => _gitHubSectionVisibility;
            set { _gitHubSectionVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 組織プランのシート使用率バーの表示/非表示。
        /// 組織データ取得成功時のみ Visible。
        /// </summary>
        public Visibility GitHubOrgBarVisibility
        {
            get => _gitHubOrgBarVisibility;
            set { _gitHubOrgBarVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 個人プラン用ステータスドットの表示/非表示。
        /// 組織データが取得できない（個人プラン）ときのみ Visible。
        /// </summary>
        public Visibility GitHubIndividualDotVisibility
        {
            get => _gitHubIndividualDotVisibility;
            set { _gitHubIndividualDotVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 組織シート使用率（0.0 ～ 100.0）。ProgressBar の Value にバインドする。
        /// </summary>
        public double GitHubSeatsPercent
        {
            get => _gitHubSeatsPercent;
            set { _gitHubSeatsPercent = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 組織シート使用率テキスト（例: "80%"）。
        /// </summary>
        public string GitHubSeatsPercentText
        {
            get => _gitHubSeatsPercentText;
            set { _gitHubSeatsPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 組織シート数テキスト（例: "8/10 シート"）。
        /// 個人プランの場合は空文字。
        /// </summary>
        public string GitHubSeatsText
        {
            get => _gitHubSeatsText;
            set { _gitHubSeatsText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// GitHub Copilot ステータステキスト（例: "Connected" / "エラー: PAT認証エラー"）。
        /// </summary>
        public string GitHubStatusText
        {
            get => _gitHubStatusText;
            set { _gitHubStatusText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// GitHub ログインユーザー名（例: "@yhashimoto"）。
        /// 未接続時は空文字。
        /// </summary>
        public string GitHubUserText
        {
            get => _gitHubUserText;
            set { _gitHubUserText = value; OnPropertyChanged(); }
        }

        // ────────────────────────────────────────────────────────────────

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

            // ── Claude 使用量を更新 ──
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
            if (isFromApi)
                StatusText = $"API: {DateTime.Now:HH:mm}";
            else
           