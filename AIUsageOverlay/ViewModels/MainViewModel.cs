using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
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

        /// <summary>
        /// 起動直後に Windows のネットワーク初期化や WebView2 プロファイル復元と競合しないよう、
        /// 初回の自動取得だけ短く遅延させる秒数。
        /// </summary>
        private const int InitialRefreshDelaySeconds = 5;

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

        /// <summary>5時間制限使用率バーの表示/非表示</summary>
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

        /// <summary>Codex 5時間制限使用率（0.0 ～ 100.0）</summary>
        public double CodexUsagePercent
        {
            get => _codexUsagePercent;
            set => SetProperty(ref _codexUsagePercent, value);
        }

        /// <summary>Codex 5時間制限使用率テキスト（例: "52%"）</summary>
        public string CodexUsagePercentText
        {
            get => _codexUsagePercentText;
            set => SetProperty(ref _codexUsagePercentText, value);
        }

        /// <summary>Codex 5時間制限の残り時間テキスト（例: "4時間11分"）</summary>
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
            _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync();
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
            }
            else
            {
                // データなし → ドット + Active 表示
                GitHubOrgBarVisibility        = Visibility.Collapsed;
                GitHubIndividualDotVisibility = Visibility.Visible;
                GitHubStatusText              = data.IsActive ? "Active" : "Inactive";
            }

            // 取得成功を記録（以降の一時的失敗では前回表示を維持する）
            _gitHubEverLoaded = true;
        }

        /// <summary>
        /// Codex の使用制限状況を取得してバインディングプロパティを更新する。
        /// 5時間制限を左側プログレスバー、週間制限を右側ステータスとして表示する。
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
                return;
            }

            if (data.HasSessionData)
            {
                // 5時間制限 → 左側プログレスバー
                CodexBarVisibility  = Visibility.Visible;
                CodexDotVisibility  = Visibility.Collapsed;

                CodexUsagePercent     = data.SessionPercent;
                CodexUsagePercentText = $"{data.SessionPercent}%";
                CodexDetailText       = FormatNullableMinutes(data.SessionRemainingMinutes);
            }
            else
            {
                // 5時間制限が取れない場合のみドット表示にする
                CodexBarVisibility  = Visibility.Collapsed;
                CodexDotVisibility  = Visibility.Visible;
                CodexUsagePercentText = "0%";
                CodexDetailText       = "--";
            }

            if (data.HasWeeklyData)
            {
                // 週間制限 → 右側ステータス
                CodexStatusText = $"{data.WeeklyPercent}%";
                CodexSubText    = FormatNullableMinutes(data.WeeklyRemainingMinutes);
            }
            else
            {
                CodexStatusText = "--";
                CodexSubText    = "--";
            }

            _codexEverLoaded = true;
        }

        /// <summary>同期版リフレッシュ（後方互換用）</summary>
        public void RefreshUsage() => _ = RefreshUsageAsync();

        /// <summary>タイマー間隔を設定変更後に再設定する</summary>
        public void UpdateRefreshInterval()
        {
            var settings = _usageService.GetSettings();
            _refreshTimer.Interval = TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        }

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
