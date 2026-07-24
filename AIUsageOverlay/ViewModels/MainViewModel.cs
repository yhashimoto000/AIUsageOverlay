using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AIUsageOverlay.Services;
// UseWindowsForms 有効化で System.Drawing.Brush が暗黙 using に入るため、
// ペース色に使う WPF のブラシはエイリアスで衝突を回避する（CS0104 予防）。
using MediaBrush = System.Windows.Media.Brush;

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

        /// <summary>閾値超過・リセット・上限到達の通知を担うサービス（F-07）。NotifyIcon は App から注入。</summary>
        private readonly NotificationService _notificationService = new();

        /// <summary>定期更新用タイマー</summary>
        private readonly DispatcherTimer _refreshTimer;

        /// <summary>
        /// 起動直後に Windows のネットワーク初期化や WebView2 プロファイル復元と競合しないよう、
        /// 初回の自動取得だけ短く遅延させる秒数。
        /// </summary>
        private const int InitialRefreshDelaySeconds = 5;

        /// <summary>stale（取得失敗で情報が古い）時のセクション不透明度（F-02。CodexBar と同値の 55%）。</summary>
        private const double StaleOpacity = 0.55;

        /// <summary>
        /// 定期更新・手動更新・設定保存後更新が同時に走らないようにする排他制御。
        /// WebView2 は同一インスタンスで複数 Navigate を重ねると状態が壊れやすいため、
        /// ViewModel 側で更新処理全体を 1 本に制限する。
        /// </summary>
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        /// <summary>
        /// ViewModel 破棄済みフラグ。
        /// 起動直後の遅延初回更新が残っている状態でアプリ終了しても、破棄後の更新処理を走らせない。
        /// </summary>
        private bool _disposed;

        // ── F-10 適応更新間隔 ─────────────────────────────────────────
        /// <summary>最後にユーザー操作があった時刻。放置時間に応じた間隔延長に使う。</summary>
        private DateTime _lastInteractionAt = DateTime.Now;

        // ── F-11 スヌーズ ────────────────────────────────────────────
        /// <summary>この時刻まで自動更新を一時停止する（null=停止なし）。永続化しない。</summary>
        private DateTime? _snoozeUntil;
        private bool _isSnoozing;

        // Claude バッキングフィールド
        private double _sessionPercent;
        private string _sessionPercentText = "0%";
        private string _sessionRemainingText = "--";
        private double _weeklyPercent;
        private string _weeklyPercentText = "0%";
        private string _weeklyRemainingText = "--";
        private string _statusText = "取得中...";

        /// <summary>
        /// 直近取得時のセッション/週間リセット日時（ローカル）。F-04 の絶対時刻表示に使う。
        /// API 経由取得時のみ実値が入り、ローカルフォールバック・未使用時は null。
        /// </summary>
        private DateTime? _sessionResetAt;
        private DateTime? _weeklyResetAt;

        // ── stale（取得失敗で情報が古い）状態と、それに応じたセクション不透明度（F-02）──
        // Claude のみトレイ減光（App.xaml.cs）で bool を参照するため IsClaudeStale として公開する。
        // GitHub / Codex は不透明度（ClaudeSectionOpacity 等）のみで表現するため bool は持たない。
        private bool       _claudeStale;
        private double     _claudeSectionOpacity = 1.0;
        private double     _gitHubSectionOpacity = 1.0;
        private double     _codexSectionOpacity  = 1.0;

        // ── ペース表示（F-06）──────────────────────────────────────
        private string     _sessionPaceText = "";
        private MediaBrush _sessionPaceBrush = PaceGray;
        private Visibility _sessionPaceVisibility = Visibility.Collapsed;
        private string     _codexPaceText = "";
        private MediaBrush _codexPaceBrush = PaceGray;
        private Visibility _codexPaceVisibility = Visibility.Collapsed;

        // ── メタ行・週チップ（デザイン刷新 2b）──────────────────────
        // 縦積みレイアウトでは各サービスの「残り時間＋ペース」を 1 行のメタ行に統合し、
        // 週間使用率は見出し行の小さなチップ（"週 12%"）で示す。
        private string     _claudeMetaPrimary        = "--";  // 例: "残り 2時間13分" / "14:32 リセット"
        private string     _weeklyChipText           = "";    // 例: "週 12%"
        private Visibility  _weeklyChipVisibility     = Visibility.Collapsed;
        private string     _codexMetaPrimary         = "--";  // 例: "残り 1時間47分" / "22:22 リセット"
        private string     _codexWeeklyChipText      = "";    // 例: "週 34%"
        private Visibility  _codexWeeklyChipVisibility = Visibility.Collapsed;
        private string     _copilotMetaText          = "";    // 例: "450/1000 クレジット ・ 更新まで26日"

        /// <summary>
        /// スパークライン再描画トリガー。取得サイクル完了ごとにインクリメントし、
        /// 使用率%が同値でも履歴点の増加を View 側の再描画に反映させる。
        /// </summary>
        private int _sparklineRevision;

        // ペース表示色（凍結済みブラシを共有）
        private static readonly MediaBrush PaceGray   = HexBrush("#888888"); // 順調
        private static readonly MediaBrush PaceOrange = HexBrush("#FF8C00"); // 先行（持つ）
        private static readonly MediaBrush PaceRed    = HexBrush("#F44336"); // 先行（枯渇予測）
        private static readonly MediaBrush PaceGreen  = HexBrush("#4CAF50"); // 余裕

        // GitHub Copilot バッキングフィールド
        private Visibility _gitHubSectionVisibility       = Visibility.Collapsed;

        private Visibility _gitHubOrgBarVisibility        = Visibility.Collapsed;
        private Visibility _gitHubIndividualDotVisibility = Visibility.Visible;
        private double     _gitHubSeatsPercent;
        private string     _gitHubSeatsPercentText = "0%";
        private string     _gitHubSeatsText        = "";
        private string     _gitHubStatusText       = "--";
        private string     _gitHubUserText         = "";

        /// <summary>
        /// 一度でも Copilot 使用量の取得に成功したかを表すフラグ。
        /// 一時的な取得失敗（タイムアウト等）の際に前回の正常表示を維持し、
        /// "エラー" への切り替えによるちらつきを防ぐために使用する。
        /// </summary>
        private bool       _gitHubEverLoaded;

        // Codex バッキングフィールド
        private Visibility _codexSectionVisibility = Visibility.Collapsed;
        private Visibility _codexBarVisibility     = Visibility.Collapsed;
        private Visibility _codexDotVisibility     = Visibility.Visible;
        private double     _codexUsagePercent;
        private string     _codexUsagePercentText  = "0%";
        private string     _codexDetailText        = "";
        private string     _codexStatusText        = "--";
        private string     _codexSubText           = "";

        /// <summary>
        /// 一度でも Codex 使用量の取得に成功したかを表すフラグ。
        /// 起動直後の一時的なネットワーク失敗で前回表示を消さないために使用する。
        /// </summary>
        private bool       _codexEverLoaded;

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（Claude）
        // ────────────────────────────────────────────────────────────────

        /// <summary>セッション使用率（0.0 ～ 100.0）</summary>
        public double SessionPercent
        {
            get => _sessionPercent;
            set => SetProperty(ref _sessionPercent, value);
        }

        /// <summary>セッション使用率テキスト（例: "75%"）</summary>
        public string SessionPercentText
        {
            get => _sessionPercentText;
            set => SetProperty(ref _sessionPercentText, value);
        }

        /// <summary>セッション残り時間テキスト（例: "1時間13分"）</summary>
        public string SessionRemainingText
        {
            get => _sessionRemainingText;
            set => SetProperty(ref _sessionRemainingText, value);
        }

        /// <summary>週間使用率（0.0 ～ 100.0）</summary>
        public double WeeklyPercent
        {
            get => _weeklyPercent;
            set => SetProperty(ref _weeklyPercent, value);
        }

        /// <summary>週間使用率テキスト（例: "5%"）</summary>
        public string WeeklyPercentText
        {
            get => _weeklyPercentText;
            set => SetProperty(ref _weeklyPercentText, value);
        }

        /// <summary>週間リセットまでの残り時間テキスト</summary>
        public string WeeklyRemainingText
        {
            get => _weeklyRemainingText;
            set => SetProperty(ref _weeklyRemainingText, value);
        }

        /// <summary>データ取得状態テキスト（例: "API: 14:32"）</summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ── stale 状態（F-02）──────────────────────────────────────────

        /// <summary>
        /// Claude の取得が失敗（API 傍受不成立でローカル値にフォールバック）しているか。
        /// トレイアイコンの減光判定（App.xaml.cs）が参照するため公開する。
        /// </summary>
        public bool IsClaudeStale
        {
            get => _claudeStale;
            private set => SetProperty(ref _claudeStale, value);
        }

        /// <summary>Claude セクションの不透明度。stale のとき 0.55、通常 1.0。</summary>
        public double ClaudeSectionOpacity
        {
            get => _claudeSectionOpacity;
            private set => SetProperty(ref _claudeSectionOpacity, value);
        }

        /// <summary>GitHub Copilot セクションの不透明度。stale のとき 0.55、通常 1.0。</summary>
        public double GitHubSectionOpacity
        {
            get => _gitHubSectionOpacity;
            private set => SetProperty(ref _gitHubSectionOpacity, value);
        }

        /// <summary>Codex セクションの不透明度。stale のとき 0.55、通常 1.0。</summary>
        public double CodexSectionOpacity
        {
            get => _codexSectionOpacity;
            private set => SetProperty(ref _codexSectionOpacity, value);
        }

        // ── ペース表示（F-06）──────────────────────────────────────

        /// <summary>Claude セッションのペース表示テキスト（例: "ペース: 予定比 +8%"）。</summary>
        public string SessionPaceText
        {
            get => _sessionPaceText;
            private set => SetProperty(ref _sessionPaceText, value);
        }

        /// <summary>Claude セッションのペース表示色。</summary>
        public MediaBrush SessionPaceBrush
        {
            get => _sessionPaceBrush;
            private set => SetProperty(ref _sessionPaceBrush, value);
        }

        /// <summary>Claude セッションのペース行の表示/非表示（計算不能・ゲート未達・OFF・stale で Collapsed）。</summary>
        public Visibility SessionPaceVisibility
        {
            get => _sessionPaceVisibility;
            private set => SetProperty(ref _sessionPaceVisibility, value);
        }

        /// <summary>Codex のペース表示テキスト（5時間枠優先、OnTrack のときのみ週間枠）。</summary>
        public string CodexPaceText
        {
            get => _codexPaceText;
            private set => SetProperty(ref _codexPaceText, value);
        }

        /// <summary>Codex のペース表示色。</summary>
        public MediaBrush CodexPaceBrush
        {
            get => _codexPaceBrush;
            private set => SetProperty(ref _codexPaceBrush, value);
        }

        /// <summary>Codex のペース行の表示/非表示。</summary>
        public Visibility CodexPaceVisibility
        {
            get => _codexPaceVisibility;
            private set => SetProperty(ref _codexPaceVisibility, value);
        }

        // ── メタ行・週チップ（デザイン刷新 2b）──────────────────────

        /// <summary>Claude メタ行の主テキスト（残り時間 or 絶対リセット時刻）。ペースは別 Run で連結する。</summary>
        public string ClaudeMetaPrimary
        {
            get => _claudeMetaPrimary;
            private set => SetProperty(ref _claudeMetaPrimary, value);
        }

        /// <summary>Claude 見出し行の週間チップ文字（例: "週 12%"）。</summary>
        public string WeeklyChipText
        {
            get => _weeklyChipText;
            private set => SetProperty(ref _weeklyChipText, value);
        }

        /// <summary>Claude 週間チップの表示可否（週間データがあるときのみ表示）。</summary>
        public Visibility WeeklyChipVisibility
        {
            get => _weeklyChipVisibility;
            private set => SetProperty(ref _weeklyChipVisibility, value);
        }

        /// <summary>Codex メタ行の主テキスト（5時間枠の残り時間 or 絶対リセット時刻）。</summary>
        public string CodexMetaPrimary
        {
            get => _codexMetaPrimary;
            private set => SetProperty(ref _codexMetaPrimary, value);
        }

        /// <summary>Codex 見出し行の週間チップ文字（例: "週 34%"）。</summary>
        public string CodexWeeklyChipText
        {
            get => _codexWeeklyChipText;
            private set => SetProperty(ref _codexWeeklyChipText, value);
        }

        /// <summary>Codex 週間チップの表示可否（週間データがあるときのみ表示）。</summary>
        public Visibility CodexWeeklyChipVisibility
        {
            get => _codexWeeklyChipVisibility;
            private set => SetProperty(ref _codexWeeklyChipVisibility, value);
        }

        /// <summary>Copilot メタ行のテキスト（クレジット + 更新情報。ペースは持たない）。</summary>
        public string CopilotMetaText
        {
            get => _copilotMetaText;
            private set => SetProperty(ref _copilotMetaText, value);
        }

        /// <summary>
        /// スパークライン再描画トリガー。取得サイクル完了ごとに変化し、View がこれを購読して
        /// 履歴（UsageService.GetHistory）から Polyline を引き直す。
        /// </summary>
        public int SparklineRevision
        {
            get => _sparklineRevision;
            private set => SetProperty(ref _sparklineRevision, value);
        }

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（GitHub Copilot）
        // ────────────────────────────────────────────────────────────────

        /// <summary>GitHub Copilot セクションの表示/非表示（PAT 未設定時は Collapsed）</summary>
        public Visibility GitHubSectionVisibility
        {
            get => _gitHubSectionVisibility;
            set => SetProperty(ref _gitHubSectionVisibility, value);
        }

        /// <summary>シート/クレジット使用率バーの表示/非表示</summary>
        public Visibility GitHubOrgBarVisibility
        {
            get => _gitHubOrgBarVisibility;
            set => SetProperty(ref _gitHubOrgBarVisibility, value);
        }

        /// <summary>個人プラン用ステータスドットの表示/非表示</summary>
        public Visibility GitHubIndividualDotVisibility
        {
            get => _gitHubIndividualDotVisibility;
            set => SetProperty(ref _gitHubIndividualDotVisibility, value);
        }

        /// <summary>使用率（0.0 ～ 100.0）</summary>
        public double GitHubSeatsPercent
        {
            get => _gitHubSeatsPercent;
            set => SetProperty(ref _gitHubSeatsPercent, value);
        }

        /// <summary>使用率テキスト（例: "1%"）</summary>
        public string GitHubSeatsPercentText
        {
            get => _gitHubSeatsPercentText;
            set => SetProperty(ref _gitHubSeatsPercentText, value);
        }

        /// <summary>使用量テキスト（例: "18/1500 AI credits"）</summary>
        public string GitHubSeatsText
        {
            get => _gitHubSeatsText;
            set => SetProperty(ref _gitHubSeatsText, value);
        }

        /// <summary>GitHub ステータステキスト（例: "18/1500"）</summary>
        public string GitHubStatusText
        {
            get => _gitHubStatusText;
            set => SetProperty(ref _gitHubStatusText, value);
        }

        /// <summary>更新日テキスト（例: "更新まで26日"）</summary>
        public string GitHubUserText
        {
            get => _gitHubUserText;
            set => SetProperty(ref _gitHubUserText, value);
        }

        // ────────────────────────────────────────────────────────────────
        // バインディングプロパティ（Codex / OpenAI）
        // ────────────────────────────────────────────────────────────────

        /// <summary>Codex セクションの表示/非表示（未設定時は Collapsed）</summary>
        public Visibility CodexSectionVisibility
        {
            get => _codexSectionVisibility;
            set => SetProperty(ref _codexSectionVisibility, value);
        }

        /// <summary>週間制限使用率バーの表示/非表示（F-13。旧5時間バーを週間バーへ転用）</summary>
        public Visibility CodexBarVisibility
        {
            get => _codexBarVisibility;
            set => SetProperty(ref _codexBarVisibility, value);
        }

        /// <summary>ステータスドットの表示/非表示</summary>
        public Visibility CodexDotVisibility
        {
            get => _codexDotVisibility;
            set => SetProperty(ref _codexDotVisibility, value);
        }

        /// <summary>Codex 週間制限使用率（0.0 ～ 100.0）（F-13）</summary>
        public double CodexUsagePercent
        {
            get => _codexUsagePercent;
            set => SetProperty(ref _codexUsagePercent, value);
        }

        /// <summary>Codex 週間制限使用率テキスト（例: "52%"）（F-13）</summary>
        public string CodexUsagePercentText
        {
            get => _codexUsagePercentText;
            set => SetProperty(ref _codexUsagePercentText, value);
        }

        /// <summary>Codex 週間制限の残り時間/リセット時刻テキスト（例: "5日9時間"）（F-13）</summary>
        public string CodexDetailText
        {
            get => _codexDetailText;
            set => SetProperty(ref _codexDetailText, value);
        }

        /// <summary>Codex 週間制限使用率テキスト（例: "18%" / "エラー"）</summary>
        public string CodexStatusText
        {
            get => _codexStatusText;
            set => SetProperty(ref _codexStatusText, value);
        }

        /// <summary>Codex 週間制限の残り時間テキスト（例: "5日9時間"）</summary>
        public string CodexSubText
        {
            get => _codexSubText;
            set => SetProperty(ref _codexSubText, value);
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
            // F-10: タイマー Tick の先頭で適応間隔を再計算してから取得する
            _refreshTimer.Tick += async (_, _) =>
            {
                UpdateAdaptiveInterval();
                await RefreshUsageAsync();
            };
            _refreshTimer.Start();

            _ = RefreshUsageAfterStartupDelayAsync();
        }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>全ツールの使用量を非同期で取得・更新する</summary>
        public async Task RefreshUsageAsync()
        {
            if (_disposed)
                return;

            if (!await _refreshGate.WaitAsync(0))
                return;

            try
            {
                if (_disposed)
                    return;

                // F-11: スヌーズ中は取得をスキップ（手動更新は ClearSnooze 後に呼ばれるため実行される）
                if (_snoozeUntil.HasValue && _snoozeUntil.Value > DateTime.Now)
                {
                    StatusText = $"一時停止中（〜{_snoozeUntil.Value:HH:mm}）";
                    return;
                }

                StatusText = "取得中...";

                // ── Claude ──
                var (sessionRatio, sessionRemaining, weeklyRatio, weeklyRemaining, isFromApi,
                     sessionResetAt, weeklyResetAt) =
                    await _usageService.UpdateAndGetUsageAsync();

                // F-04: リセット日時を保持し、表示形式（相対/絶対）に応じて残り時間テキストを組み立てる
                _sessionResetAt = sessionResetAt;
                _weeklyResetAt  = weeklyResetAt;
                var mode = _usageService.GetSettings().ResetDisplayMode;

                SessionPercent      = sessionRatio * 100.0;
                SessionPercentText  = $"{(int)(sessionRatio * 100)}%";
                SessionRemainingText = BuildResetText(mode, sessionRemaining, sessionResetAt);
                WeeklyPercent       = weeklyRatio * 100.0;
                WeeklyPercentText   = $"{(int)(weeklyRatio * 100)}%";
                WeeklyRemainingText = BuildResetText(mode, weeklyRemaining, weeklyResetAt);

                // デザイン刷新: メタ行の主テキスト（残り時間/絶対時刻）と週間チップを組み立てる
                ClaudeMetaPrimary    = BuildMetaPrimary(mode, SessionRemainingText);
                WeeklyChipText       = $"週 {(int)(weeklyRatio * 100)}%";
                WeeklyChipVisibility = Visibility.Visible;

                // F-02: API 取得成功なら通常表示、失敗（ローカルフォールバック）なら stale として減光する
                IsClaudeStale        = !isFromApi;
                ClaudeSectionOpacity = isFromApi ? 1.0 : StaleOpacity;

                // デザイン刷新: スパークライン用に使用率%を自己記録（実測＝API 取得成功時のみ）
                if (isFromApi)
                    _usageService.RecordHistory(UsageHistoryService.SeriesClaude,
                        sessionRatio * 100.0, weeklyRatio * 100.0);

                // ── F-06: ペース計算・表示（PaceEnabled かつ API 取得成功時のみ。stale では誤解を避け非表示）──
                if (_usageService.GetSettings().PaceEnabled && isFromApi)
                {
                    // Claude セッション（5時間=300分固定）
                    var sessionPace = UsagePaceCalculator.Compute(sessionRatio * 100.0, 300, sessionRemaining);
                    ApplyPace(sessionPace,
                        v => SessionPaceText = v, b => SessionPaceBrush = b, vis => SessionPaceVisibility = vis);

                    // Claude 週間（10080分固定）は行を増やさず WeeklyRemainingText 末尾へ予定比を付加
                    var weeklyPace = UsagePaceCalculator.Compute(weeklyRatio * 100.0, 10080, weeklyRemaining);
                    if (weeklyPace != null && weeklyPace.Stage != Models.PaceStage.OnTrack)
                        WeeklyRemainingText += $"（{FormatSignedDelta(weeklyPace.DeltaPercent)}）";
                }
                else
                {
                    SessionPaceVisibility = Visibility.Collapsed;
                }

                // ── F-07: 通知判定（API 取得成功時のみ。stale では誤発火防止のためスキップ）──
                if (isFromApi)
                {
                    var ns = _usageService.GetSettings();
                    _notificationService.Evaluate(UsageWindowKey.ClaudeSession,
                        (int)Math.Round(sessionRatio * 100.0), sessionResetAt, ns);
                    _notificationService.Evaluate(UsageWindowKey.ClaudeWeekly,
                        (int)Math.Round(weeklyRatio * 100.0), weeklyResetAt, ns);
                }

                if (isFromApi)
                    StatusText = $"API: {DateTime.Now:HH:mm}";
                else
                {
                    var apiError = _usageService.GetLastApiError();
                    StatusText = apiError != null ? $"エラー: {apiError}" : "接続エラー";
                }

                // ── GitHub Copilot ──
                await RefreshGitHubCopilotAsync();

                // ── Codex / OpenAI ──
                await RefreshCodexAsync();

                // デザイン刷新: 取得サイクル完了 → スパークライン再描画を促す（%同値でも履歴は伸びる）
                SparklineRevision++;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        /// <summary>
        /// GitHub Copilot の使用状況を取得してバインディングプロパティを更新する。
        /// 有効設定なければセクション非表示。使用量データがあればプログレスバーを表示。
        ///
        /// ちらつき対策:
        ///   表示の切り替え（バー⇄ドット）は取得が完了した後にのみ行う。
        ///   await 前にバーを Collapsed へ戻すと、スクレイピング待ち（最大十数秒）の間
        ///   バーが消えてドット表示になり、更新のたびにちらついていたため廃止した。
        ///   一時的な取得失敗時は前回の正常表示を維持し、"エラー" で上書きしない。
        /// </summary>
        private async Task RefreshGitHubCopilotAsync()
        {
            var settings = _usageService.GetSettings();

            if (!settings.GitHubCopilotEnabled)
            {
                GitHubSectionVisibility = Visibility.Collapsed;
                return;
            }

            // セクションは常に表示。バー/ドットの切り替えは取得完了後にのみ行う
            // （await 前にリセットしないことでちらつきを防ぐ）。
            GitHubSectionVisibility = Visibility.Visible;

            var data = await _usageService.FetchGitHubCopilotAsync();

            if (data == null)
            {
                // 一時的な取得失敗（タイムアウト等）では前回の正常表示を維持する。
                // 一度も取得できていない初回のみエラーを表示する。
                if (!_gitHubEverLoaded)
                {
                    GitHubStatusText = "エラー";
                    GitHubUserText   = _usageService.GetLastGitHubError() ?? "接続失敗";
                }
                else
                {
                    // F-02: 前回値を保持しつつ「情報が古い」ことを減光で示す
                    GitHubSectionOpacity = StaleOpacity;
                }
                return;
            }

            // 残り日数（右列サブ情報）
            GitHubUserText = data.DaysUntilRenewal >= 0
                ? $"更新まで {data.DaysUntilRenewal}日"
                : data.NextBillingDate.HasValue
                    ? $"更新: {data.NextBillingDate.Value.LocalDateTime:M/d}"
                    : "Active";

            if (data.HasUsageData && data.CreditsTotal > 0)
            {
                // 使用量プログレスバーを表示する
                GitHubOrgBarVisibility        = Visibility.Visible;
                GitHubIndividualDotVisibility = Visibility.Collapsed;

                double ratio           = Math.Min(1.0, (double)data.CreditsUsed / data.CreditsTotal);
                GitHubSeatsPercent     = ratio * 100.0;
                GitHubSeatsPercentText = $"{(int)(ratio * 100)}%";
                GitHubSeatsText        = $"{data.CreditsUsed}/{data.CreditsTotal} AI credits";
                GitHubStatusText       = $"{data.CreditsUsed}/{data.CreditsTotal}";

                // デザイン刷新 2b: メタ行に「クレジット ・ 更新情報」を集約し、%を自己記録する
                CopilotMetaText = $"{data.CreditsUsed}/{data.CreditsTotal} クレジット ・ {GitHubUserText}";
                _usageService.RecordHistory(UsageHistoryService.SeriesCopilot, ratio * 100.0, 0);
            }
            else
            {
                // データなし → ドット + Active 表示
                GitHubOrgBarVisibility        = Visibility.Collapsed;
                GitHubIndividualDotVisibility = Visibility.Visible;
                GitHubStatusText              = data.IsActive ? "Active" : "Inactive";

                // デザイン刷新 2b: バーが出せない個人プランは更新情報のみをメタ行に出す
                CopilotMetaText = GitHubUserText;
            }

            // 取得成功を記録（以降の一時的失敗では前回表示を維持する）
            _gitHubEverLoaded = true;
            // F-02: 取得成功で通常不透明度へ戻す
            GitHubSectionOpacity = 1.0;
        }

        /// <summary>
        /// Codex の使用制限状況を取得してバインディングプロパティを更新する。
        /// Codex は 5時間制限を廃止し週間制限のみ運用のため、週間制限をメインバー＋大数値として
        /// 表示する（F-13）。5時間枠のデータ層は据え置きだが、表示・記録・通知では参照しない。
        /// </summary>
        private async Task RefreshCodexAsync()
        {
            var settings = _usageService.GetSettings();

            if (!settings.CodexEnabled)
            {
                CodexSectionVisibility = Visibility.Collapsed;
                return;
            }

            CodexSectionVisibility = Visibility.Visible;

            var data = await _usageService.FetchCodexAsync();

            if (data == null)
            {
                if (!_codexEverLoaded)
                {
                    CodexStatusText = "エラー";
                    CodexSubText    = _usageService.GetLastCodexError() ?? "接続失敗";
                }
                else
                {
                    // F-02: 前回値を保持しつつ減光で古さを示す
                    CodexSectionOpacity = StaleOpacity;
                }
                // F-06: stale 時はペースを非表示（古いデータで誤解を与えない）
                CodexPaceVisibility = Visibility.Collapsed;
                return;
            }

            // F-04: 絶対表示モードでは Codex が保持済みのリセット時刻テキストを優先する
            var absolute = _usageService.GetSettings().ResetDisplayMode == "absolute";

            // F-13: Codex は 5時間制限を廃止し週間制限のみ運用となったため、週間枠(HasWeeklyData)を
            //       主軸に表示する。週間データがあればメインバー＋大数値、無ければ状態ドットへ落とす。
            //       （5時間枠 data.Session* は据え置き。将来復活しても壊れないよう参照しないだけに留める）
            if (data.HasWeeklyData)
            {
                // 週間制限 → メインプログレスバー＋大数値
                CodexBarVisibility  = Visibility.Visible;
                CodexDotVisibility  = Visibility.Collapsed;

                CodexUsagePercent     = data.WeeklyPercent;
                CodexUsagePercentText = $"{data.WeeklyPercent}%";
                CodexDetailText       = absolute && !string.IsNullOrEmpty(data.WeeklyResetText)
                    ? $"{data.WeeklyResetText} リセット"
                    : FormatNullableMinutes(data.WeeklyRemainingMinutes);

                // デザイン刷新 2b: メタ行の主テキスト（相対は "残り X"、絶対はリセット時刻をそのまま）
                CodexMetaPrimary = BuildMetaPrimary(absolute ? "absolute" : "relative", CodexDetailText);

                // 旧デザイン互換の右側ステータス（CodexStatusText/CodexSubText）も週間値で更新しておく
                CodexStatusText = $"{data.WeeklyPercent}%";
                CodexSubText    = CodexDetailText;
            }
            else
            {
                // 週間制限が取れない場合はドット表示にする
                CodexBarVisibility  = Visibility.Collapsed;
                CodexDotVisibility  = Visibility.Visible;
                CodexUsagePercentText = "0%";
                CodexDetailText       = "--";
                CodexMetaPrimary      = "--";
                CodexStatusText       = "--";
                CodexSubText          = "--";
            }

            // F-13: 週間%をメインバーへ昇格したため、見出しの週間チップは重複となる。常に非表示にする。
            CodexWeeklyChipVisibility = Visibility.Collapsed;

            _codexEverLoaded = true;

            // F-14: スパークライン用に週間枠の%を自己記録（週間データ取得時のみ）。
            //       記録値・ゲートを週間へ変更したことで Codex スパークラインが週間推移で描画される。
            if (data.HasWeeklyData)
                _usageService.RecordHistory(UsageHistoryService.SeriesCodex,
                    data.WeeklyPercent, data.WeeklyPercent);
            // F-02: 取得成功で通常不透明度へ戻す
            CodexSectionOpacity = 1.0;

            // ── F-06/F-13: Codex ペース（週間枠 10080分 のみ。5時間枠の優先計算は撤去）──
            if (settings.PaceEnabled)
            {
                Models.UsagePace? codexPace = null;
                if (data.HasWeeklyData && data.WeeklyRemainingMinutes >= 0)
                    codexPace = UsagePaceCalculator.Compute(data.WeeklyPercent, 10080, data.WeeklyRemainingMinutes);

                ApplyPace(codexPace,
                    v => CodexPaceText = v, b => CodexPaceBrush = b, vis => CodexPaceVisibility = vis);
            }
            else
            {
                CodexPaceVisibility = Visibility.Collapsed;
            }

            // ── F-07/F-13: Codex の通知判定（週間枠のみ。5時間枠 CodexSession の通知は撤去）──
            if (data.HasWeeklyData)
                _notificationService.Evaluate(UsageWindowKey.CodexWeekly, data.WeeklyPercent, null, settings);
        }

        /// <summary>同期版リフレッシュ（後方互換用）</summary>
        public void RefreshUsage() => _ = RefreshUsageAsync();

        /// <summary>
        /// オーバーレイが表示中か（MainWindow が IsVisibleChanged で更新）。F-10 の間隔判定に使う。
        /// </summary>
        public bool IsOverlayVisible { get; set; } = true;

        /// <summary>スヌーズ（更新一時停止）中か。トレイ減光判定（App.xaml.cs）が参照する。F-11。</summary>
        public bool IsSnoozing
        {
            get => _isSnoozing;
            private set => SetProperty(ref _isSnoozing, value);
        }

        /// <summary>タイマー間隔を設定変更後に再設定する（適応更新 OFF 時の固定間隔にも使う）。</summary>
        public void UpdateRefreshInterval()
        {
            var settings = _usageService.GetSettings();
            // 適応更新が有効ならその場で適応間隔を算出、無効なら固定間隔
            if (settings.AdaptiveRefreshEnabled)
                UpdateAdaptiveInterval();
            else
                _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        }

        /// <summary>
        /// ユーザー操作を記録する（F-10）。手動更新・ドラッグ・設定保存・表示切替・ログイン等から呼ぶ。
        /// 操作直後は間隔を基準値へ戻すため、その場で適応間隔を再計算する。
        /// </summary>
        public void NotifyUserInteraction()
        {
            _lastInteractionAt = DateTime.Now;
            UpdateAdaptiveInterval();
        }

        /// <summary>
        /// 適応更新間隔（F-10）を現在状況から再計算してタイマーへ適用する。
        /// 適応更新が無効なら固定間隔（RefreshIntervalSeconds）にする。
        /// </summary>
        public void UpdateAdaptiveInterval()
        {
            var settings = _usageService.GetSettings();
            if (!settings.AdaptiveRefreshEnabled)
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
                return;
            }

            _refreshTimer.Interval = AdaptiveRefreshPolicy.Compute(
                DateTime.Now, _lastInteractionAt, IsOverlayVisible,
                settings.RefreshIntervalSeconds, IsPowerConstrained());
        }

        /// <summary>
        /// 電源制約下か（バッテリー駆動かつ残量 20% 未満）を判定する（F-10）。
        /// 注意: Windows の「バッテリー節約機能」自体を直接判定する簡易 API が無いため、
        ///       残量ベースの近似とする（省電力モード検出は本実装ではスコープ外）。
        /// </summary>
        private static bool IsPowerConstrained()
        {
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                bool onBattery = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
                float battery  = ps.BatteryLifePercent; // 0.0〜1.0（不明時は 255 相当の大きな値）
                return onBattery && battery >= 0f && battery <= 0.20f;
            }
            catch
            {
                return false; // 取得不能時は制約なし扱い
            }
        }

        /// <summary>
        /// 指定時間だけ自動更新を一時停止する（F-11）。トレイメニューから呼ぶ。永続化しない。
        /// </summary>
        public void SnoozeFor(TimeSpan duration)
        {
            _snoozeUntil = DateTime.Now + duration;
            IsSnoozing   = true;
            StatusText   = $"一時停止中（〜{_snoozeUntil.Value:HH:mm}）";
        }

        /// <summary>スヌーズを解除する（F-11。手動更新・再開メニューから呼ぶ）。</summary>
        public void ClearSnooze()
        {
            _snoozeUntil = null;
            IsSnoozing   = false;
        }

        /// <summary>
        /// 現在の設定を取得する（トレイ描画・色判定のため App.xaml.cs / MainWindow が参照する）。
        /// </summary>
        public Models.AppSettings GetSettings() => _usageService.GetSettings();

        /// <summary>通知の送出先 NotifyIcon を注入する（F-07。App から起動時に呼ぶ）。</summary>
        public void AttachNotifier(System.Windows.Forms.NotifyIcon icon)
            => _notificationService.Attach(icon);

        /// <summary>セッションをリセットして表示を即時更新する</summary>
        public void ResetSession()
        {
            _usageService.ResetSession();
            RefreshUsage();
        }

        /// <summary>タイマーを停止し、遅延中の初回更新が破棄後に走らないようにする</summary>
        public void Dispose()
        {
            _disposed = true;
            _refreshTimer.Stop();
        }

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパー
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// アプリ起動直後の初回自動取得を少し遅らせてから実行する。
        /// Windows スタートアップ起動時はネットワーク・WebView2 プロファイル・Cookie DB の準備が
        /// 数秒遅れることがあるため、即時取得による初回失敗とログイン操作の競合を避ける。
        /// </summary>
        private async Task RefreshUsageAfterStartupDelayAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(InitialRefreshDelaySeconds));
            if (_disposed)
                return;

            await RefreshUsageAsync();
        }

        /// <summary>分数を "X日Y時間Z分" 形式に変換する</summary>
        private static string FormatMinutes(int totalMinutes)
        {
            if (totalMinutes <= 0) return "0分";
            var ts      = TimeSpan.FromMinutes(totalMinutes);
            int days    = (int)ts.TotalDays;
            int hours   = ts.Hours;
            int minutes = ts.Minutes;

            if (days > 0 && hours > 0)  return $"{days}日{hours}時間";
            if (days > 0)               return $"{days}日";
            if (hours > 0 && minutes > 0) return $"{hours}時間{minutes}分";
            if (hours > 0)              return $"{hours}時間";
            return $"{minutes}分";
        }

        /// <summary>
        /// 取得できなかった残り時間を "--" として表示し、取得済みの場合は通常の残り時間表記に変換する。
        /// </summary>
        private static string FormatNullableMinutes(int totalMinutes)
            => totalMinutes >= 0 ? FormatMinutes(totalMinutes) : "--";

        /// <summary>
        /// F-04: リセット時刻を表示形式（相対/絶対）に応じて文字列化する。
        ///
        /// - "absolute" かつ resetAt が非 null: 当日中なら "14:32 リセット"、
        ///   翌日以降なら "7/8 14:32 リセット"。
        /// - それ以外（"relative" もしくは resetAt が null）: 残り分数を相対表記へ（従来動作）。
        ///
        /// resetAt が null になるのは未使用アカウント（API が resets_at を返さない）や
        /// ローカルフォールバック時で、その場合は相対表示へフォールバックする。
        /// </summary>
        /// <param name="mode">表示モード（"relative" / "absolute"）</param>
        /// <param name="remainingMinutes">リセットまでの残り分数（相対表示・フォールバック用）</param>
        /// <param name="resetAt">リセット日時（ローカル）。null なら相対へフォールバック</param>
        private static string BuildResetText(string mode, int remainingMinutes, DateTime? resetAt)
        {
            if (mode == "absolute" && resetAt.HasValue)
            {
                var r = resetAt.Value;
                // 当日中は時刻のみ、日付が変わる場合は "M/d HH:mm" を付ける
                return r.Date == DateTime.Now.Date
                    ? $"{r:HH:mm} リセット"
                    : $"{r:M/d HH:mm} リセット";
            }

            // 相対表示（従来動作）
            return FormatMinutes(remainingMinutes);
        }

        /// <summary>
        /// デザイン刷新 2b: メタ行の主テキストを組み立てる。
        /// - 絶対表示（"14:32 リセット" 等）はそのまま返す。
        /// - 相対表示は "残り " を接頭する（ただし "--" や空は接頭せずそのまま）。
        /// </summary>
        /// <param name="mode">表示モード（"relative" / "absolute"）</param>
        /// <param name="resetText">BuildResetText / CodexDetailText の結果テキスト</param>
        private static string BuildMetaPrimary(string mode, string resetText)
        {
            if (mode == "absolute") return resetText;
            if (string.IsNullOrEmpty(resetText) || resetText == "--") return resetText;
            return $"残り {resetText}";
        }

        // ────────────────────────────────────────────────────────────────
        // ペース表示ヘルパー（F-06）
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ペース計算結果を表示テキスト・色・可否へ変換して、渡された setter 経由で反映する。
        /// null（計算不能・ゲート未達）なら行を非表示にする。
        /// </summary>
        private void ApplyPace(Models.UsagePace? pace,
            Action<string> setText, Action<MediaBrush> setBrush, Action<Visibility> setVisibility)
        {
            if (pace == null)
            {
                setVisibility(Visibility.Collapsed);
                return;
            }

            string text;
            MediaBrush brush;

            // デザイン刷新 2b: メタ行へ連結するため接頭辞を "ペース " に統一（コロンなし）。
            // View 側で "残り X ・ " に続けて 1 行に表示する。
            if (pace.Stage == Models.PaceStage.OnTrack)
            {
                text  = "ペース 順調";
                brush = PaceGray;
            }
            else if (pace.DeltaPercent >= 0)
            {
                // 先行（予定より速い）
                if (!pace.WillLastToReset && pace.Eta.HasValue)
                {
                    text  = $"ペース 予定比 {FormatSignedDelta(pace.DeltaPercent)} ・ {FormatEtaClock(pace.Eta.Value)} 上限";
                    brush = PaceRed;
                }
                else
                {
                    text  = $"ペース 予定比 {FormatSignedDelta(pace.DeltaPercent)}";
                    brush = PaceOrange;
                }
            }
            else
            {
                // 余裕（予定より遅い）
                text  = $"ペース 予定比 {FormatSignedDelta(pace.DeltaPercent)} ・ 余裕";
                brush = PaceGreen;
            }

            setText(text);
            setBrush(brush);
            setVisibility(Visibility.Visible);
        }

        /// <summary>予定比を符号付き整数％へ整形する（例: +8% / -12%）。</summary>
        private static string FormatSignedDelta(double delta)
        {
            long r = (long)Math.Round(delta);
            return (r >= 0 ? "+" : "") + r + "%";
        }

        /// <summary>
        /// 枯渇予測時刻を "16:40頃"（当日）/ "明日 9:20頃"（翌日）/ "7/8 9:20頃"（それ以降）へ整形する。
        /// </summary>
        private static string FormatEtaClock(TimeSpan eta)
        {
            var t     = DateTime.Now + eta;
            var today = DateTime.Now.Date;
            if (t.Date == today)             return $"{t:HH:mm}頃";
            if (t.Date == today.AddDays(1))  return $"明日 {t:H:mm}頃";
            return $"{t:M/d H:mm}頃";
        }

        /// <summary>16進表記から凍結済み WPF ブラシを生成する（ペース色の共有用）。</summary>
        private static MediaBrush HexBrush(string hex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ────────────────────────────────────────────────────────────────
        // INotifyPropertyChanged
        // ────────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 指定プロパティの変更を UI へ通知する。
        /// </summary>
        /// <param name="propertyName">
        /// 変更されたプロパティ名。呼び出し元メンバー名が自動補完される。
        /// </param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// バッキングフィールドへ値を設定する共通ヘルパー。
        /// 既存値と新規値が等しい場合は何もせず false を返し、不要な
        /// PropertyChanged 通知（=不要な UI 更新）を抑制する。
        /// 値が変化した場合のみフィールドを更新し、OnPropertyChanged を発火して true を返す。
        /// </summary>
        /// <typeparam name="T">プロパティの型</typeparam>
        /// <param name="field">更新対象のバッキングフィールド（ref 渡し）</param>
        /// <param name="value">設定する新しい値</param>
        /// <param name="propertyName">
        /// 通知するプロパティ名。呼び出し元プロパティ名が自動補完される。
        /// </param>
        /// <returns>値が変化して通知した場合 true、変化がなく何もしなかった場合 false</returns>
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // 既存値と等価なら通知不要（参照型・値型ともに既定の比較子で判定）
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
