namespace AIUsageOverlay.Services
{
    /// <summary>
    /// 操作からの経過時間・オーバーレイ表示状態・電源制約から、次回更新間隔を決める純関数（F-10）。
    ///
    /// CodexBar (MIT, steipete) AdaptiveRefreshPolicy.swift を参考に移植。
    /// 固定間隔（既定30秒）は WebView2 スクレイピングとしては高頻度で、放置中は無駄が大きいため、
    /// 操作直後は短く・放置時は長く・電源制約時は最長にする。
    ///
    /// 副作用なし。電源状態やウィンドウ可視性の取得は呼び出し側（ViewModel）が行い、値で渡す。
    /// </summary>
    public static class AdaptiveRefreshPolicy
    {
        /// <summary>
        /// 次回更新間隔を算出する。判定は上から順に評価する（先に一致したものを採用）。
        ///
        /// | 条件 | 間隔 |
        /// |---|---|
        /// | 電源制約（バッテリー残量僅少 / 省電力） | 30 分 |
        /// | オーバーレイ非表示（トレイのみ常駐） | 15 分 |
        /// | 最終操作から 5 分以内 | baseIntervalSeconds（既定 30 秒） |
        /// | 〜1 時間 | 2 分 |
        /// | 〜4 時間 | 5 分 |
        /// | 4 時間超 | 15 分 |
        /// </summary>
        /// <param name="now">現在時刻</param>
        /// <param name="lastInteractionAt">最後にユーザー操作があった時刻</param>
        /// <param name="isOverlayVisible">オーバーレイが表示中か</param>
        /// <param name="baseIntervalSeconds">操作直後に用いる基準間隔（秒）。設定の更新間隔</param>
        /// <param name="powerConstrained">電源制約下か（バッテリー残量僅少など）</param>
        /// <returns>次回更新までの間隔</returns>
        public static TimeSpan Compute(
            DateTime now, DateTime lastInteractionAt, bool isOverlayVisible,
            int baseIntervalSeconds, bool powerConstrained)
        {
            if (powerConstrained)  return TimeSpan.FromMinutes(30);
            if (!isOverlayVisible) return TimeSpan.FromMinutes(15);

            var sinceInteraction = now - lastInteractionAt;

            if (sinceInteraction <= TimeSpan.FromMinutes(5))
                return TimeSpan.FromSeconds(Math.Max(5, baseIntervalSeconds)); // 操作直後は基準間隔
            if (sinceInteraction <= TimeSpan.FromHours(1))
                return TimeSpan.FromMinutes(2);
            if (sinceInteraction <= TimeSpan.FromHours(4))
                return TimeSpan.FromMinutes(5);

            return TimeSpan.FromMinutes(15); // 4 時間超の放置
        }
    }
}
