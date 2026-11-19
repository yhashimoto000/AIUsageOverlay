using AIUsageOverlay.Models;
using AIUsageOverlay.Services.SessionStats;

namespace AIUsageOverlay.Tests;

/// <summary>
/// 効果・稼働タブ（ImpactView）の集計を検証する。
///
/// 走査は通さず、組み立て済みの SessionStatsRecord から Build を呼ぶ。
/// 稼働時間・定着率・費用換算はいずれも「何を母数に置くか」で値が変わるため、
/// 境界（重複する枠、土日、単価表に無いモデル）を固定データで押さえる。
/// </summary>
public class ImpactTests
{
    /// <summary>集計の基準時刻。2026-11-24 は火曜日。</summary>
    private static readonly DateTime NowLocal = new(2026, 11, 24, 10, 0, 0, DateTimeKind.Local);

    /// <summary>テストで使う前提値。</summary>
    private static readonly ReportAssumptions Assumptions = new(5000, 2.0, 150.0);

    /// <summary>1 時間あたりの枠数（12 枠 = 5 分 × 12）。</summary>
    private const int SlotsPerHour = 12;

    /// <summary>
    /// 検証用のレポートを作る。
    ///
    /// 稼働枠は次のように置く（枠番号 = 時 × 12 + 分 / 5）。
    ///   2026-11-16 (月) S1 が 9:00 と 9:05、S2 が 9:05 と 9:10 → 重複を除いて 3 枠
    ///   2026-11-18 (水) 9:00 の 1 枠
    ///   2026-11-21 (土) 8:20 と 8:25 の 2 枠（営業日ではない）
    ///   2026-11-23 (月) 14:00 から 14:25 までの 6 枠
    /// </summary>
    /// <returns>集計済みのレポート</returns>
    private static UsageReport BuildReport()
    {
        var scan = new ScanResult();

        scan.Sessions.Add(NewSession(
            key: "claude:s1",
            tool: SessionStatsRecord.ToolClaudeCode,
            isSubagent: false,
            startUtc: new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc),
            slots: new() { ["2026-11-16"] = new() { 108, 109 } },
            tokens: new TokenTotals { Input = 1000, Output = 2000, CacheCreate = 500 },
            model: "claude-opus-5"));

        scan.Sessions.Add(NewSession(
            key: "claude:s2",
            tool: SessionStatsRecord.ToolClaudeCode,
            isSubagent: true,
            startUtc: new DateTime(2026, 11, 16, 1, 5, 0, DateTimeKind.Utc),
            slots: new() { ["2026-11-16"] = new() { 109, 110 } },
            tokens: new TokenTotals { Input = 100, Output = 200 },
            model: "claude-sonnet-5"));

        scan.Sessions.Add(NewSession(
            key: "codex:s3",
            tool: SessionStatsRecord.ToolCodex,
            isSubagent: false,
            startUtc: new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc),
            slots: new()
            {
                ["2026-11-18"] = new() { 108 },
                ["2026-11-21"] = new() { 100, 101 },
                ["2026-11-23"] = new() { 168, 169, 170, 171, 172, 173 },
            },
            tokens: new TokenTotals { Input = 1000, CacheRead = 400, Output = 500 },
            model: "gpt-5.6-sol"));

        return SessionStatsAggregator.Build(
            scan, includePrompt: false, appVersion: "test",
            cacheWasCorrupted: false, nowLocal: NowLocal, assumptions: Assumptions);
    }

    /// <summary>検証用のセッションを 1 件作る。</summary>
    private static SessionStatsRecord NewSession(
        string key,
        string tool,
        bool isSubagent,
        DateTime startUtc,
        Dictionary<string, List<int>> slots,
        TokenTotals tokens,
        string model)
        => new()
        {
            Key = key,
            Tool = tool,
            SessionId = key,
            IsSubagent = isSubagent,
            ProjectKey = "C:\\TEST",
            ProjectDisplay = "C:\\test",
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(10),
            ElapsedSec = 600,
            ElapsedMeasurable = true,
            Turns = 1,
            Tokens = tokens,
            ActiveSlots = slots,
            ByModel = new List<ModelBreakdown>
            {
                new() { Model = model, Turns = 1, Tokens = tokens },
            },
            ByDay = new List<DayBreakdown>
            {
                new()
                {
                    DateLocal = startUtc.ToLocalTime().ToString("yyyy-MM-dd"),
                    Turns = 1,
                    Tokens = tokens,
                },
            },
        };

    /// <summary>全期間の稼働・効果データ。期間ごとに持つので "all" から取る。</summary>
    private static PeriodImpactView AllImpact() => BuildReport().Periods["all"].Impact;

    [Fact]
    public void 同じ枠に複数のセッションが居ても稼働時間は二重計上されない()
    {
        var impact = AllImpact();

        // 11-16 は 2 セッションで延べ 4 枠だが、重複を除くと 3 枠 = 15 分
        var day = impact.ActiveByDay.Single(d => d.Date == "2026-11-16");
        Assert.Equal(15, day.Minutes);

        // 3 + 1 + 2 + 6 = 12 枠 = 60 分
        Assert.Equal(60, impact.TotalActiveMinutes);
    }

    [Fact]
    public void 時間帯別の稼働率は稼働した平日だけを母数にする()
    {
        var impact = AllImpact();

        // 平日で稼働したのは 11-16 / 11-18 / 11-23 の 3 日。11-21 は土曜なので母数に入らない
        Assert.Equal(3, impact.HourlyWeekdayCount);

        // 9 時台の枠は 11-16 が 3 つ、11-18 が 1 つ。3 日 × 12 枠が母数
        var nine = impact.Hourly.Single(h => h.Hour == 9);
        Assert.Equal(4, nine.ActiveSlots);
        Assert.Equal(Math.Round(4d / (3 * SlotsPerHour) * 100d, 1), nine.Rate);

        // 14 時台は 11-23 の 6 枠だけ
        var fourteen = impact.Hourly.Single(h => h.Hour == 14);
        Assert.Equal(6, fourteen.ActiveSlots);
        Assert.Equal(Math.Round(6d / (3 * SlotsPerHour) * 100d, 1), fourteen.Rate);

        // 土曜だけに記録がある 8 時台は、母数から外れているので 0 になる
        Assert.Equal(0d, impact.Hourly.Single(h => h.Hour == 8).Rate);
    }

    [Fact]
    public void 定着率は営業日を母数にし土日は連続の切れ目にしない()
    {
        var adoption = AllImpact().Adoption;

        Assert.Equal("2026-11-16", adoption.FirstDay);
        Assert.Equal("2026-11-23", adoption.LastDay);

        // 11-16 から 11-23 までの営業日は 16/17/18/19/20/23 の 6 日
        Assert.Equal(6, adoption.BusinessDays);

        // そのうち稼働したのは 16 / 18 / 23 の 3 日
        Assert.Equal(3, adoption.UsedBusinessDays);

        // 土曜の 11-21 も「稼働した日」には数える
        Assert.Equal(4, adoption.ActiveDays);

        // 営業日が 1 日おきなので連続は 1 日。土日は切れ目にしないが、11-20（金）が空いている
        Assert.Equal(1, adoption.BestStreak);
        Assert.Equal(1, adoption.CurrentStreak);
    }

    [Fact]
    public void サブエージェントの内訳は単独値で集計される()
    {
        var subagent = AllImpact().Subagent;

        Assert.Equal(2, subagent.Parents);
        Assert.Equal(1, subagent.Subagents);

        // 親: Claude 3,500（1000 + 500 + 2000）+ Codex 1,100（1000 - 400 + 500）
        Assert.Equal(4600, subagent.ParentBillable);

        // サブ: 100 + 200
        Assert.Equal(300, subagent.SubagentBillable);
    }

    [Fact]
    public void 単価表にあるモデルだけが費用換算の対象になる()
    {
        var modelCost = AllImpact().ModelCost;

        var opus = modelCost.Single(m => m.Model == "claude-opus-5");
        // 入力 1000 × $5 + 出力 2000 × $25 + TTL 不明のキャッシュ書込 500 × $5 × 1.25
        Assert.Equal(0.058125m, opus.Usd);

        var sonnet = modelCost.Single(m => m.Model == "claude-sonnet-5");
        Assert.True(sonnet.Usd > 0m);

        // Codex は単価表と対応しないため換算しない。量は出す
        var codex = modelCost.Single(m => m.Model == "gpt-5.6-sol");
        Assert.Equal(0m, codex.Usd);
        Assert.Equal(1100, codex.Billable);
    }

    [Fact]
    public void 金額計算の前提値がそのままレポートへ載る()
    {
        var assumptions = BuildReport().Impact.Assumptions;

        Assert.Equal(5000, assumptions.HourlyRateJpy);
        Assert.Equal(2.0, assumptions.ReductionFactor);
        Assert.Equal(150.0, assumptions.UsdJpyRate);
        Assert.Equal(SessionStatsRecord.ActiveSlotMinutes, assumptions.SlotMinutes);
        Assert.Equal(ModelPricing.CoefficientReferenceDate, assumptions.CoefficientReferenceDate);
    }

    [Fact]
    public void 期間を絞ると稼働時間も切り替わる()
    {
        var report = BuildReport();

        // 全期間は 12 枠 = 60 分
        Assert.Equal(60, report.Periods["all"].Impact.TotalActiveMinutes);

        // 直近 30 日（2026-10-26 以降）には検証データがすべて入る
        Assert.Equal(60, report.Periods["30"].Impact.TotalActiveMinutes);

        // 「今日」は 2026-11-24。枠が無いので 0 になり、定着率も空になる
        var today = report.Periods["today"].Impact;
        Assert.Equal(0, today.TotalActiveMinutes);
        Assert.Equal(0, today.HourlyWeekdayCount);
        Assert.Equal(string.Empty, today.Adoption.FirstDay);
    }

    [Fact]
    public void 月次の期間は従量課金だった場合の費用を持つ()
    {
        var report = BuildReport();

        // 11 月の期間に Claude 分の費用が載る（Codex は換算対象外なので 0 のまま）
        var november = report.Periods["month:2026-11"];
        Assert.True(november.Cost.UsdAmount > 0m);

        // モデル別は期間側にだけ持たせる（セッション明細に同じ表を何度も載せない）
        Assert.NotEmpty(november.Cost.UsdByModel);
        Assert.Empty(report.Sessions[0].Cost.UsdByModel);
    }
}
