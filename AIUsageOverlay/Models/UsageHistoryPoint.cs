using System.Text.Json.Serialization;

namespace AIUsageOverlay.Models
{
    /// <summary>
    /// 使用率履歴の 1 点を表す POCO（デザイン刷新のスパークライン用）。
    ///
    /// アプリが定期取得している使用率%を自己記録して推移を描くための最小単位。
    /// API の履歴取得は不要で、取得成功のたびに 1 点を追記する。
    /// %AppData%\AIUsageOverlay\history.json にサービスごとの配列として永続化される。
    /// </summary>
    public class UsageHistoryPoint
    {
        /// <summary>記録時刻（ローカル）。5 時間より古い点は破棄判定に使う。</summary>
        [JsonPropertyName("t")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 主使用率（%、0〜100）。
        /// Claude=セッション / Copilot=クレジット / Codex=5時間枠。スパークライン描画に使う。
        /// </summary>
        [JsonPropertyName("s")]
        public double Session { get; set; }

        /// <summary>
        /// 週間使用率（%、0〜100）。週間枠を持たないサービス（Copilot）は 0。
        /// 現状スパークラインは Session のみ描画するが、将来用に併せて記録する。
        /// </summary>
        [JsonPropertyName("w")]
        public double Weekly { get; set; }
    }
}
