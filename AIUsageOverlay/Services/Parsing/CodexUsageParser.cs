using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// Codex / ChatGPT の取得結果（傍受 JSON または DOM テキスト）を解析して
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

            var payload = StripDiagnosticPrefix(raw);
            var trimmed = payload.TrimStart();
            return trimmed.StartsWith('{') || trimmed.StartsWith('[')
                ? ParseFromJson(payload)
                : ParseFromPageText(payload);
        }

        /// <summary>
        /// Scraper が診断用に付与した __URL__ / __SOURCE__ ヘッダーを取り除く。
        /// ヘッダー行の後ろにある実データだけを Parser の対象にする。
        /// </summary>
        private static string StripDiagnosticPrefix(string raw)
        {
            if (!raw.StartsWith("__URL__:") && !raw.StartsWith("__SOURCE__:"))
                return raw;

            var lineBreak = raw.IndexOf('\n');
            return lineBreak >= 0 ? raw[(lineBreak + 1)..] : raw;
        }

        /// <summary>
        /// 傍受した JSON から Codex の 5時間制限・週間制限を解析する。
        /// ChatGPT/Codex 側の内部 JSON はフィールド名が変わりやすいため、
        /// five_hour / seven_day のような明示キーに加え、period/name と usage/limit の組も探索する。
        /// </summary>
        private static CodexUsageData? ParseFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                var lj  = json.ToLowerInvariant();

                // Codex / usage limit 関連でなければスキップする
                if (!lj.Contains("codex") && !lj.Contains("usage")
                    && !lj.Contains("limit") && !lj.Contains("rate")
                    && !lj.Contains("five_hour") && !lj.Contains("seven_day"))
                    return null;

                var acc = new UsageLimitAccumulator();
                FindUsageLimitFields(doc.RootElement, "", acc);
                return acc.ToData();
            }
            catch { return null; }
        }

        /// <summary>
        /// JSON ツリーを再帰探索して 5時間制限・週間制限に該当するオブジェクトを探す。
        /// パス名・プロパティ名・period/name フィールドのいずれかで対象期間を判定する。
        /// </summary>
        private static void FindUsageLimitFields(
            JsonElement el,
            string path,
            UsageLimitAccumulator acc)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                var objectPeriod = DetectPeriodFromObject(el, path);
                if (objectPeriod == UsageLimitPeriod.Session)
                    acc.MergeSession(ReadPeriodValues(el, UsageLimitPeriod.Session));
                else if (objectPeriod == UsageLimitPeriod.Weekly)
                    acc.MergeWeekly(ReadPeriodValues(el, UsageLimitPeriod.Weekly));

                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();
                    var nextPath = $"{path}/{key}";

                    if (IsSessionKey(key))
                        acc.MergeSession(ReadPeriodValues(prop.Value, UsageLimitPeriod.Session));
                    else if (IsWeeklyKey(key))
                        acc.MergeWeekly(ReadPeriodValues(prop.Value, UsageLimitPeriod.Weekly));
                    else if (key == "limits_progress")
                        MergeLimitsProgress(prop.Value, acc);

                    FindUsageLimitFields(prop.Value, nextPath, acc);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    FindUsageLimitFields(item, path, acc);
            }
        }

        /// <summary>
        /// period/name/title などの文字列フィールドから、オブジェクト自体が表す制限期間を判定する。
        /// </summary>
        private static UsageLimitPeriod DetectPeriodFromObject(JsonElement el, string path)
        {
            if (IsSessionKey(path))
                return UsageLimitPeriod.Session;
            if (IsWeeklyKey(path))
                return UsageLimitPeriod.Weekly;

            if (el.ValueKind != JsonValueKind.Object) return UsageLimitPeriod.Unknown;

            foreach (var prop in el.EnumerateObject())
            {
                var key = prop.Name.ToLowerInvariant();
                if (key is not ("period" or "name" or "title" or "label" or "window" or "bucket" or "feature_name"))
                    continue;

                if (prop.Value.ValueKind != JsonValueKind.String)
                    continue;

                var value = prop.Value.GetString() ?? "";
                if (IsSessionKey(value))
                    return UsageLimitPeriod.Session;
                if (IsWeeklyKey(value))
                    return UsageLimitPeriod.Weekly;
            }

            return UsageLimitPeriod.Unknown;
        }

        /// <summary>
        /// ChatGPT の conversation_detail_metadata.limits_progress 配列から Codex 制限を解析する。
        /// Codex は内部 feature 名として odyssey が返ることがあるため、feature_name と reset_after の両方で分類する。
        /// </summary>
        private static void MergeLimitsProgress(JsonElement el, UsageLimitAccumulator acc)
        {
            if (el.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var featureName = GetStringProperty(item, "feature_name") ?? "";
                if (!IsCodexFeatureName(featureName))
                    continue;

                var period = DetectPeriodFromObject(item, "/limits_progress");
                var values = ReadPeriodValues(item, period);
                if (period == UsageLimitPeriod.Unknown)
                    period = InferPeriodFromRemainingMinutes(values.RemainingMinutes);
                if (period == UsageLimitPeriod.Unknown)
                    period = InferPeriodFromReset(values.ResetsAt);

                if (period == UsageLimitPeriod.Session)
                    acc.MergeSession(values);
                else if (period == UsageLimitPeriod.Weekly)
                    acc.MergeWeekly(values);
            }
        }

        /// <summary>limits_progress の feature 名が Codex 系の制限を表しているか判定する。</summary>
        private static bool IsCodexFeatureName(string featureName)
        {
            var normalized = featureName.ToLowerInvariant();
            return normalized.Contains("codex", StringComparison.Ordinal)
                || normalized.Contains("odyssey", StringComparison.Ordinal);
        }

        /// <summary>リセット時刻までの残り時間から 5時間制限か週間制限かを推定する。</summary>
        private static UsageLimitPeriod InferPeriodFromReset(DateTimeOffset? resetsAt)
        {
            if (resetsAt == null)
                return UsageLimitPeriod.Unknown;

            var remaining = resetsAt.Value - DateTimeOffset.Now;
            if (remaining.TotalMinutes <= 0)
                return UsageLimitPeriod.Unknown;

            if (remaining.TotalHours <= 8)
                return UsageLimitPeriod.Session;

            if (remaining.TotalDays <= 8)
                return UsageLimitPeriod.Weekly;

            return UsageLimitPeriod.Unknown;
        }

        /// <summary>リセットまでの残り分数から 5時間制限か週間制限かを推定する。</summary>
        private static UsageLimitPeriod InferPeriodFromRemainingMinutes(int remainingMinutes)
        {
            if (remainingMinutes < 0)
                return UsageLimitPeriod.Unknown;

            if (remainingMinutes <= 8 * 60)
                return UsageLimitPeriod.Session;

            if (remainingMinutes <= 8 * 24 * 60)
                return UsageLimitPeriod.Weekly;

            return UsageLimitPeriod.Unknown;
        }

        /// <summary>JSON オブジェクトから文字列プロパティを取得する。</summary>
        private static string? GetStringProperty(JsonElement el, string propertyName)
        {
            if (el.ValueKind != JsonValueKind.Object)
                return null;

            if (!el.TryGetProperty(propertyName, out var prop))
                return null;

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        }

        /// <summary>
        /// 1つの制限期間オブジェクトから使用率・使用量・上限・リセット時刻を読み取る。
        /// percent が無い場合でも used/limit が取れれば使用率を算出する。
        /// </summary>
        private static UsageLimitValues ReadPeriodValues(
            JsonElement el,
            UsageLimitPeriod targetPeriod = UsageLimitPeriod.Unknown)
        {
            var values = new UsageLimitValues();
            ReadPeriodValuesRecursive(el, "", values, targetPeriod);

            if (values.Percent < 0 && values.RemainingPercent >= 0)
                values.Percent = ClampPercent(100 - values.RemainingPercent);

            if (values.Percent < 0 && values.Used >= 0 && values.Limit > 0)
                values.Percent = ClampPercent((int)Math.Round(values.Used / values.Limit * 100.0));

            if (values.Percent < 0 && values.Remaining >= 0 && values.Limit > 0)
            {
                var used = Math.Max(0, values.Limit - values.Remaining);
                values.Percent = ClampPercent((int)Math.Round(used / values.Limit * 100.0));
            }

            if (values.RemainingMinutes < 0 && values.ResetsAt.HasValue)
            {
                var remaining = values.ResetsAt.Value - DateTimeOffset.Now;
                values.RemainingMinutes = remaining.TotalMinutes > 0
                    ? (int)remaining.TotalMinutes
                    : 0;
            }

            return values;
        }

        /// <summary>
        /// 制限期間オブジェクトを再帰探索して、使用率・使用数・上限・残り時間候補を取り込む。
        /// </summary>
        private static void ReadPeriodValuesRecursive(
            JsonElement el,
            string keyPath,
            UsageLimitValues values,
            UsageLimitPeriod targetPeriod)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name.ToLowerInvariant();
                    var nextPath = $"{keyPath}/{key}";
                    var keyPeriod = DetectPeriodFromKey(key);
                    if (IsOppositePeriod(targetPeriod, keyPeriod))
                        continue;

                    if (TryGetNumber(prop.Value, out var number))
                    {
                        if (IsRemainingPercentKey(key) && values.RemainingPercent < 0)
                            values.RemainingPercent = NormalizePercent(number, key);
                        else if (IsPercentKey(key) && values.Percent < 0)
                            values.Percent = NormalizePercent(number, key);
                        else if (IsUsedKey(key) && values.Used < 0)
                            values.Used = number;
                        else if (IsLimitKey(key) && values.Limit < 0)
                            values.Limit = number;
                        else if (IsRemainingMinutesKey(key) && values.RemainingMinutes < 0)
                            values.RemainingMinutes = (int)Math.Max(0, Math.Round(number));
                        else if (IsRemainingSecondsKey(key) && values.RemainingMinutes < 0)
                            values.RemainingMinutes = (int)Math.Max(0, Math.Round(number / 60.0));
                        else if (IsRemainingCountKey(key) && values.Remaining < 0)
                            values.Remaining = number;
                        else if (TryGetResetDurationMinutes(key, number, out var resetMinutes)
                                 && values.RemainingMinutes < 0)
                            values.RemainingMinutes = resetMinutes;
                    }

                    if (values.ResetsAt == null && IsResetKey(key))
                    {
                        var resetsAt = TryGetDateTimeOffset(prop.Value);
                        if (resetsAt.HasValue)
                            values.ResetsAt = resetsAt;
                    }

                    ReadPeriodValuesRecursive(prop.Value, nextPath, values, targetPeriod);
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                    ReadPeriodValuesRecursive(item, keyPath, values, targetPeriod);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // DOM テキスト解析
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// DOM テキストから Codex の 5時間制限・週間制限を解析する。
        ///
        /// 想定テキスト例:
        ///   "Codex 5h limit 60%"
        ///   "Weekly 18%"
        ///   "5時間 60% 週間 18%"
        /// </summary>
        private static CodexUsageData? ParseFromPageText(string pageText)
        {
            if (string.IsNullOrWhiteSpace(pageText)) return null;

            var ltext = pageText.ToLowerInvariant();

            // Codex / usage limit 関連でなければスキップする
            if (!ltext.Contains("codex") && !ltext.Contains("usage")
                && !ltext.Contains("limit") && !ltext.Contains("5時間")
                && !ltext.Contains("週間"))
                return null;

            var isRemainingUsageText = IsRemainingUsageText(pageText);
            var sessionPercent = ExtractUsagePercentNearKeywords(
                pageText,
                isRemainingUsageText,
                "5-hour",
                "5 hour",
                "five hour",
                "5h",
                "5時間",
                "session");
            var weeklyPercent = ExtractUsagePercentNearKeywords(
                pageText,
                isRemainingUsageText,
                "weekly",
                "week",
                "7-day",
                "7 day",
                "seven day",
                "7d",
                "週間");
            var sessionResetText = ExtractResetTextNearKeywords(
                pageText,
                UsageLimitPeriod.Session,
                "5-hour",
                "5 hour",
                "five hour",
                "5h",
                "5時間",
                "session");
            var weeklyResetText = ExtractResetTextNearKeywords(
                pageText,
                UsageLimitPeriod.Weekly,
                "weekly",
                "week",
                "7-day",
                "7 day",
                "seven day",
                "7d",
                "週間");

            if (sessionPercent >= 0 || weeklyPercent >= 0)
            {
                return new CodexUsageData
                {
                    IsConnected             = true,
                    SessionPercent          = sessionPercent,
                    SessionRemainingMinutes = CalculateRemainingMinutesFromResetText(
                        sessionResetText,
                        UsageLimitPeriod.Session),
                    SessionResetText        = sessionResetText,
                    HasSessionData          = sessionPercent >= 0,
                    WeeklyPercent           = weeklyPercent,
                    WeeklyRemainingMinutes  = CalculateRemainingMinutesFromResetText(
                        weeklyResetText,
                        UsageLimitPeriod.Weekly),
                    WeeklyResetText         = weeklyResetText,
                    HasWeeklyData           = weeklyPercent >= 0
                };
            }

            return null;
        }

        /// <summary>
        /// 指定キーワード近傍の "60%" のような使用率を抽出する。
        /// </summary>
        private static int ExtractUsagePercentNearKeywords(
            string text,
            bool isRemainingUsageText,
            params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                var value = ExtractPercentNearKeyword(text, keyword);
                if (value >= 0)
                    return isRemainingUsageText ? ClampPercent(100 - value) : value;
            }

            return -1;
        }

        /// <summary>
        /// テキストから指定 keyword 近傍の "60%" を抽出する。
        /// </summary>
        private static int ExtractPercentNearKeyword(string text, string keyword)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;

            var start = idx;
            var length = Math.Min(220, text.Length - start);
            var chunk = text.Substring(start, length);

            var m = Regex.Match(chunk, @"(\d{1,3})\s*%", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (int.TryParse(m.Groups[1].Value, out var value))
                    return ClampPercent(value);
            }

            return -1;
        }

        /// <summary>DOM テキストが「残り使用量」を示しているか判定する。</summary>
        private static bool IsRemainingUsageText(string text)
        {
            var normalized = text.ToLowerInvariant();
            return normalized.Contains("残り使用量", StringComparison.Ordinal)
                || normalized.Contains("remaining usage", StringComparison.Ordinal)
                || normalized.Contains("remaining use", StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"\d{1,3}\s*%\s*残り", RegexOptions.IgnoreCase)
                || Regex.IsMatch(normalized, @"\d{1,3}\s*%\s*(remaining|left)", RegexOptions.IgnoreCase);
        }

        /// <summary>指定キーワード近傍からリセット時刻または日付を抽出する。</summary>
        private static string? ExtractResetTextNearKeywords(
            string text,
            UsageLimitPeriod period,
            params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                var value = ExtractResetTextNearKeyword(text, keyword, period);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        /// <summary>指定キーワード以降の短い範囲から Codex Usage パネルのリセット表示を抽出する。</summary>
        private static string? ExtractResetTextNearKeyword(string text, string keyword, UsageLimitPeriod period)
        {
            var idx = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var length = Math.Min(180, text.Length - idx);
            var chunk = text.Substring(idx, length);
            if (period == UsageLimitPeriod.Weekly)
            {
                var date = Regex.Match(
                    chunk,
                    @"\d{4}[/-]\d{1,2}[/-]\d{1,2}(?:\s+\d{1,2}:\d{2})?|\d{1,2}\s*月\s*\d{1,2}\s*日(?:\s+\d{1,2}:\d{2})?|\d{1,2}/\d{1,2}(?:\s+\d{1,2}:\d{2})?",
                    RegexOptions.IgnoreCase);
                if (date.Success)
                    return NormalizeResetText(date.Value);
            }

            var time = Regex.Match(chunk, @"\d{1,2}:\d{2}", RegexOptions.IgnoreCase);
            return time.Success ? NormalizeResetText(time.Value) : null;
        }

        /// <summary>画面表示用のリセット日時テキストから余分な空白を取り除く。</summary>
        private static string NormalizeResetText(string value)
        {
            var withoutJapaneseDateSpaces = Regex.Replace(value.Trim(), @"\s*(月|日)\s*", "$1");
            return Regex.Replace(withoutJapaneseDateSpaces, @"\s+", " ");
        }

        /// <summary>Codex Usage パネルのリセット表示から、リセットまでの残り分数を計算する。</summary>
        private static int CalculateRemainingMinutesFromResetText(string? resetText, UsageLimitPeriod period)
        {
            if (string.IsNullOrWhiteSpace(resetText))
                return -1;

            var resetAt = TryParseResetText(resetText, period);
            if (!resetAt.HasValue)
                return -1;

            var remaining = resetAt.Value - DateTime.Now;
            return remaining.TotalMinutes > 0
                ? (int)Math.Ceiling(remaining.TotalMinutes)
                : 0;
        }

        /// <summary>時刻のみ、月日、日/月のリセット表示をローカル日時へ変換する。</summary>
        private static DateTime? TryParseResetText(string resetText, UsageLimitPeriod period)
        {
            var normalized = NormalizeResetText(resetText);
            var now = DateTime.Now;

            var yearDate = Regex.Match(
                normalized,
                @"(?<year>\d{4})[/-](?<month>\d{1,2})[/-](?<day>\d{1,2})(?:\s*(?<hour>\d{1,2}):(?<minute>\d{2}))?");
            if (yearDate.Success
                && TryBuildYearResetDate(yearDate, out var parsedYearDate))
                return parsedYearDate;

            var japaneseDate = Regex.Match(
                normalized,
                @"(?<month>\d{1,2})月(?<day>\d{1,2})日(?:\s*(?<hour>\d{1,2}):(?<minute>\d{2}))?");
            if (japaneseDate.Success
                && TryBuildResetDate(now, japaneseDate, monthFirst: true, out var parsedJapaneseDate))
                return parsedJapaneseDate;

            var slashDate = Regex.Match(
                normalized,
                @"(?<first>\d{1,2})/(?<second>\d{1,2})(?:\s*(?<hour>\d{1,2}):(?<minute>\d{2}))?");
            if (slashDate.Success
                && TryBuildSlashResetDate(now, slashDate, out var parsedSlashDate))
                return parsedSlashDate;

            var timeOnly = Regex.Match(normalized, @"(?<hour>\d{1,2}):(?<minute>\d{2})");
            if (!timeOnly.Success)
                return null;

            if (!int.TryParse(timeOnly.Groups["hour"].Value, out var hour)
                || !int.TryParse(timeOnly.Groups["minute"].Value, out var minute))
                return null;

            var resetAt = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
            if (period == UsageLimitPeriod.Session && resetAt <= now)
                resetAt = resetAt.AddDays(1);

            return resetAt;
        }

        /// <summary>年付きスラッシュ区切りの日付を DateTime に変換する。</summary>
        private static bool TryBuildYearResetDate(Match match, out DateTime resetAt)
        {
            resetAt = default;
            if (!int.TryParse(match.Groups["year"].Value, out var year)
                || !int.TryParse(match.Groups["month"].Value, out var month)
                || !int.TryParse(match.Groups["day"].Value, out var day))
                return false;

            var hour = TryReadIntGroup(match, "hour") ?? 0;
            var minute = TryReadIntGroup(match, "minute") ?? 0;
            if (!IsValidDateTimePart(year, month, day, hour, minute))
                return false;

            resetAt = new DateTime(year, month, day, hour, minute, 0);
            return true;
        }

        /// <summary>日本語表記の月日リセット表示を DateTime に変換する。</summary>
        private static bool TryBuildResetDate(
            DateTime now,
            Match match,
            bool monthFirst,
            out DateTime resetAt)
        {
            resetAt = default;
            var monthGroup = monthFirst ? "month" : "second";
            var dayGroup = monthFirst ? "day" : "first";
            if (!int.TryParse(match.Groups[monthGroup].Value, out var month)
                || !int.TryParse(match.Groups[dayGroup].Value, out var day))
                return false;

            var hour = TryReadIntGroup(match, "hour") ?? 0;
            var minute = TryReadIntGroup(match, "minute") ?? 0;
            if (!IsValidDateTimePart(now.Year, month, day, hour, minute))
                return false;

            resetAt = new DateTime(now.Year, month, day, hour, minute, 0);
            if (resetAt <= now)
                resetAt = resetAt.AddYears(1);

            return true;
        }

        /// <summary>スラッシュ区切りの日付を、Codex の日/月表記を優先して DateTime に変換する。</summary>
        private static bool TryBuildSlashResetDate(DateTime now, Match match, out DateTime resetAt)
        {
            resetAt = default;
            if (!int.TryParse(match.Groups["first"].Value, out var first)
                || !int.TryParse(match.Groups["second"].Value, out var second))
                return false;

            var day = first;
            var month = second;
            if (first <= 12 && second > 12)
            {
                month = first;
                day = second;
            }

            var hour = TryReadIntGroup(match, "hour") ?? 0;
            var minute = TryReadIntGroup(match, "minute") ?? 0;
            if (!IsValidDateTimePart(now.Year, month, day, hour, minute))
                return false;

            resetAt = new DateTime(now.Year, month, day, hour, minute, 0);
            if (resetAt <= now)
                resetAt = resetAt.AddYears(1);

            return true;
        }

        /// <summary>正規表現グループから int を読み取る。未取得なら null。</summary>
        private static int? TryReadIntGroup(Match match, string groupName)
        {
            var group = match.Groups[groupName];
            if (!group.Success)
                return null;

            return int.TryParse(group.Value, out var value) ? value : null;
        }

        /// <summary>DateTime 構築前に年月日・時分の範囲を検証する。</summary>
        private static bool IsValidDateTimePart(int year, int month, int day, int hour, int minute)
            => year is >= 1 and <= 9999
            && month is >= 1 and <= 12
            && day is >= 1 and <= 31
            && day <= DateTime.DaysInMonth(year, month)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59;

        /// <summary>キー名が 5時間制限を表すか判定する。</summary>
        private static bool IsSessionKey(string key)
        {
            var normalized = key.ToLowerInvariant()
                .Replace("-", "_", StringComparison.Ordinal)
                .Replace(" ", "_", StringComparison.Ordinal);

            return normalized.Contains("five_hour")
                || normalized.Contains("5_hour")
                || normalized.Contains("5h")
                || normalized.Contains("5時間")
                || normalized.Contains("session");
        }

        /// <summary>キー名が週間制限を表すか判定する。</summary>
        private static bool IsWeeklyKey(string key)
        {
            var normalized = key.ToLowerInvariant()
                .Replace("-", "_", StringComparison.Ordinal)
                .Replace(" ", "_", StringComparison.Ordinal);

            return normalized.Contains("seven_day")
                || normalized.Contains("7_day")
                || normalized.Contains("7d")
                || normalized.Contains("weekly")
                || normalized.Contains("week")
                || normalized.Contains("週間");
        }

        /// <summary>キー名から 5時間・週間の制限期間種別を判定する。</summary>
        private static UsageLimitPeriod DetectPeriodFromKey(string key)
        {
            if (IsSessionKey(key))
                return UsageLimitPeriod.Session;
            if (IsWeeklyKey(key))
                return UsageLimitPeriod.Weekly;
            return UsageLimitPeriod.Unknown;
        }

        /// <summary>対象期間と反対側のサブツリーか判定する。</summary>
        private static bool IsOppositePeriod(UsageLimitPeriod targetPeriod, UsageLimitPeriod keyPeriod)
            => targetPeriod != UsageLimitPeriod.Unknown
            && keyPeriod != UsageLimitPeriod.Unknown
            && targetPeriod != keyPeriod;

        /// <summary>キー名が使用率を表すか判定する。</summary>
        private static bool IsPercentKey(string key)
            => key.Contains("utilization")
            || key.Contains("percent")
            || key.Contains("percentage")
            || key.Contains("ratio")
            || key.Contains("fraction");

        /// <summary>キー名が残り率を表すか判定する。</summary>
        private static bool IsRemainingPercentKey(string key)
            => key.Contains("remaining")
            && (key.Contains("utilization")
                || key.Contains("percent")
                || key.Contains("percentage")
                || key.Contains("ratio")
                || key.Contains("fraction"));

        /// <summary>キー名が使用済み量を表すか判定する。</summary>
        private static bool IsUsedKey(string key)
            => key is "used" or "used_count" or "usage" or "used_amount"
            || key.Contains("used_messages")
            || key.Contains("messages_used");

        /// <summary>キー名が上限量を表すか判定する。</summary>
        private static bool IsLimitKey(string key)
            => key is "limit" or "cap" or "max" or "total"
            || key.Contains("limit_count")
            || key.Contains("message_limit")
            || key.Contains("messages_limit");

        /// <summary>キー名が残り分数を表すか判定する。</summary>
        private static bool IsRemainingMinutesKey(string key)
            => key.Contains("remaining") && key.Contains("minute");

        /// <summary>キー名が残り秒数を表すか判定する。</summary>
        private static bool IsRemainingSecondsKey(string key)
            => key.Contains("remaining") && key.Contains("second");

        /// <summary>キー名が残り利用回数を表すか判定する。</summary>
        private static bool IsRemainingCountKey(string key)
            => (key is "remaining" or "remaining_count" or "remaining_messages")
            && !key.Contains("minute")
            && !key.Contains("second")
            && !key.Contains("time");

        /// <summary>キー名がリセット日時を表すか判定する。</summary>
        private static bool IsResetKey(string key)
            => key.Contains("reset") || key.Contains("resets_at") || key.Contains("reset_at");

        /// <summary>
        /// reset_after / resets_in_seconds のような数値のリセット待ち時間を分に変換する。
        /// Unix 時刻の reset_at と混同しないよう、duration を示すキーだけを対象にする。
        /// </summary>
        private static bool TryGetResetDurationMinutes(string key, double value, out int minutes)
        {
            minutes = -1;
            if (value < 0)
                return false;

            var normalized = key.ToLowerInvariant()
                .Replace("-", "_", StringComparison.Ordinal)
                .Replace(" ", "_", StringComparison.Ordinal);
            var isResetDurationKey =
                normalized.Contains("reset", StringComparison.Ordinal)
                || normalized.Contains("expire", StringComparison.Ordinal)
                || normalized.Contains("ttl", StringComparison.Ordinal);
            if (!isResetDurationKey)
                return false;

            double totalMinutes;
            if (normalized.Contains("millisecond", StringComparison.Ordinal)
                || normalized.EndsWith("_ms", StringComparison.Ordinal)
                || normalized.Contains("_ms_", StringComparison.Ordinal))
            {
                totalMinutes = value / 60_000.0;
            }
            else if (normalized.Contains("minute", StringComparison.Ordinal))
            {
                totalMinutes = value;
            }
            else if (normalized.Contains("hour", StringComparison.Ordinal))
            {
                totalMinutes = value * 60.0;
            }
            else if (normalized.Contains("second", StringComparison.Ordinal)
                     || normalized.Contains("_in", StringComparison.Ordinal)
                     || normalized.Contains("after", StringComparison.Ordinal)
                     || normalized.Contains("ttl", StringComparison.Ordinal))
            {
                totalMinutes = value / 60.0;
            }
            else
            {
                return false;
            }

            minutes = (int)Math.Max(0, Math.Round(totalMinutes));
            return true;
        }

        /// <summary>JSON 値から double を読み取る。</summary>
        private static bool TryGetNumber(JsonElement el, out double value)
        {
            value = -1;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var number))
            {
                value = number;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String
                && double.TryParse(el.GetString(), out var stringNumber))
            {
                value = stringNumber;
                return true;
            }

            return false;
        }

        /// <summary>JSON 値から DateTimeOffset を読み取る。</summary>
        private static DateTimeOffset? TryGetDateTimeOffset(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(el.GetString(), out var parsed))
                return parsed;

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var unix))
            {
                try
                {
                    return unix > 10_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                        : DateTimeOffset.FromUnixTimeSeconds(unix);
                }
                catch { return null; }
            }

            return null;
        }

        /// <summary>
        /// 0〜1 の ratio と 0〜100 の percent の両方を 0〜100 の整数に正規化する。
        /// </summary>
        private static int NormalizePercent(double value, string key)
        {
            if (value < 0) return -1;
            var normalized = value <= 1.0 || key.Contains("ratio") || key.Contains("fraction")
                ? value * 100.0
                : value;
            return ClampPercent((int)Math.Round(normalized));
        }

        /// <summary>使用率を 0〜100 に丸める。</summary>
        private static int ClampPercent(int value)
            => Math.Max(0, Math.Min(100, value));

        /// <summary>解析対象の制限期間種別。</summary>
        private enum UsageLimitPeriod
        {
            Unknown,
            Session,
            Weekly
        }

        /// <summary>1つの制限期間から読み取った中間値。</summary>
        private sealed class UsageLimitValues
        {
            public int Percent { get; set; } = -1;
            public int RemainingPercent { get; set; } = -1;
            public double Used { get; set; } = -1;
            public double Limit { get; set; } = -1;
            public double Remaining { get; set; } = -1;
            public int RemainingMinutes { get; set; } = -1;
            public DateTimeOffset? ResetsAt { get; set; }
        }

        /// <summary>5時間・週間の解析結果を蓄積し、CodexUsageData へ変換する。</summary>
        private sealed class UsageLimitAccumulator
        {
            private UsageLimitValues _session = new();
            private UsageLimitValues _weekly = new();

            public void MergeSession(UsageLimitValues values)
                => _session = Merge(_session, values);

            public void MergeWeekly(UsageLimitValues values)
                => _weekly = Merge(_weekly, values);

            public CodexUsageData? ToData()
            {
                var hasSession = _session.Percent >= 0;
                var hasWeekly = _weekly.Percent >= 0;
                if (!hasSession && !hasWeekly)
                    return null;

                return new CodexUsageData
                {
                    IsConnected             = true,
                    SessionPercent          = _session.Percent,
                    SessionRemainingMinutes = _session.RemainingMinutes,
                    SessionResetText        = BuildResetText(_session, UsageLimitPeriod.Session),
                    HasSessionData          = hasSession,
                    WeeklyPercent           = _weekly.Percent,
                    WeeklyRemainingMinutes  = _weekly.RemainingMinutes,
                    WeeklyResetText         = BuildResetText(_weekly, UsageLimitPeriod.Weekly),
                    HasWeeklyData           = hasWeekly
                };
            }

            /// <summary>絶対リセット時刻または残り分数から画面表示用のリセット日時を生成する。</summary>
            private static string? BuildResetText(UsageLimitValues values, UsageLimitPeriod period)
            {
                DateTime resetLocalTime;
                if (values.ResetsAt.HasValue)
                {
                    resetLocalTime = values.ResetsAt.Value.LocalDateTime;
                }
                else if (values.RemainingMinutes >= 0)
                {
                    resetLocalTime = DateTime.Now.AddMinutes(values.RemainingMinutes);
                }
                else
                {
                    return null;
                }

                return period == UsageLimitPeriod.Session
                    ? resetLocalTime.ToString("HH:mm")
                    : resetLocalTime.ToString("M月d日 HH:mm");
            }

            private static UsageLimitValues Merge(UsageLimitValues current, UsageLimitValues next)
            {
                if (current.Percent < 0 && next.Percent >= 0)
                    current.Percent = next.Percent;
                if (current.RemainingPercent < 0 && next.RemainingPercent >= 0)
                    current.RemainingPercent = next.RemainingPercent;
                if (current.RemainingMinutes < 0 && next.RemainingMinutes >= 0)
                    current.RemainingMinutes = next.RemainingMinutes;
                if (current.Used < 0 && next.Used >= 0)
                    current.Used = next.Used;
                if (current.Limit < 0 && next.Limit >= 0)
                    current.Limit = next.Limit;
                if (current.Remaining < 0 && next.Remaining >= 0)
                    current.Remaining = next.Remaining;
                if (current.ResetsAt == null && next.ResetsAt.HasValue)
                    current.ResetsAt = next.ResetsAt;
                return current;
            }
        }
    }
}
