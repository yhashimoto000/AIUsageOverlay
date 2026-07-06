using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// claude.ai の Usage API レスポンス JSON を解析して
    /// <see cref="ScrapedUsageData"/> を生成する純粋なパーサ。
    ///
    /// WebView2 などの取得処理からは独立しているため、サンプル JSON を渡すだけで
    /// 解析仕様を単体検証できる。取得は <see cref="ClaudeApiClient"/> が担当する。
    /// </summary>
    public static class ClaudeUsageParser
    {
        // ────────────────────────────────────────────────────────────────
        // JSON デシリアライズ用モデル
        // ────────────────────────────────────────────────────────────────

        /// <summary>API レスポンスのルートオブジェクト</summary>
        private sealed class UsageResponse
        {
            [JsonPropertyName("five_hour")]
            public UsagePeriod? FiveHour { get; set; }

            [JsonPropertyName("seven_day")]
            public UsagePeriod? SevenDay { get; set; }
        }

        /// <summary>各制限期間（5時間 / 7日）の使用量データ</summary>
        private sealed class UsagePeriod
        {
            /// <summary>使用率（0.0 ～ 100.0 %）</summary>
            [JsonPropertyName("utilization")]
            public double Utilization { get; set; }

            /// <summary>
            /// リセット日時（ISO 8601 / UTC オフセット付き）。
            /// 使用率が 0% の場合など、未使用のときは API が null を返すため nullable にする。
            /// </summary>
            [JsonPropertyName("resets_at")]
            public DateTimeOffset? ResetsAt { get; set; }
        }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JSON テキストから <see cref="ScrapedUsageData"/> を生成する。
        /// five_hour / seven_day のいずれかが欠損している場合は null を返す。
        ///
        /// resets_at が null のケース（使用率 0% で未使用のとき API が null を返す）:
        ///   - セッション残り時間 → 5時間（300分）をそのまま残り時間として扱う
        ///   - 週間残り時間      → 7日（10080分）をそのまま残り時間として扱う
        /// </summary>
        /// <param name="json">傍受した Usage API レスポンスの JSON 文字列</param>
        /// <returns>解析結果。解析できない場合は null</returns>
        public static ScrapedUsageData? Parse(string json)
        {
            try
            {
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var resp = JsonSerializer.Deserialize<UsageResponse>(json, opts);

                if (resp?.FiveHour == null || resp.SevenDay == null)
                    return null;

                var now = DateTimeOffset.Now;

                // resets_at が null の場合（未使用で制限未到達）は各制限期間をフル残りとして扱う
                int sessionRemainingMinutes;
                if (resp.FiveHour.ResetsAt.HasValue)
                {
                    // リセット日時が分かっている場合は差分を計算する
                    var sessionRemaining = resp.FiveHour.ResetsAt.Value - now;
                    sessionRemainingMinutes = sessionRemaining.TotalMinutes > 0
                                             ? (int)sessionRemaining.TotalMinutes : 0;
                }
                else
                {
                    // null = まだリセット不要（使用率 0%）→ 5時間フル残り
                    sessionRemainingMinutes = 5 * 60;
                }

                int weeklyRemainingMinutes;
                if (resp.SevenDay.ResetsAt.HasValue)
                {
                    // リセット日時が分かっている場合は差分を計算する
                    var weeklyRemaining = resp.SevenDay.ResetsAt.Value - now;
                    weeklyRemainingMinutes = weeklyRemaining.TotalMinutes > 0
                                            ? (int)weeklyRemaining.TotalMinutes : 0;
                }
                else
                {
                    // null = まだリセット不要（使用率 0%）→ 7日フル残り
                    weeklyRemainingMinutes = 7 * 24 * 60;
                }

                return new ScrapedUsageData
                {
                    SessionPercent          = (int)Math.Round(resp.FiveHour.Utilization),
                    SessionRemainingMinutes = sessionRemainingMinutes,
                    WeeklyPercent           = (int)Math.Round(resp.SevenDay.Utilization),
                    WeeklyRemainingMinutes  = weeklyRemainingMinutes,
                    // F-04: 残り分数への変換に加えて、リセット日時そのものをローカル時刻で保持する。
                    // resets_at が null（未使用）のときは絶対表示できないため null のまま渡し、
                    // 表示側（MainViewModel）で相対表示へフォールバックさせる。
                    SessionResetAt = resp.FiveHour.ResetsAt?.LocalDateTime,
                    WeeklyResetAt  = resp.SevenDay.ResetsAt?.LocalDateTime
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
