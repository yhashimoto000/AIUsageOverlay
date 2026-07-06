namespace AIUsageOverlay.Models
{
    /// <summary>
    /// 消費ペースの段階（F-05）。予定（経過時間に応じた想定消費率）に対する実消費率のズレで決まる。
    /// 「Ahead（先行）」が悪化方向（＝予定より速く使っている）、「Behind（余裕）」が良い方向。
    /// CodexBar UsagePace.swift 準拠。
    /// </summary>
    public enum PaceStage
    {
        /// <summary>予定どおり（|delta| ≤ 2%）。</summary>
        OnTrack,
        /// <summary>やや先行（2% &lt; delta ≤ 6%）。</summary>
        SlightlyAhead,
        /// <summary>先行（6% &lt; delta ≤ 12%）。</summary>
        Ahead,
        /// <summary>大幅先行（delta &gt; 12%）。</summary>
        FarAhead,
        /// <summary>やや余裕（-6% ≤ delta &lt; -2%）。</summary>
        SlightlyBehind,
        /// <summary>余裕（-12% ≤ delta &lt; -6%）。</summary>
        Behind,
        /// <summary>大幅余裕（delta &lt; -12%）。</summary>
        FarBehind
    }

    /// <summary>
    /// ペース計算結果（不変・F-05）。CodexBar UsagePace.swift 互換の線形モデル。
    /// </summary>
    /// <param name="Stage">ペース段階</param>
    /// <param name="DeltaPercent">予定比（実消費率 - 予定消費率）。+ が先行（悪化方向）</param>
    /// <param name="ExpectedUsedPercent">経過時間に応じた予定消費率（%）</param>
    /// <param name="ActualUsedPercent">実消費率（%）</param>
    /// <param name="Eta">枯渇予測までの時間。リセットまで持つ場合は null</param>
    /// <param name="WillLastToReset">現在ペースでリセットまで持つか</param>
    /// <param name="SpeedMultiplierToReset">
    /// 「この倍率までペースを落とせばリセットまで持つ」係数。算出不能時は null。
    /// </param>
    public sealed record UsagePace(
        PaceStage Stage,
        double DeltaPercent,
        double ExpectedUsedPercent,
        double ActualUsedPercent,
        TimeSpan? Eta,
        bool WillLastToReset,
        double? SpeedMultiplierToReset);
}
