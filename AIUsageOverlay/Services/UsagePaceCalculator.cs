using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// 消費ペースを計算する純関数群（F-05）。副作用なし・状態なし。
    ///
    /// CodexBar (MIT, steipete) UsagePace.swift の線形モデルを移植:
    ///   「経過時間に応じた予定消費率」と「実消費率」のズレ（delta）で段階を判定し、
    ///   現在ペースを外挿してリセット前に枯渇するか（ETA）を求める。
    ///
    /// 営業日補正（workDays）は初期実装ではスコープ外。
    /// </summary>
    public static class UsagePaceCalculator
    {
        /// <summary>表示ゲート: 窓開始直後（経過割合がこの値未満）はノイズが大きいので計算対象外。</summary>
        private const double DisplayGateRatio = 0.03;

        /// <summary>
        /// 使用率・窓長・残り時間からペースを計算する。計算不能・表示不適当な場合は null。
        /// </summary>
        /// <param name="actualUsedPercent">実使用率（0〜100。100 超も許容＝上限到達扱い）</param>
        /// <param name="windowMinutes">窓の長さ（分）。例: セッション 300 / 週間 10080</param>
        /// <param name="remainingMinutes">リセットまでの残り（分）</param>
        /// <returns>ペース結果。窓外・リセット直後の不整合・ゲート未達時は null</returns>
        public static UsagePace? Compute(double actualUsedPercent, int windowMinutes, int remainingMinutes)
        {
            if (windowMinutes <= 0) return null;

            double duration       = windowMinutes * 60.0;    // 秒
            double timeUntilReset = remainingMinutes * 60.0; // 秒

            // 残りが 0 以下、または窓長を超える（データ不整合）→ 計算不能
            if (timeUntilReset <= 0 || timeUntilReset > duration) return null;

            double elapsed = duration - timeUntilReset;

            // リセット直後の不整合（経過 0 なのに使用率あり）→ null
            if (elapsed <= 0 && actualUsedPercent > 0) return null;

            // 窓開始直後 3% 未満はノイズ抑制のため非表示
            if (elapsed / duration < DisplayGateRatio) return null;

            double expected = elapsed / duration * 100.0;   // 予定消費率
            double delta    = actualUsedPercent - expected; // 予定比（+ が先行＝悪化方向）
            PaceStage stage = ClassifyStage(delta);

            // 枯渇予測（ETA）
            TimeSpan? eta = null;
            bool willLast;
            if (actualUsedPercent >= 100.0)
            {
                // 既に上限到達
                willLast = false;
                eta      = TimeSpan.Zero;
            }
            else
            {
                double rate = actualUsedPercent / elapsed; // %/秒（elapsed>0 はゲートで保証）
                if (rate > 0)
                {
                    double candidateSeconds = (100.0 - actualUsedPercent) / rate; // 枯渇までの秒
                    willLast = candidateSeconds >= timeUntilReset;
                    if (!willLast) eta = TimeSpan.FromSeconds(candidateSeconds);
                }
                else
                {
                    // 使用率 0 → 消費していないので必ず持つ
                    willLast = true;
                }
            }

            // 「この倍率までペースを落とせば持つ」係数。actual=0 や分母 0 は算出不能。
            double? speedMultiplierToReset = null;
            if (actualUsedPercent > 0)
            {
                double denom = actualUsedPercent * timeUntilReset / elapsed;
                if (denom > 0) speedMultiplierToReset = (100.0 - actualUsedPercent) / denom;
            }

            return new UsagePace(stage, delta, expected, actualUsedPercent,
                                 eta, willLast, speedMultiplierToReset);
        }

        /// <summary>予定比（delta）から段階を判定する。|delta| の大きさと符号（+=先行）で分類。</summary>
        private static PaceStage ClassifyStage(double delta)
        {
            double abs   = Math.Abs(delta);
            bool   ahead = delta > 0;

            if (abs <= 2)  return PaceStage.OnTrack;
            if (abs <= 6)  return ahead ? PaceStage.SlightlyAhead : PaceStage.SlightlyBehind;
            if (abs <= 12) return ahead ? PaceStage.Ahead        : PaceStage.Behind;
            return ahead ? PaceStage.FarAhead : PaceStage.FarBehind;
        }
    }
}
