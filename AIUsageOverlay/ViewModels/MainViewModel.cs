using System.Collections.Generic;
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

        /// <summary>クレジット使用率バーの表示/非表示</summary>
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

        /// <summary>クレジット使用率（0.0 ～ 100.0）</summary>
        public double CodexUsagePercent
        {
            get => _codexUsagePercent;
            set => SetProperty(ref _codexUsagePercent, value);
        }

        /// <summary>クレジット使用率テキスト（例: "52%"）</summary>
        public string CodexUsagePercentText
        {
            get => _codexUsagePercentText;
            set => SetProperty(ref _codexUsagePercentText, value);
        }

        /// <summary>クレジット詳細テキスト（例: "$5.23 残高"）</summary>
        public string CodexDetailText
        {
            get => _codexDetailText;
            set => SetProperty(ref _codexDetailText, value);
        }

        /// <summary>ステータステキスト（例: "$5.23" / "エラー"）</summary>
        public string CodexStatusText
        {
            get => _codexStatusText;
            set => SetProperty(ref _codexStatusText, value);
        }

        /// <summary>サブテキスト（例: "今月 $3.47 使用"）</summary>
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

        /// <summary>
        /// GitHub Copilot の使用状況を取得してバインディングプロパティを更新する。
        /// 有効設定なければセクション非表示。使用量データがあればプログレスバーを表示。
        /// </summary>
        private async Task RefreshGitHubCopilotAsync()
        {
            var settings = _usageService.GetSettings();

            if (!settings.GitHubCopilotEnabled)
            {
                GitHubSectionVisibility = Visibility.Collapsed;
                return;
            }

            GitHubSectionVisibility       = Visibility.Visible;
            GitHubOrgBarVisibility        = Visibility.Collapsed;
            GitHubIndividualDotVisibility = Visibility.Visible;

            var data = await _usageService.FetchGitHubCopilotAsync();

            if (data == null)
            {
                GitHubStatusText = "エラー";
                GitHubUserText   = _usageService.GetLastGitHubError() ?? "接続失敗";
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
        }

        /// <summary>
        /// OpenAI / Codex の使用状況を取得してバインディングプロパティを更新する。
        /// クレジット残高があればプログレスバー表示。なければステータスドット表示。
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
            CodexBarVisibility     = Visibility.Collapsed;
            CodexDotVisibility     = Visibility.Visible;

            var data = await _usageService.FetchCodexAsync();

            if (data == null)
            {
                CodexStatusText = "エラー";
                CodexSubText    = _usageService.GetLastCodexError() ?? "接続失敗";
                return;
            }

            if (data.HasCreditData && data.CreditTotal > 0)
            {
                // クレジット残高 + 上限 → 使用率プログレスバー
                CodexBarVisibility  = Visibility.Visible;
                CodexDotVisibility  = Visibility.Collapsed;

                decimal used  = data.CreditTotal - data.CreditBalance;
                double  ratio = Math.Min(1.0, (double)(used / data.CreditTotal));
                CodexUsagePercent     = ratio * 100.0;
                CodexUsagePercentText = $"{(int)(ratio * 100)}%";
                CodexDetailText       = $"残 ${data.CreditBalance:F2}";
                CodexStatusText       = $"${data.CreditBalance:F2}";
                CodexSubText          = data.HasUsageData
                    ? $"今月 ${data.MonthlyUsageUsd:F2} 使用"
                    : "残高";
            }
            else if (data.HasCreditData)
            {
                // 残高のみ（上限不明）
                CodexBarVisibility  = Visibility.Collapsed;
                CodexDotVisibility  = Visibility.Visible;
                CodexStatusText     = $"${data.CreditBalance:F2}";
                CodexSubText        = "残高";
                CodexDetailText     = "";
            }
            else if (data.HasUsageData)
            {
                // 使用額のみ
                CodexBarVisibility  = Visibility.Collapsed;
                CodexDotVisibility  = Visibility.Visible;
                CodexStatusText     = "Connected";
                CodexSubText        = $"今月 ${data.MonthlyUsageUsd:F2} 使用";
                CodexDetailText     = "";
            }
            else
            {
                CodexDotVisibility  = Visibility.Visible;
                CodexStatusText     = "Connected";
                CodexSubText        = "取得中";
                CodexDetailText     = "";
            }
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

        /// <summary>タイマーを停止してリソースを解放する</summary>
        public void Dispose() => _refreshTimer.Stop();

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパー
        // ────────────────────────────────────────────────────────────────

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
