using System.Drawing;
using System.Drawing.Drawing2D;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// トレイアイコンのビットマップ描画を担当する静的クラス。F-01 で App.xaml.cs から分離した。
    ///
    /// 提供する 2 形式:
    ///   - <see cref="RenderDualBar"/>: 上段=セッション / 下段=週間 の2段バー（既定）。
    ///     CodexBar IconRenderer.swift / Win-CodexBar tray/render.rs を参考に移植。
    ///   - <see cref="RenderDonut"/>: 従来のドーナツ + 中央%テキスト（設定で選択可）。
    ///
    /// 色は <see cref="UsageLevelHelper"/> に一本化し、閾値色の二重定義を排除する。
    /// stale（取得失敗で情報が古い）時はアルファを下げて減光表現する（macOS 版準拠）。
    ///
    /// 移植元ライセンス: CodexBar (MIT, steipete) / Win-CodexBar (MIT, Finesssee)。
    /// </summary>
    public static class TrayIconRenderer
    {
        /// <summary>キャンバスサイズ（px）。GDI+ で 32×32 を描画する。</summary>
        private const int Size = 32;

        // ── stale 時のアルファ（CodexBar と同値）──────────────────────
        /// <summary>stale 時のフィルのアルファ（55%）。</summary>
        private const int StaleFillAlpha = 140;   // 255 * 0.55
        /// <summary>stale 時のトラックのアルファ（18%）。</summary>
        private const int StaleTrackAlpha = 46;   // 255 * 0.18
        /// <summary>通常時のトラックのアルファ（28%）。</summary>
        private const int NormalTrackAlpha = 71;  // 255 * 0.28

        /// <summary>トラック（未使用部分）の基準色。グレー。</summary>
        private static readonly Color TrackBaseColor = Color.FromArgb(0x80, 0x80, 0x80);

        /// <summary>
        /// 上段=セッション / 下段=週間 の2段バーアイコンを描画する（F-01）。
        ///
        /// 座標（32×32、Win-CodexBar tray/render.rs の実証値に準拠）:
        ///   上段バー: x=2, y=7,  w=28, h=10, 角丸 r=3
        ///   下段バー: x=2, y=21, w=28, h=6,  角丸 r=3
        /// フィル幅は整数ピクセルにスナップしてサブピクセルのにじみを防ぐ。
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <param name="weeklyPercent">週間使用率（0〜100）</param>
        /// <param name="stale">true のとき減光表示（情報が古い）</param>
        /// <param name="settings">閾値色の判定に使う設定</param>
        /// <returns>32×32 の ARGB ビットマップ（呼び出し元が Dispose する）</returns>
        public static Bitmap RenderDualBar(int sessionPercent, int weeklyPercent, bool stale, AppSettings settings)
        {
            var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // 上段: セッション（高さ 10px）
            DrawBar(g, sessionPercent, x: 2, y: 7, w: 28, h: 10, radius: 3, stale, settings);
            // 下段: 週間（高さ 6px、細め）
            DrawBar(g, weeklyPercent, x: 2, y: 21, w: 28, h: 6, radius: 3, stale, settings);

            return bmp;
        }

        /// <summary>
        /// 1 本の横バー（トラック + フィル）を描画する内部ヘルパー。
        /// </summary>
        /// <param name="g">描画先グラフィックス</param>
        /// <param name="percent">使用率（0〜100、範囲外はクランプ）</param>
        /// <param name="x">左端 X</param>
        /// <param name="y">上端 Y</param>
        /// <param name="w">全幅</param>
        /// <param name="h">高さ</param>
        /// <param name="radius">角丸半径</param>
        /// <param name="stale">減光するか</param>
        /// <param name="settings">閾値設定</param>
        private static void DrawBar(Graphics g, int percent, int x, int y, int w, int h,
                                    int radius, bool stale, AppSettings settings)
        {
            int clamped = Math.Clamp(percent, 0, 100);

            // ── トラック（背景）──
            int trackAlpha = stale ? StaleTrackAlpha : NormalTrackAlpha;
            using (var trackBrush = new SolidBrush(Color.FromArgb(trackAlpha,
                       TrackBaseColor.R, TrackBaseColor.G, TrackBaseColor.B)))
            using (var trackPath = RoundedRect(x, y, w, h, radius))
                g.FillPath(trackBrush, trackPath);

            if (clamped <= 0) return;

            // ── フィル（使用率分）──
            // フィル幅を整数ピクセルにスナップ（サブピクセル起因のにじみ防止）
            int fillWidth = (int)Math.Round(w * clamped / 100.0);
            if (fillWidth <= 0) return;

            // 閾値色を取得し、stale ならアルファを 55% に落とす
            var baseColor = ColorTranslator.FromHtml(UsageLevelHelper.GetHex(clamped, settings));
            var fillColor = stale
                ? Color.FromArgb(StaleFillAlpha, baseColor.R, baseColor.G, baseColor.B)
                : baseColor;

            // 幅が小さいときに角丸半径がはみ出さないよう調整する
            int fillRadius = Math.Min(radius, fillWidth / 2);

            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath  = RoundedRect(x, y, fillWidth, h, fillRadius);
            g.FillPath(fillBrush, fillPath);
        }

        /// <summary>
        /// 角丸矩形の <see cref="GraphicsPath"/> を生成する（GDI+ には直接の角丸矩形が無いため）。
        /// radius が 0 以下のときは通常の矩形として返す。
        /// </summary>
        private static GraphicsPath RoundedRect(int x, int y, int w, int h, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(new Rectangle(x, y, w, h));
                return path;
            }

            int d = radius * 2;                 // 角の円弧の直径
            d = Math.Min(d, Math.Min(w, h));     // 幅・高さより大きい直径を防ぐ

            path.AddArc(x, y, d, d, 180, 90);                    // 左上
            path.AddArc(x + w - d, y, d, d, 270, 90);            // 右上
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);      // 右下
            path.AddArc(x, y + h - d, d, d, 90, 90);             // 左下
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// セッション使用率を視覚化したドーナツ型アイコンを描画する（従来デザイン、F-01 で移設）。
        ///
        /// デザイン:
        ///   - 外枠: ダーク円（#1C1C1C）
        ///   - 進捗弧: 使用率に応じた閾値色で -90° から時計回りに描画
        ///   - 内枠: ダーク円で中抜き（ドーナツ形状）
        ///   - 中央テキスト: 使用率（%）を白で表示
        /// stale 時は進捗弧・テキストのアルファを 55% に落として減光する。
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <param name="stale">true のとき減光表示</param>
        /// <param name="settings">閾値色の判定に使う設定</param>
        /// <returns>32×32 の ARGB ビットマップ（呼び出し元が Dispose する）</returns>
        public static Bitmap RenderDonut(int sessionPercent, bool stale, AppSettings settings)
        {
            var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // 使用率に応じた閾値色（F-03 の UsageLevelHelper に一本化）。stale ならアルファ減光。
            var baseColor = ColorTranslator.FromHtml(UsageLevelHelper.GetHex(sessionPercent, settings));
            Color progressColor = stale
                ? Color.FromArgb(StaleFillAlpha, baseColor.R, baseColor.G, baseColor.B)
                : baseColor;

            // 外側のダーク円（背景）
            using (var bgBrush = new SolidBrush(Color.FromArgb(255, 28, 28, 28)))
                g.FillEllipse(bgBrush, 1, 1, Size - 2, Size - 2);

            // 進捗弧（-90° から時計回りに sweepAngle 度）
            if (sessionPercent > 0)
            {
                float sweepAngle = Math.Min(sessionPercent, 100) * 3.6f;
                using var progressBrush = new SolidBrush(progressColor);
                g.FillPie(progressBrush, 2, 2, Size - 4, Size - 4, -90f, sweepAngle);
            }

            // 内側のダーク円（ドーナツの穴）
            int holeSize   = Size - 14;
            int holeOffset = (Size - holeSize) / 2;
            using (var holeBrush = new SolidBrush(Color.FromArgb(255, 28, 28, 28)))
                g.FillEllipse(holeBrush, holeOffset, holeOffset, holeSize, holeSize);

            // 中央にパーセンテージテキストを描画する
            string text     = $"{sessionPercent}%";
            float  fontSize = sessionPercent >= 100 ? 6.5f : 7.5f;  // 3桁のとき少し小さく
            using var font  = new Font("Arial", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            var textColor   = stale ? Color.FromArgb(StaleFillAlpha, 255, 255, 255) : Color.White;
            using var brush = new SolidBrush(textColor);
            var textSize    = g.MeasureString(text, font);
            float tx        = (Size - textSize.Width)  / 2f;
            float ty        = (Size - textSize.Height) / 2f;
            g.DrawString(text, font, brush, tx, ty);

            return bmp;
        }

        /// <summary>
        /// セッション使用率をストローク弧（リング）で表すアイコンを描画する（デザイン刷新 1e・既定）。
        ///
        /// デザイン:
        ///   - トラック: グレー 20%（stale 時 18%）の全周の細い円環（線幅 4px）
        ///   - 進捗弧: 使用率に応じた閾値色で -90°（真上）から時計回りに描画（線幅 4px・丸端）
        ///   - 中央テキスト: 使用率（%）を白 Bold で表示（100% 時はフォント縮小）
        /// 塗りつぶし円弧（FillPie）より視認性が高く、線が細いぶん数値が読みやすい。
        /// stale 時は弧・テキストのアルファを 55% に落として減光する。
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <param name="stale">true のとき減光表示</param>
        /// <param name="settings">閾値色の判定に使う設定</param>
        /// <returns>32×32 の ARGB ビットマップ（呼び出し元が Dispose する）</returns>
        public static Bitmap RenderRing(int sessionPercent, bool stale, AppSettings settings)
        {
            var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            int clamped = Math.Clamp(sessionPercent, 0, 100);

            // 弧が端で切れないよう、線幅の半分＋1px を内側に取った描画矩形
            const float penWidth = 4f;
            float inset = penWidth / 2f + 1f;
            var rect = new RectangleF(inset, inset, Size - inset * 2f, Size - inset * 2f);

            // ── トラック（全周・グレー）──
            int trackAlpha = stale ? StaleTrackAlpha : 51; // 51 ≒ 255 * 0.20
            using (var trackPen = new Pen(Color.FromArgb(trackAlpha,
                       TrackBaseColor.R, TrackBaseColor.G, TrackBaseColor.B), penWidth))
            {
                g.DrawEllipse(trackPen, rect);
            }

            // ── 進捗弧（-90° から時計回り、レベル色）──
            if (clamped > 0)
            {
                var baseColor = ColorTranslator.FromHtml(UsageLevelHelper.GetHex(clamped, settings));
                var arcColor  = stale
                    ? Color.FromArgb(StaleFillAlpha, baseColor.R, baseColor.G, baseColor.B)
                    : baseColor;
                using var arcPen = new Pen(arcColor, penWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap   = LineCap.Round
                };
                float sweep = clamped * 3.6f; // 100% = 360°
                g.DrawArc(arcPen, rect, -90f, sweep);
            }

            // ── 中央テキスト（%）──
            string text     = $"{clamped}%";
            float  fontSize = clamped >= 100 ? 8f : 9.5f; // 3 桁のとき縮小
            using var font  = new Font("Arial", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            var textColor   = stale ? Color.FromArgb(StaleFillAlpha, 255, 255, 255) : Color.White;
            using var brush = new SolidBrush(textColor);
            var ts          = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (Size - ts.Width) / 2f, (Size - ts.Height) / 2f);

            return bmp;
        }

        /// <summary>
        /// セッション使用率を「大きな%数値 ＋ 下部ミニバー」で表すアイコンを描画する（デザイン刷新 1e・選択肢）。
        ///
        /// デザイン:
        ///   - 上部: 使用率（%）を Bold・レベル色で表示（100% 時はフォント縮小）
        ///   - 下部: 幅 22 × 高さ 3 のミニバー（トラック＋レベル色フィル）
        /// stale 時はフィル 55% / トラック 18% に減光する（全形式共通）。
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <param name="stale">true のとき減光表示</param>
        /// <param name="settings">閾値色の判定に使う設定</param>
        /// <returns>32×32 の ARGB ビットマップ（呼び出し元が Dispose する）</returns>
        public static Bitmap RenderNumeric(int sessionPercent, bool stale, AppSettings settings)
        {
            var bmp = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            int clamped   = Math.Clamp(sessionPercent, 0, 100);
            var baseColor = ColorTranslator.FromHtml(UsageLevelHelper.GetHex(clamped, settings));
            var color     = stale
                ? Color.FromArgb(StaleFillAlpha, baseColor.R, baseColor.G, baseColor.B)
                : baseColor;

            // ── 上部: %数値 ──
            string text     = $"{clamped}%";
            float  fontSize = clamped >= 100 ? 12f : 15f;
            using (var font  = new Font("Arial", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(color))
            {
                var ts = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush, (Size - ts.Width) / 2f, 1f);
            }

            // ── 下部: ミニバー（幅 22 × 高さ 3、中央下）──
            int barW = 22, barH = 3;
            int barX = (Size - barW) / 2;
            int barY = Size - barH - 4;

            int trackAlpha = stale ? StaleTrackAlpha : NormalTrackAlpha;
            using (var trackBrush = new SolidBrush(Color.FromArgb(trackAlpha,
                       TrackBaseColor.R, TrackBaseColor.G, TrackBaseColor.B)))
            using (var trackPath = RoundedRect(barX, barY, barW, barH, 1))
                g.FillPath(trackBrush, trackPath);

            int fillWidth = (int)Math.Round(barW * clamped / 100.0);
            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(color);
                using var fillPath  = RoundedRect(barX, barY, fillWidth, barH, 1);
                g.FillPath(fillBrush, fillPath);
            }

            return bmp;
        }
    }
}
