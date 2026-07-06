using System.Windows;
using System.Windows.Media;
// UseWindowsForms 有効化で System.Drawing が暗黙 using に入り、Brush / Color / Pen / Point が
// System.Drawing 側と衝突する（CS0104）。WPF 側の型に固定するためエイリアスで明示する。
using Brush           = System.Windows.Media.Brush;
using Color           = System.Windows.Media.Color;
using Pen             = System.Windows.Media.Pen;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Point           = System.Windows.Point;

namespace AIUsageOverlay.Controls
{
    /// <summary>
    /// プログレスバーに重ねて、閾値位置（注意%・警告%）に縦線マーカーを描く軽量コントロール（F-03）。
    ///
    /// ProgressBar と同じ Grid セルに後置し、幅（ActualWidth）を 0〜100% に対応させて
    /// 各閾値の X 位置に 1px の縦線を描画する。CodexBar の warningMarkerPercents に相当。
    /// FrameworkElement を継承し OnRender だけを実装する（ヒットテスト不要のため IsHitTestVisible=false 前提）。
    /// </summary>
    public sealed class ThresholdMarkerOverlay : FrameworkElement
    {
        /// <summary>
        /// マーカーを描く閾値の配列（0〜100 の百分率）。null / 空なら何も描かない。
        /// </summary>
        public static readonly DependencyProperty ThresholdsProperty =
            DependencyProperty.Register(
                nameof(Thresholds), typeof(double[]), typeof(ThresholdMarkerOverlay),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>マーカー線のブラシ。既定は白 35% 不透明。</summary>
        public static readonly DependencyProperty MarkerBrushProperty =
            DependencyProperty.Register(
                nameof(MarkerBrush), typeof(Brush), typeof(ThresholdMarkerOverlay),
                new FrameworkPropertyMetadata(
                    CreateDefaultBrush(), FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>マーカーを描く閾値（百分率）の配列。</summary>
        public double[]? Thresholds
        {
            get => (double[]?)GetValue(ThresholdsProperty);
            set => SetValue(ThresholdsProperty, value);
        }

        /// <summary>マーカー線のブラシ。</summary>
        public Brush MarkerBrush
        {
            get => (Brush)GetValue(MarkerBrushProperty);
            set => SetValue(MarkerBrushProperty, value);
        }

        /// <summary>既定ブラシ（白・アルファ 35%）を生成して凍結する。</summary>
        private static SolidColorBrush CreateDefaultBrush()
        {
            // 0x59 = 89 ≒ 255 * 0.35
            var brush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 各閾値位置に幅 1px・バー高いっぱいの縦線を描画する。
        /// X 位置は ActualWidth * threshold / 100。0% と 100% は端に潰れて見えないため描かない。
        /// </summary>
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var thresholds = Thresholds;
            if (thresholds == null || thresholds.Length == 0) return;
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            var pen = new Pen(MarkerBrush, 1.0);
            pen.Freeze();

            foreach (var t in thresholds)
            {
                // 0 以下・100 以上は端に隠れるため描画しない
                if (t <= 0 || t >= 100) continue;

                // ピクセル境界に合わせて 0.5 オフセット（1px 線をくっきり見せる）
                double x = Math.Round(ActualWidth * t / 100.0) + 0.5;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }
}
