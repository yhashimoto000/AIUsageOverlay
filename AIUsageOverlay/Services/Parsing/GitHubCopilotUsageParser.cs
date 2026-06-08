using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// GitHub Copilot の取得結果（傍受 JSON または DOM テキスト）を解析して
    /// <see cref="GitHubCopilotData"/> を生成する純粋なパーサ。
    ///
    /// 入力は <see cref="GitHubWebScraper"/> が取得した生文字列で、
    /// DOM テキストの場合は先頭に <see cref="PageTextPrefix"/> が付与されている。
    /// WebView2 から独立しているためサンプル文字列で単体検証できる。
    /// </summary>
    public static class GitHubCopilotUsageParser
    {
        /// <summary>DOM テキスト由来であることを示すプレフィックス（スクレイパーが付与）</summary>
        private const string PageTextPrefix = "__PAGETEXT__:";

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 取得した生文字列を解析して <see cref="GitHubCopilotData"/> を返す。
        /// プレフィックス付きなら DOM テキスト解析、そうでなければ JSON 解析に振り分ける。
        /// </summary>
        /// <param name="raw">傍受 JSON、または <see cref="PageTextPrefix"/> 付き DOM テキスト</param>
        /// <returns>解析結果。解析できない場合は null</returns>
        public static GitHubCopilotData? Parse(string raw)
        {
            if (raw.StartsWith(PageTextPrefix))
                return ParseFromPageText(raw[PageTextPrefix.Length..]);
            return ParseFromJson(raw);
        }

        // ────────────────────────────────────────────────────────────────
        // JSON 解析
        // ────────────────────────────────────────────────────────────────

        /// <summary>傍受した JSON から Copilot データを解析する</summary>
        private static GitHubCopilotData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var jsonLower = json.ToLowerInvariant();
                if (!jsonLower.Contains("copilot") && !jsonLower.Contains("credit"))
                    return null;

                DateTimeOffset? nextBilling = null;
                bool isActive    = false;
                int creditsUsed  = -1, creditsTotal = -1;

                FindCopilotFields(doc.RootElement,
                    ref nextBilling, ref isActive,
                    ref creditsUsed, ref creditsTotal);

                if (nextBilling.HasValue || isActive || creditsUsed >= 0)
                {
                    return new GitHubCopilotData
                    {
                        IsConnected      = true,
                        IsActive         = isActive || nextBilling.HasValue || creditsUsed >= 0,
                        CreditsUsed      = creditsUsed,
                        CreditsTotal     = creditsTotal,
                        HasUsageData     = creditsUsed >= 0 && creditsTotal > 0,
                        NextBillingDate  = nextBilling,
                        DaysUntilRenewal = nextBilling.HasValue
                            ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                            : -1
                    };
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// JSON ツリーを再帰探索して next_billing_date・status・AI credits を取得する
        /// </summary>
        private static void FindCopilotFields(
            JsonElement el,
            ref DateTimeOffset? nextBilling,
            ref bool isActive,
            ref int creditsUsed,
            ref int creditsTotal)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    // 次回リセット日
                    if ((key.Contains("next") && key.Contains("bill"))
                        || key is "next_billing_date" or "renewal_date" or "renews_at" or "reset_at" or "resets_at")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String
                            && DateTimeOffset.TryParse(prop.Value.GetString(), out var dt))
                            nextBilling = dt;
                    }

                    // ステータス
                    if (key is "status" or "state" or "subscription_status")
                    {
                        var val = (prop.Value.GetString() ?? "").ToLowerInvariant();
                        if (val is "active" or "enabled" or "paid")
                            isActive = true;
                    }

                    // AI credits 使用数・上限
                    if (key.Contains("credit") || key.Contains("quota") || key.Contains("allowance"))
                    {
                        if (key.Contains("used") || key.Contains("consumed") || key.Contains("spent"))
                            TryGetInt(prop.Value, ref creditsUsed);
                        else if (key.Contains("total") || key.Contains("limit")
                              || key.Contains("included") || key.Contains("max") || key.Contains("allowance"))
                            TryGetInt(prop.Value, ref creditsTotal);
                    }

                    FindCopilotFields(prop.Value, ref nextBilling, ref isActive,
                        ref creditsUsed, ref creditsTotal);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindCopilotFields(item, ref nextBilling, ref isActive,
                        ref creditsUsed, ref creditsTotal);
            }
        }

        /// <summary>JSON 値が非負の整数なら target に取り込む</summary>
        private static void TryGetInt(JsonElement el, ref int target)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) && v >= 0)
                target = v;
        }

        // ────────────────────────────────────────────────────────────────
        // DOM テキスト解析
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// DOM テキストから Copilot 情報を解析する。
        /// "18 / 1,500 AI credits" や "Resets in 26 days on Jul 1, 2026" を捕捉する。
        /// </summary>
        private static GitHubCopilotData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText)) return null;

            var ltext = pageText.ToLowerInvariant();
            if (!ltext.Contains("copilot") && !ltext.Contains("credit"))
                return null;

            // Active 判定: キャンセル文言がなければアクティブとみなす
            bool isActive = !ltext.Contains("cancel")
                         && !ltext.Contains("キャンセル")
                         && !ltext.Contains("inactive")
                         && !ltext.Contains("expired")
                         && !ltext.Contains("無効");

            // AI credits の使用量を抽出: "18 / 1,500 AI credits"
            var (creditsUsed, creditsTotal) = ExtractUsagePair(pageText, "credit");

            // 次回リセット日を抽出: "Resets in 26 days on Jul 1, 2026"
            DateTimeOffset? nextBilling = ExtractNextBillingDate(pageText);

            return new GitHubCopilotData
            {
                IsConnected      = true,
                IsActive         = isActive,
                CreditsUsed      = creditsUsed,
                CreditsTotal     = creditsTotal,
                HasUsageData     = creditsUsed >= 0 && creditsTotal > 0,
                NextBillingDate  = nextBilling,
                DaysUntilRenewal = nextBilling.HasValue
                    ? Math.Max(0, (int)(nextBilling.Value - DateTimeOffset.Now).TotalDays)
                    : -1
            };
        }

        /// <summary>
        /// ページテキストから次回リセット日を抽出する。
        /// 検索起点: "resets" / "next billing" / "次の請求"
        /// </summary>
        private static DateTimeOffset? ExtractNextBillingDate(string text)
        {
            var idx = text.IndexOf("resets", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = text.IndexOf("next billing", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = text.IndexOf("次の請求", StringComparison.OrdinalIgnoreCase);

            var searchText = idx >= 0
                ? text.Substring(idx, Math.Min(200, text.Length - idx))
                : text;

            // 英語フルネーム月: "August 1, 2026" / "Jul 1, 2026"
            var m1 = Regex.Match(searchText,
                @"(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},?\s+\d{4}",
                RegexOptions.IgnoreCase);
            if (m1.Success && DateTimeOffset.TryParse(m1.Value, out var dt1))
                return dt1;

            // ISO 形式: "2026-08-01"
            var m2 = Regex.Match(searchText, @"\d{4}-\d{2}-\d{2}");
            if (m2.Success && DateTimeOffset.TryParse(m2.Value, out var dt2))
                return dt2;

            return null;
        }

        /// <summary>
        /// ページテキストから使用量ペア（used, total）を抽出する。
        /// 対応パターン: "18 / 1,500 AI credits" / "150 of 300 credits"
        /// </summary>
        private static (int used, int total) ExtractUsagePair(string text, string keyword)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (-1, -1);

            var start = Math.Max(0, idx - 50);
            var chunk = text.Substring(start, Math.Min(300, text.Length - start));

            // "18 / 1,500" or "18 of 1,500"
            var m = Regex.Match(chunk, @"([\d,]+)\s*(?:of|\/)\s*([\d,]+)", RegexOptions.IgnoreCase);
            if (m.Success
                && TryParseNumber(m.Groups[1].Value, out var u)
                && TryParseNumber(m.Groups[2].Value, out var t))
                return (u, t);

            return (-1, -1);
        }

        /// <summary>カンマ区切りを許容して整数へパースする</summary>
        private static bool TryParseNumber(string s, out int result)
            => int.TryParse(s.Replace(",", ""), out result);
    }
}
