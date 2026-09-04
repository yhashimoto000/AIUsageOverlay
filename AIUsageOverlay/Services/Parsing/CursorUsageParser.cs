using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// Cursor の取得結果（傍受 JSON または DOM テキスト）を解析して
    /// <see cref="CursorUsageData"/> を生成する純粋なパーサ。
    ///
    /// 入力は <see cref="CursorWebScraper"/> が取得した生文字列で、
    /// DOM テキストの場合は先頭に <see cref="PageTextPrefix"/> が付与されている。
    /// WebView2 から独立しているためサンプル文字列で単体検証できる。
    /// </summary>
    public static class CursorUsageParser
    {
        /// <summary>DOM テキスト由来であることを示すプレフィックス（スクレイパーが付与）</summary>
        private const string PageTextPrefix = "__PAGETEXT__:";

        /// <summary>
        /// 取得した生文字列を解析して <see cref="CursorUsageData"/> を返す。
        /// プレフィックス付きなら DOM テキスト解析、そうでなければ JSON 解析に振り分ける。
        /// </summary>
        /// <param name="raw">傍受 JSON、または <see cref="PageTextPrefix"/> 付き DOM テキスト</param>
        /// <returns>解析結果。解析できない場合は null</returns>
        public static CursorUsageData? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (raw.StartsWith(PageTextPrefix, StringComparison.Ordinal))
                return ParseFromPageText(raw[PageTextPrefix.Length..]);

            var payload = StripDiagnosticPrefix(raw);
            var trimmed = payload.TrimStart();
            return trimmed.StartsWith('{') || trimmed.StartsWith('[')
                ? ParseFromJson(payload)
                : ParseFromPageText(payload);
        }

        /// <summary>
        /// Scraper が診断用に付与した __URL__ / __SOURCE__ ヘッダーを取り除く。
        /// </summary>
        private static string StripDiagnosticPrefix(string raw)
        {
            if (!raw.StartsWith("__URL__:", StringComparison.Ordinal) &&
                !raw.StartsWith("__SOURCE__:", StringComparison.Ordinal))
                return raw;

            var lineBreak = raw.IndexOf('\n');
            return lineBreak >= 0 ? raw[(lineBreak + 1)..] : raw;
        }

        /// <summary>
        /// 傍受した JSON からプラン枠の使用率と請求サイクルを解析する。
        /// /api/usage-summary と /api/dashboard/get-current-period-usage の両方を受け付ける。
        /// </summary>
        private static CursorUsageData? ParseFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                var data = new CursorUsageData { IsConnected = true };

                ReadMembership(root, data);
                ReadBillingCycle(root, data);
                ReadPlanUsage(root, data);
                ReadOnDemand(root, data);

                if (!data.HasPlanData && !data.IsUnlimited && data.BillingCycleEnd is null)
                    return null;

                return data;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>membershipType / isUnlimited を読む。</summary>
        private static void ReadMembership(JsonElement root, CursorUsageData data)
        {
            if (root.TryGetProperty("membershipType", out var membership) &&
                membership.ValueKind == JsonValueKind.String)
            {
                data.MembershipType = membership.GetString();
            }

            if (root.TryGetProperty("isUnlimited", out var unlimited) &&
                unlimited.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                data.IsUnlimited = unlimited.GetBoolean();
            }
        }

        /// <summary>billingCycleEnd から残り日数・分数を算出する。</summary>
        private static void ReadBillingCycle(JsonElement root, CursorUsageData data)
        {
            if (!root.TryGetProperty("billingCycleEnd", out var endEl))
                return;

            var parsed = ReadDate(endEl);
            if (parsed is null)
                return;

            data.BillingCycleEnd = parsed;
            var remaining = parsed.Value - DateTimeOffset.Now;
            data.DaysUntilReset = Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
            data.RemainingMinutes = Math.Max(0, (int)remaining.TotalMinutes);
        }

        /// <summary>
        /// individualUsage.plan またはルート直下の percent 系フィールドを読む。
        /// totalPercentUsed を主軸にし、無ければ used/limit から算出する。
        /// </summary>
        private static void ReadPlanUsage(JsonElement root, CursorUsageData data)
        {
            JsonElement plan = default;
            var hasPlan = false;
            if (root.TryGetProperty("individualUsage", out var individual) &&
                individual.ValueKind == JsonValueKind.Object &&
                individual.TryGetProperty("plan", out plan) &&
                plan.ValueKind == JsonValueKind.Object)
            {
                hasPlan = true;
            }
            else if (root.TryGetProperty("plan", out plan) && plan.ValueKind == JsonValueKind.Object)
            {
                hasPlan = true;
            }

            if (hasPlan)
            {
                data.PlanUsed = ReadInt(plan, "used");
                data.PlanLimit = ReadInt(plan, "limit");
                data.AutoPercentUsed = ReadInt(plan, "autoPercentUsed");
                data.ApiPercentUsed = ReadInt(plan, "apiPercentUsed");

                var total = ReadInt(plan, "totalPercentUsed");
                if (total >= 0)
                {
                    data.PlanPercent = ClampPercent(total);
                    data.HasPlanData = true;
                }
                else if (data.PlanUsed >= 0 && data.PlanLimit > 0)
                {
                    data.PlanPercent = ClampPercent((int)Math.Round(data.PlanUsed * 100.0 / data.PlanLimit));
                    data.HasPlanData = true;
                }
                else if (data.ApiPercentUsed >= 0)
                {
                    data.PlanPercent = ClampPercent(data.ApiPercentUsed);
                    data.HasPlanData = true;
                }
                else if (data.AutoPercentUsed >= 0)
                {
                    data.PlanPercent = ClampPercent(data.AutoPercentUsed);
                    data.HasPlanData = true;
                }
            }

            if (!data.HasPlanData)
            {
                var total = ReadInt(root, "totalPercentUsed");
                var auto = ReadInt(root, "autoPercentUsed");
                var api = ReadInt(root, "apiPercentUsed");
                if (auto >= 0) data.AutoPercentUsed = auto;
                if (api >= 0) data.ApiPercentUsed = api;
                if (total >= 0)
                {
                    data.PlanPercent = ClampPercent(total);
                    data.HasPlanData = true;
                }
                else if (api >= 0)
                {
                    data.PlanPercent = ClampPercent(api);
                    data.HasPlanData = true;
                }
                else if (auto >= 0)
                {
                    data.PlanPercent = ClampPercent(auto);
                    data.HasPlanData = true;
                }
            }

            if (data.IsUnlimited)
                data.HasPlanData = true;
        }

        /// <summary>individualUsage.onDemand を読む。</summary>
        private static void ReadOnDemand(JsonElement root, CursorUsageData data)
        {
            if (!root.TryGetProperty("individualUsage", out var individual) ||
                individual.ValueKind != JsonValueKind.Object ||
                !individual.TryGetProperty("onDemand", out var onDemand) ||
                onDemand.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (onDemand.TryGetProperty("enabled", out var enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                data.OnDemandEnabled = enabled.GetBoolean();
            }

            data.OnDemandUsed = ReadInt(onDemand, "used");
        }

        /// <summary>
        /// ダッシュボードの DOM テキストから使用率を拾うフォールバック。
        /// JSON 傍受が失敗したときだけ使う。最初に見つかった 0〜100 の百分率をプラン枠とする。
        /// </summary>
        private static CursorUsageData? ParseFromPageText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lower = text.ToLowerInvariant();
            if (!lower.Contains("usage") && !lower.Contains("cursor") && !lower.Contains("%"))
                return null;

            var match = Regex.Match(text, @"(?:used|usage|included)[^\d%]{0,40}(\d{1,3})\s*%",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(text, @"(\d{1,3})\s*%\s+of\s+(?:your\s+)?included", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups[1].Value, out var percent))
                return null;

            percent = ClampPercent(percent);
            return new CursorUsageData
            {
                IsConnected = true,
                HasPlanData = true,
                PlanPercent = percent,
            };
        }

        /// <summary>JSON 数値または数値文字列を int として読む。無ければ -1。</summary>
        private static int ReadInt(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el))
                return -1;
            return el.ValueKind switch
            {
                JsonValueKind.Number when el.TryGetInt32(out var i) => i,
                JsonValueKind.Number when el.TryGetDouble(out var d) => (int)Math.Round(d),
                JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var s) => s,
                _ => -1,
            };
        }

        /// <summary>ISO 8601 または Unix ミリ秒の日付を読む。</summary>
        private static DateTimeOffset? ReadDate(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed;
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) &&
                    ms > 1_000_000_000_000)
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
            else if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n) && n > 1_000_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(n);
            }

            return null;
        }

        /// <summary>使用率を 0〜100 に収める。</summary>
        private static int ClampPercent(int value) => Math.Clamp(value, 0, 100);

        // ── get-filtered-usage-events パース ─────────────────────────────

        /// <summary>
        /// cursor.com ダッシュボードの get-filtered-usage-events レスポンス JSON を解析して
        /// <see cref="CursorTokenRecord"/> のリストを返す。
        ///
        /// 個人プランではページがロードするたびに発火するレスポンスを傍受する。
        /// isTokenBasedCall が false のイベントや tokenUsage が無いイベントはスキップする。
        /// </summary>
        /// <param name="json">傍受した JSON 文字列</param>
        /// <returns>パース成功したイベントのリスト。失敗時は空リスト</returns>
        public static List<CursorTokenRecord> ParseUsageEvents(string json)
        {
            var result = new List<CursorTokenRecord>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // usageEventsDisplay（ダッシュボード実レスポンス）または usageEvents（旧/Admin API）
                JsonElement eventsEl;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    eventsEl = root;
                }
                else if (root.TryGetProperty("usageEventsDisplay", out eventsEl) &&
                         eventsEl.ValueKind == JsonValueKind.Array)
                {
                    // OK
                }
                else if (root.TryGetProperty("usageEvents", out eventsEl) &&
                         eventsEl.ValueKind == JsonValueKind.Array)
                {
                    // OK
                }
                else
                {
                    return result;
                }

                foreach (var ev in eventsEl.EnumerateArray())
                {
                    if (ev.ValueKind != JsonValueKind.Object) continue;

                    // tokenUsage があれば計上する（isTokenBasedCall=false の Included でもトークンは付く）
                    if (!ev.TryGetProperty("tokenUsage", out var tu) ||
                        tu.ValueKind != JsonValueKind.Object)
                        continue;

                    var record = new CursorTokenRecord
                    {
                        Timestamp      = ReadString(ev, "timestamp"),
                        ConversationId = ReadString(ev, "conversationId"),
                        Model          = ReadString(ev, "model"),
                        InputTokens      = ReadLong(tu, "inputTokens"),
                        OutputTokens     = ReadLong(tu, "outputTokens"),
                        CacheWriteTokens = ReadLong(tu, "cacheWriteTokens"),
                        CacheReadTokens  = ReadLong(tu, "cacheReadTokens"),
                    };

                    // タイムスタンプが解析できないものはスキップ
                    if (!CursorTokenRecord.TryParseTimestamp(record.Timestamp, out _))
                        continue;

                    result.Add(record);
                }
            }
            catch (JsonException) { /* 壊れた JSON はスキップ */ }

            return result;
        }

        /// <summary>JSON 文字列プロパティを読む。無ければ空文字。</summary>
        private static string ReadString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return string.Empty;
            return el.ValueKind == JsonValueKind.String ? el.GetString() ?? string.Empty : el.ToString();
        }

        /// <summary>JSON 数値プロパティを long として読む。無ければ 0。</summary>
        private static long ReadLong(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var el)) return 0L;
            return el.ValueKind switch
            {
                JsonValueKind.Number when el.TryGetInt64(out var n) => n,
                JsonValueKind.Number when el.TryGetDouble(out var d) => (long)d,
                JsonValueKind.String when long.TryParse(el.GetString(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
                _ => 0L,
            };
        }
    }
}
