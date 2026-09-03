using System.IO;
using AIUsageOverlay.Models;
using AIUsageOverlay.Services.SessionStats;

namespace AIUsageOverlay.Tests;

/// <summary>
/// 単価で重み付けしたコスト換算の検証。
/// 実履歴は業務情報を含むため使わず、TestData の合成 JSONL と手計算で検証する。
/// </summary>
public class CostTests
{
    /// <summary>合成 JSONL の置き場所。</summary>
    private static string DataPath(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>Claude Code の 1 ファイルを読み、レコードへ畳む。</summary>
    private static SessionStatsRecord ParseClaude(string name)
        => SessionStatsScanner.ParseClaudeFile(DataPath(name), includePrompt: true)
           ?? throw new InvalidOperationException($"レコードが作られなかった: {name}");

    /// <summary>
    /// usage.cache_creation の入れ子から TTL 別の内訳を読む。
    /// 合計（cache_creation_input_tokens）は内訳の再掲であり、二重加算しない。
    /// </summary>
    [Fact]
    public void C1_キャッシュ書き込みのTTL別内訳を読み取る()
    {
        var record = ParseClaude("claude_cache_ttl.jsonl");
        var t = record.Tokens;

        // 2000（内訳あり）+ 500（内訳なし）+ 0 = 2500
        Assert.Equal(2500, t.CacheCreate);
        Assert.Equal(800, t.CacheCreate5m);
        Assert.Equal(1200, t.CacheCreate1h);

        // 内訳の合計が合計値を超えない（超えるとコスト換算が破綻する）
        Assert.True(t.CacheCreate5m + t.CacheCreate1h <= t.CacheCreate);
    }

    /// <summary>
    /// cache_creation の入れ子が無い古い履歴では TTL 別が 0 のままになり、
    /// 合計との差が「TTL 不明分」として残る。
    /// </summary>
    [Fact]
    public void C2_TTL内訳が無い行は不明分として残る()
    {
        var record = ParseClaude("claude_cache_ttl.jsonl");
        var t = record.Tokens;

        var unknownTtl = t.CacheCreate - t.CacheCreate5m - t.CacheCreate1h;
        Assert.Equal(500, unknownTtl);
    }

    /// <summary>
    /// 単価表は接頭辞で引く。長い接頭辞を優先するため、
    /// claude-fable-5-1 が claude-fable-5 に吸われない。日付サフィックス付きにも当たる。
    /// </summary>
    [Theory]
    [InlineData("claude-opus-5", 5.0, 25.0)]
    [InlineData("claude-sonnet-5", 2.0, 10.0)]
    [InlineData("claude-sonnet-4-6", 3.0, 15.0)]
    [InlineData("claude-haiku-4-5-20251001", 1.0, 5.0)]
    [InlineData("claude-fable-5", 10.0, 50.0)]
    [InlineData("claude-fable-5-1", 10.0, 50.0)]
    public void C3_モデル名から単価を引ける(string model, double input, double output)
    {
        var price = ModelPricing.Lookup(model);

        Assert.NotNull(price);
        Assert.Equal((decimal)input, price!.InputPerMillion);
        Assert.Equal((decimal)output, price.OutputPerMillion);
    }

    /// <summary>
    /// Claude Fable 5.1 だけキャッシュ読込が 0.025 倍で、他は 0.1 倍。
    /// 取り違えると読込のコストを 4 倍に見積もる。
    /// </summary>
    [Fact]
    public void C4_Fableのみキャッシュ読込単価が異なる()
    {
        Assert.Equal(0.025m, ModelPricing.Lookup("claude-fable-5-1")!.CacheReadRatio);
        Assert.Equal(0.10m, ModelPricing.Lookup("claude-fable-5")!.CacheReadRatio);
        Assert.Equal(0.10m, ModelPricing.Lookup("claude-opus-5")!.CacheReadRatio);
    }

    /// <summary>
    /// 表に無いモデルは null を返す。既定値へフォールバックしない
    /// （静かに誤った構成比を出すより、除外して明示するほうが正しい）。
    /// </summary>
    [Theory]
    [InlineData("claude-unlisted-9")]
    [InlineData("claude-mythos-5-1")]
    [InlineData("gpt-5.6-sol")]
    [InlineData("sonnet")]
    [InlineData("")]
    [InlineData(null)]
    public void C5_単価表に無いモデルはnullを返す(string? model)
        => Assert.Null(ModelPricing.Lookup(model));

    /// <summary>
    /// 構成比が手計算と一致する。
    ///
    /// claude-opus-5（入力 $5 / 出力 $25 / 1M）で
    /// input 1000・write5m 800・write1h 1200・read 50000・output 500 のとき、
    /// 相対コストは 0.005 / 0.005 / 0.012 / 0.025 / 0.0125（合計 0.0595）。
    /// </summary>
    [Fact]
    public void C6_単価で重み付けした構成比を計算する()
    {
        var acc = new CostAccumulator();
        acc.Add(SessionStatsRecord.ToolClaudeCode, "claude-opus-5", new TokenTotals
        {
            Input = 1000,
            Output = 500,
            CacheCreate = 2000,
            CacheCreate5m = 800,
            CacheCreate1h = 1200,
            CacheRead = 50000,
        });

        var view = acc.Build();

        Assert.True(view.Available);
        Assert.Equal(8.4, view.InputPercent);
        Assert.Equal(8.4, view.CacheWrite5mPercent);
        Assert.Equal(20.2, view.CacheWrite1hPercent);
        Assert.Equal(42.0, view.CacheReadPercent);
        Assert.Equal(21.0, view.OutputPercent);
        Assert.Equal(0.0, view.CacheWriteUnknownPercent);
    }

    /// <summary>
    /// トークン数の比とコスト換算の比は別物になる。
    /// 同じ実績でキャッシュ読込はトークン数で 93.5 %、コスト換算では 42.0 %。
    /// 併記して誤読させないための根拠となる差。
    /// </summary>
    [Fact]
    public void C7_トークン数の比とコスト換算の比は一致しない()
    {
        var tokens = new TokenTotals
        {
            Input = 1000,
            Output = 500,
            CacheCreate = 2000,
            CacheCreate5m = 800,
            CacheCreate1h = 1200,
            CacheRead = 50000,
        };
        var acc = new CostAccumulator();
        acc.Add(SessionStatsRecord.ToolClaudeCode, "claude-opus-5", tokens);

        var processed = tokens.Processed(SessionStatsRecord.ToolClaudeCode);
        var tokenSharePercent = Math.Round((double)tokens.CacheRead / processed * 100d, 1);

        Assert.Equal(93.5, tokenSharePercent);
        Assert.Equal(42.0, acc.Build().CacheReadPercent);
    }

    /// <summary>
    /// TTL の記録が無い書き込みは既定 TTL（5 分・1.25 倍）で見積もり、
    /// 量が分かるように専用の構成比へ入れる。
    /// </summary>
    [Fact]
    public void C8_TTL不明分は5分の単価で別枠に計上する()
    {
        var acc = new CostAccumulator();
        acc.Add(SessionStatsRecord.ToolClaudeCode, "claude-opus-5", new TokenTotals
        {
            CacheCreate = 1000,   // 内訳なし = すべて TTL 不明
        });

        var view = acc.Build();

        Assert.True(view.Available);
        Assert.Equal(100.0, view.CacheWriteUnknownPercent);
        Assert.Equal(0.0, view.CacheWrite5mPercent);
    }

    /// <summary>
    /// 単価が分からないモデルは換算に混ぜず、除外量とモデル名を残す。
    /// </summary>
    [Fact]
    public void C9_単価不明のモデルは除外して明示する()
    {
        var acc = new CostAccumulator();
        acc.Add(SessionStatsRecord.ToolClaudeCode, "claude-opus-5",
            new TokenTotals { Input = 1000, Output = 1000 });
        acc.Add(SessionStatsRecord.ToolClaudeCode, "claude-unlisted-9",
            new TokenTotals { Input = 300, Output = 200 });

        var view = acc.Build();

        Assert.Equal(2000, view.CoveredTokens);
        Assert.Equal(500, view.ExcludedTokens);
        Assert.Equal(new[] { "claude-unlisted-9" }, view.ExcludedModels);
    }

    /// <summary>
    /// Codex は換算対象にしない。キャッシュ書き込み量が記録されず、
    /// TTL の区分も無く、モデル名が公開単価表と対応しないため。
    /// </summary>
    [Fact]
    public void C10_Codexは換算せず除外量に計上する()
    {
        var acc = new CostAccumulator();
        acc.Add(SessionStatsRecord.ToolCodex, "gpt-5.6-sol",
            new TokenTotals { Input = 5000, Output = 1000, CacheRead = 4000 });

        var view = acc.Build();

        Assert.False(view.Available);
        Assert.Equal(0, view.CoveredTokens);

        // Codex は cacheRead が input の内数なので (5000 - 4000) + 1000 = 2000
        Assert.Equal(2000, view.ExcludedTokens);
        Assert.Equal(new[] { "gpt-5.6-sol" }, view.ExcludedModels);
    }
}
