using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// 使用率の「表示レベル」判定と、それに対応する色（16進表記）を一元管理する静的ヘルパー。
    ///
    /// F-03 で新設。従来はトレイ（App.xaml.cs）・週間ドット（MainWindow.xaml.cs）・
    /// オーバーレイのバー色が各所にハードコードされ、注意色が #FF8C00 と #FFC107 で
    /// 食い違っていた。本ヘルパーに一本化することで閾値・色の二重定義を解消する。
    ///
    /// 依存を持たない（System.Drawing / System.Windows.Media いずれの Color 型にも依存しない）よう、
    /// 色は 16進文字列で返す。GDI 側は ColorTranslator.FromHtml、WPF 側は
    /// ColorConverter.ConvertFromString で各々の Color 型へ変換する。
    /// </summary>
    public static class UsageLevelHelper
    {
        /// <summary>使用率の表示レベル。閾値（AppSettings）で境界が決まる。</summary>
        public enum UsageLevel
        {
            /// <summary>通常（緑）。CautionThresholdPercent 未満。</summary>
            Normal,
            /// <summary>注意（オレンジ）。CautionThresholdPercent 以上 WarningThresholdPercent 未満。</summary>
            Caution,
            /// <summary>警告（赤）。WarningThresholdPercent 以上。</summary>
            Warning
        }

        /// <summary>通常レベルの色（緑）。</summary>
        public const string NormalHex = "#4CAF50";

        /// <summary>注意レベルの色（オレンジ）。旧トレイ実装 #FF8C00 に統一。</summary>
        public const string CautionHex = "#FF8C00";

        /// <summary>警告レベルの色（赤）。</summary>
        public const string WarningHex = "#F44336";

        /// <summary>
        /// 使用率（%）と設定の閾値から表示レベルを判定する。
        /// 判定順序は Warning → Caution → Normal（高い方から評価）。
        /// </summary>
        /// <param name="percent">使用率（0〜100、範囲外でも判定は成立する）</param>
        /// <param name="settings">閾値を保持する設定</param>
        /// <returns>対応する <see cref="UsageLevel"/></returns>
        public static UsageLevel GetLevel(double percent, AppSettings settings)
        {
            if (percent >= settings.WarningThresholdPercent) return UsageLevel.Warning;
            if (percent >= settings.CautionThresholdPercent) return UsageLevel.Caution;
            return UsageLevel.Normal;
        }

        /// <summary>レベルに対応する色の 16進表記（"#RRGGBB"）を返す。</summary>
        public static string GetHex(UsageLevel level) => level switch
        {
            UsageLevel.Warning => WarningHex,
            UsageLevel.Caution => CautionHex,
            _                  => NormalHex
        };

        /// <summary>使用率と設定から直接、色の 16進表記を返す簡易ヘルパー。</summary>
        public static string GetHex(double percent, AppSettings settings)
            => GetHex(GetLevel(percent, settings));
    }
}
