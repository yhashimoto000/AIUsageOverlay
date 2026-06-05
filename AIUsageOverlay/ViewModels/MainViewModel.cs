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

        /// <summary>定期更新用タイマー</summary>
        private readonly DispatcherTimer _refreshTimer;

        // Claude バッキングフィールド
        private double _sessionPercent;
        private string _sessionPercentText = "0%";
        private string _sessionRemainingText = "--";
        private double _weeklyPercent;
        private string _weeklyPercentText = "0%";
        private string _weeklyRemainingText = "--";
        private string _statusText = "取得中...";

        // GitHub Copilot バッキングフィールド
        private Visibility _gitHubSectionVisibility       = Visibility.Collapsed;

        private Visibility _gitHubOrgBarVisibility        = Visibility.Collapsed;
        private Visibility _gitHubIndividualDotVisibility = Visibility.Visible;
        private double     _gitHubSeatsPercent;
        private string     _gitHubSeatsPercentText = "0%";
        private string     _gitHubSeatsText        = "";
        private string     _gitHubStatusText       = "--";
        private string     _gitHubUserText         = "";

        // Codex バッキングフィールド
        private Visibility _codexSectionVisibility = Visibility.Collapsed;
        private Visibility _codexBarVisibility     = Visibility.Collapsed;
        private Visibility _codexDotVisibility     = Visibility.Visible;
        private double     _codexUsagePercent;
        private string     _codexUsagePercentText  = "0%";
        private string     _codexDetailText        = "";
        private string     _codexStatusText        = "--";
        private string     _codexSubText           = "";

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（Claude）
        // ────────────────────────────────────────────────────────────────

        /// <summary>セッション使用率（0.0 ～ 100.0）</summary>
        public double SessionPercent
        {
            get => _sessionPercent;
            set { _sessionPercent = value; OnPropertyChanged(); }
        }

        /// <summary>セッション使用率テキスト（例: "75%"）</summary>
        public string SessionPercentText
        {
            get => _sessionPercentText;
            set { _sessionPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>セッション残り時間テキスト（例: "1時間13分"）</summary>
        public string SessionRemainingText
        {
            get => _sessionRemainingText;
            set { _sessionRemainingText = value; OnPropertyChanged(); }
        }

        /// <summary>週間使用率（0.0 ～ 100.0）</summary>
        public double WeeklyPercent
        {
            get => _weeklyPercent;
            set { _weeklyPercent = value; OnPropertyChanged(); }
        }

        /// <summary>週間使用率テキスト（例: "5%"）</summary>
        public string WeeklyPercentText
        {
            get => _weeklyPercentText;
            set { _weeklyPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>週間リセットまでの残り時間テキスト</summary>
        public string WeeklyRemainingText
        {
            get => _weeklyRemainingText;
            set { _weeklyRemainingText = value; OnPropertyChanged(); }
        }

        /// <summary>データ取得状態テキスト（例: "API: 14:32"）</summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（GitHub Copilot）
        // ────────────────────────────────────────────────────────────────

        /// <summary>GitHub Copilot セクションの表示/非表示（PAT 未設定時は Collapsed）</summary>
        public Visibility GitHubSectionVisibility
        {
            get => _gitHubSectionVisibility;
            set { _gitHubSectionVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>シート/クレジット使用率バーの表示/非表示</summary>
        public Visibility GitHubOrgBarVisibility
        {
            get => _gitHubOrgBarVisibility;
            set { _gitHubOrgBarVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>個人プラン用ステータスドットの表示/非表示</summary>
        public Visibility GitHubIndividualDotVisibility
        {
            get => _gitHubIndividualDotVisibility;
            set { _gitHubIndividualDotVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>使用率（0.0 ～ 100.0）</summary>
        public double GitHubSeatsPercent
        {
            get => _gitHubSeatsPercent;
            set { _gitHubSeatsPercent = value; OnPropertyChanged(); }
        }

        /// <summary>使用率テキスト（例: "1%"）</summary>
        public string GitHubSeatsPercentText
        {
            get => _gitHubSeatsPercentText;
            set { _gitHubSeatsPercentText = value; OnPropertyChanged(); }
        }

        /// <summary>使用量テキスト（例: "18/1500 AI credits"）</summary>
        public string GitHubSeatsText
        {
            get => _gitHubSeatsText;
            set { _gitHubSeatsText = value; OnPropertyChanged(); }
        }

        /// <summary>GitHub ステータステキスト（例: "18/1500"）</summary>
        public string GitHubStatusText
        {
            get => _gitHubStatusText;
            set { _gitHubStatusText = value; OnPropertyChanged(); }
        }

        /// <summary>更新日テキスト（例: "更新まで26日"）</summary>
        public string GitHubUserText
        {
            get => _gitHubUserText;
            set { _gitHubUserText = value; OnPropertyChanged(); }
        }

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（Codex / OpenAI）
        // ────────────────────────────────────────────────────────────────

        /// <summary>Codex セクションの表示/非表示（未設定時は Collapsed）</summary>
        public Visibility CodexSectionVisibility
        {
            get => _codexSectionVisibility;
            set { _codexSectionVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>クレジット使用率バーの表示/非表示</summary>
        public Visibility CodexBarVisibility
        {
            get => _codexBarVisibility;
            set { _codexBarVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>ステータスドットの表示/非表示</summary>
        public Visibility CodexDotVisibility
        {
            get => _codexDotVisibility;
            set { _codexDotVisibility = value; OnPropertyChanged(); }
        }

        /// <summary>クレジット使用率（0.0 ～ 100.0）</summary>
        public double CodexUsagePercent
        {
            get => _codexUsagePercent;
            set { _codexUsagePercent = value; OnPropertyChanged(); }
        }

        /// <summary>クレジット使用率テキスト（例: "52%"）</summary>
        public string CodexUsagePercentText
        {
            get => _codexUsagePercentText;
            set { _codexUsagePercentText = value; OnPropertyChanged(); }
        }

        /// <summary>クレジット詳細テキスト（例: "$5.23 残高"）</summary>
        public string CodexDetailText
        {
            get => _codexDetailText;
            set { _codexDetailText = value; OnPropertyChanged(); }
        }

        /// <summary>ステータステキスト（例: "$5.23" / "エラー"）</summary>
        public string CodexStatusText
        {
            get => _codexStatusText;
            set { _codexStatusText = value; OnPropertyChanged(); }
        }

        /// <summary>サブテキスト（例: "今月 $3.47 使用"）</summary>
        public string CodexSubText
        {
            get => _codexSubText;
            set { _codexSubText = value; OnPropertyChanged(); }
        }

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        public MainViewModel(UsageService usageService)
        {
            _usageService = usageService;

            var settings = _usageService.GetSettings();
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds)
            };
            _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();
            _refreshTimer.Start();

            _ = RefreshUsageAsync();
        }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>全ツールの使用量を非同期で取得・更新する</summary>
        public async Task RefreshUsageAsync()
        {
            StatusText = "取得中...";

            // ── Claude ──
            var (sessionRatio, sessionRemaining, weeklyRatio, weeklyRemaining, isFromApi) =
                await _usageService.UpdateAndGetUsageAsync();

            SessionPercent      = sessionRatio * 100.0;
            SessionPercentText  = $"{(int)(sessionRatio * 100)}%";
            SessionRemainingText = FormatMinutes(sessionRemaining);
            WeeklyPercent       = weeklyRatio * 100.0;
            WeeklyPercentText   = $"{(int)(weeklyRatio * 100)}%";
            WeeklyRemainingText = FormatMinutes(weeklyRemaining);

            if (isFromApi)
                Statu