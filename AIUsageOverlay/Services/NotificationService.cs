using System.Windows.Forms;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>通知判定対象の窓の識別子（F-07）。</summary>
    public enum UsageWindowKey
    {
        /// <summary>Claude セッション（5時間枠）。</summary>
        ClaudeSession,
        /// <summary>Claude 週間枠。</summary>
        ClaudeWeekly,
        /// <summary>Codex 5時間枠。</summary>
        CodexSession,
        /// <summary>Codex 週間枠。</summary>
        CodexWeekly
    }

    /// <summary>
    /// 使用率の閾値超過・リセット完了・上限到達を検知して Windows 通知を出すサービス（F-07）。
    ///
    /// 通知手段は <see cref="NotifyIcon.ShowBalloonTip(int, string, string, ToolTipIcon)"/>。
    /// 追加依存なしで、Windows 10/11 ではトースト形式で表示される。将来のリッチ通知への
    /// 差し替えを見据え、送出はこのクラスに閉じる。
    ///
    /// 判定状態（窓ごとの通知済み閾値・前回値・前回リセット時刻）は**メモリのみ**保持し永続化しない。
    /// アプリ再起動時は状態がリセットされる（＝再通知を許容する）仕様。
    /// stale（取得失敗）時は呼び出し側が Evaluate を呼ばないことで誤発火を防ぐ。
    /// </summary>
    public sealed class NotificationService
    {
        /// <summary>通知の送出先。App から注入される。未注入なら通知は送られない。</summary>
        private NotifyIcon? _icon;

        // ── 窓ごとの判定状態（メモリのみ）──
        /// <summary>窓ごとの「通知済み閾値」集合。リセット検知でクリアする。</summary>
        private readonly Dictionary<UsageWindowKey, HashSet<int>> _notifiedThresholds = new();
        /// <summary>窓ごとの前回使用率（跨ぎ判定・急落検知に使用）。</summary>
        private readonly Dictionary<UsageWindowKey, int> _lastPercent = new();
        /// <summary>窓ごとの前回リセット時刻（通過検知に使用）。</summary>
        private readonly Dictionary<UsageWindowKey, DateTime?> _lastResetAt = new();
        /// <summary>窓ごとの「上限到達を通知済み」フラグ。リセット検知でクリアする。</summary>
        private readonly HashSet<UsageWindowKey> _exhaustedNotified = new();

        /// <summary>通知の送出先 NotifyIcon を受け取る（App から注入）。</summary>
        public void Attach(NotifyIcon icon) => _icon = icon;

        /// <summary>
        /// 使用率の通知状態機械とは独立した一般情報のバルーン通知を送出する。F-23。
        /// 更新通知など、閾値判定を必要としないアプリケーション情報に使用する。
        /// </summary>
        /// <param name="title">通知タイトル</param>
        /// <param name="message">通知本文</param>
        public void NotifyInfo(string title, string message)
            => _icon?.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

        /// <summary>
        /// 窓の現在値を評価し、閾値跨ぎ・リセット・上限到達を検知して通知を発火する。
        /// 初回（その窓の前回値が無い）呼び出しはベースライン記録のみで通知しない（起動時の連発防止）。
        /// </summary>
        /// <param name="window">対象の窓</param>
        /// <param name="percent">今回の使用率（0〜100）</param>
        /// <param name="resetsAt">リセット日時（取得できなければ null。null 時は急落でリセット代替検知）</param>
        /// <param name="settings">通知の有効可否・閾値・各種フラグ</param>
        public void Evaluate(UsageWindowKey window, int percent, DateTime? resetsAt, AppSettings settings)
        {
            // 前回値の有無（初回はベースライン扱い）
            bool hasPrev = _lastPercent.TryGetValue(window, out int prevPercent);
            _lastResetAt.TryGetValue(window, out DateTime? prevReset);

            // 状態を先に更新しておく（早期 return でも記録は残す）
            _lastPercent[window] = percent;
            _lastResetAt[window] = resetsAt;

            // 通知 OFF・未注入・初回 は通知せず、状態記録のみ
            if (!settings.NotificationsEnabled || _icon == null || !hasPrev)
                return;

            // 1) リセット検知（最優先。検知したら通知済み集合をクリアして誤跨ぎ通知を防ぐ）
            bool reset =
                (prevReset.HasValue && DateTime.Now > prevReset.Value) // ResetAt を通過した
                || (prevPercent - percent >= 30);                       // 代替: 使用率が 30pt 以上急落
            if (reset)
            {
                GetThresholdSet(window).Clear();
                _exhaustedNotified.Remove(window);

                if (settings.NotifyOnReset)
                    Notify($"{Label(window)}がリセットされました（現在 {percent}%）");

                return; // リセット直後は閾値・上限判定をしない
            }

            // 2) 閾値跨ぎ（前回値 < 閾値 ≤ 今回値 を、窓ごとに 1 回だけ通知）
            var notified = GetThresholdSet(window);
            foreach (var threshold in settings.NotificationThresholds)
            {
                if (prevPercent < threshold && percent >= threshold && notified.Add(threshold))
                {
                    var resetHint = resetsAt.HasValue ? $"（リセットは {resetsAt.Value:HH:mm}）" : "";
                    Notify($"{Label(window)} {threshold}% 到達{resetHint}");
                }
            }

            // 3) 上限到達（閾値設定と独立。窓ごとに 1 回）
            if (settings.NotifyOnExhausted && percent >= 100 && _exhaustedNotified.Add(window))
                Notify($"{Label(window)} 100% 到達（上限）");
        }

        /// <summary>窓の通知済み閾値集合を取得（無ければ生成）。</summary>
        private HashSet<int> GetThresholdSet(UsageWindowKey window)
        {
            if (!_notifiedThresholds.TryGetValue(window, out var set))
            {
                set = new HashSet<int>();
                _notifiedThresholds[window] = set;
            }
            return set;
        }

        /// <summary>バルーン通知を送出する（NotifyIcon 未注入時は何もしない）。</summary>
        private void Notify(string message)
        {
            // NotifyIcon.ShowBalloonTip は UI スレッドから呼ぶ。呼び出し元（ViewModel の更新）は
            // UI スレッド上で動作するため、ここでは直接呼び出す。
            _icon?.ShowBalloonTip(5000, "AIUsageOverlay", message, ToolTipIcon.Info);
        }

        /// <summary>窓の表示名を返す。</summary>
        private static string Label(UsageWindowKey window) => window switch
        {
            UsageWindowKey.ClaudeSession => "Claude セッション",
            UsageWindowKey.ClaudeWeekly  => "Claude 週間",
            UsageWindowKey.CodexSession  => "Codex 5時間",
            UsageWindowKey.CodexWeekly   => "Codex 週間",
            _                            => "使用量"
        };
    }
}
