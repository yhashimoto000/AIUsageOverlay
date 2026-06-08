using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// OpenAI / Codex の取得結果（傍受 JSON または DOM テキスト）を解析して
    /// <see cref="CodexUsageData"/> を生成する純粋なパーサ。
    ///
    /// 入力は <see cref="CodexWebScraper"/> が取得した生文字列で、
    /// DOM テキストの場合は先頭に <see cref="PageTextPrefix"/> が付与されている。
    /// WebView2 から独立しているためサンプル文字列で単体検証できる。
    /// </summary>
    public static class CodexUsageParser
    {
        /// <summary>DOM テキスト由来であることを示すプレフィックス（スクレイパーが付与）</summary>
        private const string PageTextPrefix = "__PAGETEXT__:";

        // ────────────────────────────────────────────────────────────────
        // 公開メソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 取得した生文字列を解析して <see cref="CodexUsageData"/> を返す。
        /// プレフィックス付きなら DOM テキスト解析、そうでなければ JSON 解析に振り分ける。
        /// </summary>
        /// <param name="raw">傍受 JSON、または <see cref="PageTextPrefix"/> 付き DOM テキスト</param>
        /// <returns>解析結果。解析できない場合は null</returns>
        public static CodexUsageData? Parse(string raw)
        {
            if (raw.StartsWith(PageTextPrefix))
                return ParseFromPageText(raw[PageTextPrefix.Length..]);
            return ParseFromJson(raw);
        }

        // ────────────────────────────────────────────────────────────────
        // JSON 解析
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 傍受した JSON からクレジット残高・使用量を解析する。
        ///
        /// OpenAI Billing API レスポンスの例:
        ///   { "total_available": 5.23, "total_granted": 10.00 }
        ///   { "total_usage": 3.47 }  ← /dashboard/billing/usage
        /// </summary>
        private static CodexUsageData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var lj  = json.ToLowerInvariant();

                // billing/credit/usage 関連でなければスキップ
                if (!lj.Contains("credit") && !lj.Contains("balance")
                    && !lj.Contains("usage") && !lj.Contains("grant"))
                    return null;

                decimal balance  = -1m, total   = -1m, monthlyUsage = -1m;
                FindBillingFields(doc.RootElement, ref balance, ref total, ref monthlyUsage);

                if (balance >= 0 || monthlyUsage >= 0)
                {
                    return new CodexUsageData
                    {
                        IsConnected      = true,
                        CreditBalance    = balance,
                        CreditTotal      = total,
                        HasCreditData    = balance >= 0,
                        MonthlyUsageUsd  = monthlyUsage,
                        HasUsageData     = monthlyUsage >= 0
                    };
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// JSON ツリーを再帰探索して残高・使用量フィールドを取得する。
        ///
        /// 対応キー候補:
        ///   残高系: total_available, hard_limit_usd, balance, available_credit
        ///   使用量系: total_usage, amount_due, usage_usd
        /// </summary>
        private static void FindBillingFields(
            JsonElement el,
            ref decimal balance,
            ref decimal total,
            ref decimal monthlyUsage)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();

                    // 残高系
                    if (key is "total_available" or "balance" or "available_credit"
                        or "credits_remaining" or "credit_balance")
                        TryGetDecimal(prop.Value, ref balance);

                    // 上限・付与系
                    if (key is "total_granted" or "hard_limit_usd" or "soft_limit_usd"
                        or "total_credit" or "credit_total")
                        TryGetDecimal(prop.Value, ref total);

                    // 使用量系
                    if (key is "total_usage" or "amount_due" or "usage_usd"
                        or "total_billed" or "current_usage")
                        TryGetDecimal(prop.Value, ref monthlyUsage);

                    FindBillingFields(prop.Value, ref balance, ref total, ref monthlyUsage);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindBillingFields(item, ref balance, ref total, ref monthlyUsage);
            }
        }

        /// <summary>JSON 値が非負の数値で、target が未取得なら取り込む</summary>
        private static void TryGetDecimal(JsonElement el, ref decimal target)
        {
            if (target >= 0) return; // 既に取得済み
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v) && v >= 0)
                target = v;
        }

        // ────────────────────────────────────────────────────────────────
        // DOM テキスト解析
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// DOM テキストからクレジット残高・使用量を解析する。
        ///
        /// OpenAI Billing ページのテキスト例（英語 UI）:
        ///   "Credit balance  $5.23"
        ///   "Add to credit balance"
        ///   "Usage this month  $3.47"
        ///   "Auto recharge is on"
        /// </summary>
        private static CodexUsageData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText)) return null;

            var ltext = pageText.ToLowerInvariant();

            // OpenAI の billing ページであることを確認
            if (!ltext.Contains("credit") && !ltext.Contains("balance") && !ltext.Contains("usage"))
                return null;

            // "$X.XX" パターンで金額を抽出する
            decimal balance     = ExtractDollarAmount(pageText, "credit balance");
            decimal monthlyUsage = ExtractDollarAmount(pageText, "usage this month");
            if (monthlyUsage < 0)
                monthlyUsage    = ExtractDollarAmount(pageText, "usage");

            if (balance >= 0 || monthlyUsage >= 0)
            {
                return new CodexUsageData
                {
                    IsConnected     = true,
                    CreditBalance   = balance,
                    HasCreditData   = balance >= 0,
                    MonthlyUsageUsd = monthlyUsage,
                    HasUsageData    = monthlyUsage >= 0
                };
            }

            return null;
        }

        /// <summary>
        /// テキストから keyword 近傍の "$X.XX" を抽出して decimal で返す。
        /// 見つからない場合は -1。
        /// </summary>
        private static decimal ExtractDollarAmount(string text, string keyword)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1m;

            var chunk = text.Substring(idx, Math.Min(150, text.Length - idx));

            // "$5.23" or "5.23 USD"
            var m = Regex.Match(chunk, @"\$\s*([\d,]+\.?\d*)|(\b[\d,]+\.?\d*)\s*USD",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var numStr = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .Replace(",", "");
                if (decimal.TryParse(numStr, out var v))
                    return v;
            }
            return -1m;
        }
    }
}
