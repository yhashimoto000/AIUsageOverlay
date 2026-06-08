using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// claude.ai の Usage JSON API を呼び出して使用量データを取得するクラス。
    ///
    /// 使用エンドポイント:
    ///   GET https://claude.ai/api/organizations/{organizationId}/usage
    ///
    /// レスポンス JSON の構造（2026 年時点）:
    /// <code>
    /// {
    ///   "five_hour":  { "utilization": 66.0, "resets_at": "2026-05-13T04:10:00+00:00" },
    ///   "seven_day":  { "utilization": 35.0, "resets_at": "2026-05-16T15:00:00+00:00" },
    ///   ...
    /// }
    /// </code>
    ///
    /// フィールド対応:
    ///   five_hour.utilization  → セッション使用率 (0.0 ～ 100.0 %)
    ///   five_hour.resets_at    → セッションリセット時刻 (ISO 8601 / DateTimeOffset)
    ///   seven_day.utilization  → 週間使用率 (0.0 ～ 100.0 %)
    ///   seven_day.resets_at    → 週間リセット時刻 (ISO 8601 / DateTimeOffset)
    /// </summary>
    public class ClaudeWebScraper
    {
        // ────────────────────────────────────────────────────────────────
        // 定数 / 静的フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// API エンドポイントのテンプレート URL。
        /// {0} に organizationId（UUID 文字列）が入る。
        /// 例: https://claude.ai/api/organizations/31157c36-.../usage
        /// </summary>
        private const string ApiUrlTemplate =
            "https://claude.ai/api/organizations/{0}/usage";

        /// <summary>
        /// 静的 HttpClient インスタンス（ソケット使い捨てを防ぐために static にする）。
        /// UseCookies=false にして Cookie ヘッダーを手動管理する。
        /// </summary>
        private static readonly HttpClient HttpClient = new(
            new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>JSON デシリアライズ用オプション（プロパティ名の大文字小文字を無視）</summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // ────────────────────────────────────────────────────────────────
        // JSON デシリアライズ用モデル（内部クラス）
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// API レスポンスのルートオブジェクト。
        /// five_hour / seven_day の 2 つの使用期間データを保持する。
        /// </summary>
        private sealed class UsageResponse
        {
            /// <summary>5時間セッション制限の使用状況</summary>
            [JsonPropertyName("five_hour")]
            public UsagePeriod? FiveHour { get; set; }

            /// <summary>7日間週間制限の使用状況</summary>
            [JsonPropertyName("seven_day")]
            public UsagePeriod? SevenDay { get; set; }
        }

        /// <summary>
        /// 各制限期間（5時間 / 7日）の使用量データ。
        /// utilization が使用率（%）、resets_at がリセット時刻を表す。
        /// </summary>
        private sealed class UsagePeriod
        {
            /// <summary>
            /// 使用率（0.0 ～ 100.0）。
            /// API は % 単位で返すため、比率に変換する場合は 100 で割る。
            /// </summary>
            [JsonPropertyName("utilization")]
            public double Utilization { get; set; }

            /// <summary>
            /// リセット日時（ISO 8601 形式 / UTC オフセット付き）。
            /// DateTimeOffset で受け取ることでタイムゾーン変換を自動化する。
            /// 例: "2026-05-13T04:10:00.402800+00:00"
            /// </summary>
            [JsonPropertyName("resets_at")]
            public DateTimeOffset ResetsAt { get; set; }
        }

        // ────────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 直前の API 呼び出しで発生したエラーの説明。
        /// 成功時は null。失敗時は "HTTP 401" / "Timeout" / "ParseError" などが入る。
        /// </summary>
        public string? LastError { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// claude.ai の Usage JSON API を呼び出して ScrapedUsageData を返す。
        ///
        /// 失敗条件（null を返す場合）:
        ///   - cookieHeader または organizationId が空
        ///   - HTTP エラー（401 Unauthorized, 403 Forbidden など）
        ///   - タイムアウト（15 秒）
        ///   - JSON パースエラー
        ///   - five_hour / seven_day フィールドが欠損
        /// </summary>
        /// <param name="cookieHeader">
        /// ブラウザの DevTools (F12) → Network タブ → usage リクエスト
        /// → Headers → Request Headers → cookie: の値全体。
        /// </param>
        /// <param name="organizationId">
        /// ユーザーの組織 ID（UUID 形式）。
        /// URL 中の /api/organizations/{ここ}/usage の部分。
        /// 例: 31157c36-5b2c-4c34-92b4-64df9cc16a86
        /// </param>
        /// <returns>取得・パース成功時は ScrapedUsageData、失敗時は null</returns>
        public async Task<ScrapedUsageData?> ScrapeAsync(string cookieHeader, string organizationId)
        {
            // エラー情報をリセットする
            LastError = null;

            // ── 入力バリデーション ──
            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                LastError = "Cookie未設定";
                return null;
            }

            if (string.IsNullOrWhiteSpace(organizationId))
            {
                LastError = "OrgID未設定";
                return null;
            }

            try
            {
                // 組織 ID を URL に埋め込む
                var apiUrl = string.Format(ApiUrlTemplate, organizationId.Trim());

                // リクエストごとに新しい HttpRequestMessage を生成する（再利用不可のため）
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

                // セッション Cookie を送信する（API 認証に必要）
                request.Headers.Add("Cookie", cookieHeader);

                // ── 標準ブラウザヘッダー ──
                request.Headers.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/124.0.0.0 Safari/537.36");
                request.Headers.Add("Accept", "*/*");
                request.Headers.Add("Accept-Language", "ja,en-US;q=0.9,en;q=0.8");
                request.Headers.Add("Origin", "https://claude.ai");
                request.Headers.Add("Referer", "https://claude.ai/settings/usage");

                // ── Fetch/XHR 固有の Sec-Fetch ヘッダー ──
                request.Headers.Add("sec-fetch-dest", "empty");
                request.Headers.Add("sec-fetch-mode", "cors");
                request.Headers.Add("sec-fetch-site", "same-origin");

                // ── Chrome ブランドヘッダー ──
                request.Headers.Add("sec-ch-ua",
                    "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
                request.Headers.Add("sec-ch-ua-mobile", "?0");
                request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");

                // ── Anthropic クライアント識別ヘッダー（403 回避に必須）──
                // anthropic-anonymous-id: Cookie 内の ajs_anonymous_id から自動抽出する
                var anonymousId = ExtractCookieValue(cookieHeader, "ajs_anonymous_id");
                if (anonymousId != null)
                    request.Headers.Add("anthropic-anonymous-id", anonymousId);

                // anthropic-device-id: Cookie 内の anthropic-device-id から自動抽出する
                var deviceId = ExtractCookieValue(cookieHeader, "anthropic-device-id");
                if (deviceId != null)
                    request.Headers.Add("anthropic-device-id", deviceId);

                // クライアントプラットフォーム・バージョン情報（固定値）
                request.Headers.Add("anthropic-client-platform", "web_claude_ai");
                request.Headers.Add("anthropic-client-version", "1.0.0");
                request.Headers.Add("anthropic-client-sha",
                    "1686e5eda398398f8f69090a3dff1e369a5835a9");

                var response = await HttpClient.SendAsync(request);

                // HTTP エラー（401, 403, 404 など）の場合はステータスコードとボディを記録して null を返す
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    // ボディが長い場合は先頭 80 文字だけ取得する
                    var snippet = body.Length > 80 ? body[..80] : body;
                    LastError = $"HTTP {(int)response.StatusCode}: {snippet}";
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();

                var result = ParseUsage(json);
                if (result == null)
                    LastError = "ParseError";
                return result;
            }
            catch (TaskCanceledException)
            {
                // タイムアウト（15秒）
                LastError = "Timeout";
                return null;
            }
            catch (Exception ex)
            {
                // その他のネットワークエラー
                LastError = ex.GetType().Name;
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 内部パースメソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Cookie ヘッダー文字列から指定した Cookie 名の値を抽出する。
        /// 例: "ajs_anonymous_id=abc123; sessionKey=sk-ant-..." → "abc123"
        /// 見つからない場合は null を返す。
        /// </summary>
        /// <param name="cookieHeader">Cookie ヘッダーの値全体</param>
        /// <param name="cookieName">抽出したい Cookie の名前</param>
        /// <returns>Cookie の値、または null</returns>
        private static string? ExtractCookieValue(string cookieHeader, string cookieName)
        {
            var prefix = cookieName + "=";
            var idx = cookieHeader.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            var start = idx + prefix.Length;
            var end = cookieHeader.IndexOf(';', start);

            // セミコロンがなければ末尾まで、あればその手前まで取得する
            return end < 0
                ? cookieHeader[start..].Trim()
                : cookieHeader[start..end].Trim();
        }

        /// <summary>
        /// API レスポンスの JSON 文字列を ScrapedUsageData に変換する。
        ///
        /// 残り時間の計算:
        ///   残り分数 = resets_at - DateTimeOffset.Now（負の場合は 0 にクランプ）
        ///
        /// five_hour または seven_day が null の場合は null を返す。
        /// </summary>
        /// <param name="json">API レスポンスの JSON テキスト</param>
        /// <returns>変換結果、またはパース失敗時は null</returns>
        private static ScrapedUsageData? ParseUsage(string json)
        {
            // JSON をデシリアライズする
            var resp = JsonSerializer.Deserialize<UsageResponse>(json, JsonOptions);

            // 必須フィールドが欠損している場合は null を返す
            if (resp?.FiveHour == null || resp.SevenDay == null)
                return null;

            var now = DateTimeOffset.Now;

            // ── セッション残り時間を計算する ──
            // resets_at（UTC オフセット付き）と現在時刻の差分を分に変換する
            var sessionRemaining = resp.FiveHour.ResetsAt - now;
            int sessionRemainingMinutes = sessionRemaining.TotalMinutes > 0
                ? (int)sessionRemaining.TotalMinutes
                : 0;

            // ── 週間残り時間を計算する ──
            var weeklyRemaining = resp.SevenDay.ResetsAt - now;
            int weeklyRemainingMinutes = weeklyRemaining.TotalMinutes > 0
                ? (int)weeklyRemaining.TotalMinutes
                : 0;

            return new ScrapedUsageData
            {
                // utilization は 0.0 ～ 100.0 の % 値なので四捨五入して int に変換する
                SessionPercent          = (int)Math.Round(resp.FiveHour.Utilization),
                SessionRemainingMinutes = sessionRemainingMinutes,
                WeeklyPercent           = (int)Math.Round(resp.SevenDay.Utilization),
                WeeklyRemainingMinutes  = weeklyRemainingMinutes
            };
        }
    }
}
