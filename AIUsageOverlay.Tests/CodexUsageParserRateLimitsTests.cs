using AIUsageOverlay.Services;
using AIUsageOverlay.Services.Parsing;

namespace AIUsageOverlay.Tests;

/// <summary>
/// Codex rate_limits API の primary / secondary 形式を検証する。
/// </summary>
public class CodexUsageParserRateLimitsTests
{
    /// <summary>
    /// window_minutes を使って primary を5時間枠、secondary を週間枠へ割り当て、
    /// percent 単位の 1 を 100% に拡大せずリセット日時も保持することを確認する。
    /// </summary>
    [Fact]
    public void RateLimits形式を5時間枠と週間枠へ変換する()
    {
        const long sessionReset = 1_893_456_000;
        const long weeklyReset = 1_893_888_000;
        const string json = """
            {
              "rate_limits": {
                "limit_id": "codex",
                "primary": {
                  "used_percent": 1,
                  "window_minutes": 300,
                  "resets_at": 1893456000
                },
                "secondary": {
                  "used_percent": 47.5,
                  "window_minutes": 10080,
                  "resets_at": 1893888000
                }
              }
            }
            """;

        var result = CodexUsageParser.Parse(json);

        Assert.NotNull(result);
        Assert.True(result.IsConnected);
        Assert.True(result.HasSessionData);
        Assert.Equal(1, result.SessionPercent);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(sessionReset).LocalDateTime.ToString("HH:mm"),
            result.SessionResetText);
        Assert.True(result.HasWeeklyData);
        Assert.Equal(48, result.WeeklyPercent);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(weeklyReset).LocalDateTime.ToString("M月d日 HH:mm"),
            result.WeeklyResetText);
    }

    /// <summary>
    /// used_percent 以外の既存形式では、1 以下の値を引き続き ratio として変換することを確認する。
    /// </summary>
    [Fact]
    public void 既存形式のRatio値を百分率へ変換する()
    {
        const string json = """
            {
              "usage": {
                "five_hour": { "percentage": 1 },
                "weekly": { "utilization": 0.5 }
              }
            }
            """;

        var result = CodexUsageParser.Parse(json);

        Assert.NotNull(result);
        Assert.Equal(100, result.SessionPercent);
        Assert.Equal(50, result.WeeklyPercent);
    }

    /// <summary>
    /// 現行 wham usage API の秒単位ウィンドウを5時間枠・週間枠へ割り当てる。
    /// reset_after_seconds をUnix時刻と誤認せず、reset_at を表示に使うことも確認する。
    /// </summary>
    [Fact]
    public void WhamUsage形式を5時間枠と週間枠へ変換する()
    {
        const long sessionReset = 1_893_456_000;
        const long weeklyReset = 1_893_888_000;
        const string json = """
            __URL__:/backend-api/wham/usage|status=200
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 12.4,
                  "limit_window_seconds": 18000,
                  "reset_after_seconds": 3600,
                  "reset_at": 1893456000
                },
                "secondary_window": {
                  "used_percent": 67.8,
                  "limit_window_seconds": 604800,
                  "reset_after_seconds": 172800,
                  "reset_at": 1893888000
                }
              }
            }
            """;

        var result = CodexUsageParser.Parse(json);

        Assert.NotNull(result);
        Assert.True(result.HasSessionData);
        Assert.Equal(12, result.SessionPercent);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(sessionReset).LocalDateTime.ToString("HH:mm"),
            result.SessionResetText);
        Assert.True(result.HasWeeklyData);
        Assert.Equal(68, result.WeeklyPercent);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(weeklyReset).LocalDateTime.ToString("M月d日 HH:mm"),
            result.WeeklyResetText);
    }

    /// <summary>同じ時間幅を含む非Codex候補より、取得元を確認できるwham usageを優先する。</summary>
    [Fact]
    public void WhamUsage候補を他のUsage候補より優先する()
    {
        const string unrelated = """
            { "usage": { "five_hour": { "percentage": 99 }, "weekly": { "percentage": 98 } } }
            """;
        const string wham = """
            __URL__:/backend-api/wham/usage|status=200
            {
              "rate_limit": {
                "primary_window": { "used_percent": 21, "limit_window_seconds": 18000 },
                "secondary_window": { "used_percent": 34, "limit_window_seconds": 604800 }
              }
            }
            """;

        var result = CodexWebScraper.ParseUsageCandidates([unrelated, wham]);

        Assert.NotNull(result);
        Assert.Equal(21, result.SessionPercent);
        Assert.Equal(34, result.WeeklyPercent);
    }

    /// <summary>取得元を確認できないwham同型JSONをCodex使用量として誤採用しない。</summary>
    [Fact]
    public void 取得元なしのWhamUsage同型JSONを拒否する()
    {
        const string json = """
            {
              "rate_limit": {
                "primary_window": { "used_percent": 21, "limit_window_seconds": 18000 },
                "secondary_window": { "used_percent": 34, "limit_window_seconds": 604800 }
              }
            }
            """;

        Assert.Null(CodexUsageParser.Parse(json));
    }

    /// <summary>非Wham候補の先着では収集を終了せず、Wham到着後だけ終了可能と判定する。</summary>
    [Fact]
    public void Wham候補の到着前に収集を打ち切らない()
    {
        const string unrelated = """
            { "usage": { "five_hour": { "percentage": 99 }, "weekly": { "percentage": 98 } } }
            """;
        const string wham = """
            __URL__:/backend-api/wham/usage|status=200
            {
              "rate_limit": {
                "primary_window": { "used_percent": 21, "limit_window_seconds": 18000 },
                "secondary_window": { "used_percent": 34, "limit_window_seconds": 604800 }
              }
            }
            """;

        Assert.False(CodexWebScraper.ContainsParseableWhamUsageData([unrelated]));
        Assert.True(CodexWebScraper.ContainsParseableWhamUsageData([unrelated, wham]));
    }
}
