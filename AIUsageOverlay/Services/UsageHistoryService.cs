using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services
{
    /// <summary>
    /// 使用率%の自己記録サービス（デザイン刷新・スパークライン用、新規）。
    ///
    /// 目的:
    ///   API に履歴取得機能が無くても、アプリが定期取得している使用率%を自分で蓄積し、
    ///   その推移（直近の傾き＝いまのペース）をオーバーレイのスパークラインに描く。
    ///
    /// 仕様:
    ///   - サービスごと（Claude / Copilot / Codex）に (timestamp, session%, weekly%) を記録。
    ///   - 保持は直近 5 時間 / 最大 300 点のリングバッファ（Claude セッション枠に一致）。
    ///   - 永続化は %AppData%\AIUsageOverlay\history.json（settings.json / usage.json とは分離）。
    ///   - 起動時に読み込み、5 時間より古い点は破棄する。
    ///   - リセット（%の大きな下落）時も履歴は継続記録する（グラフに崖として出るのが正しい挙動）。
    ///
    /// スレッド前提: 呼び出しは UI スレッド（DispatcherTimer 経由の取得処理）からのみ。
    /// </summary>
    public class UsageHistoryService
    {
        // ── 系列キー（サービス識別子）───────────────────────────────
        /// <summary>Claude セッション枠の履歴系列キー。</summary>
        public const string SeriesClaude = "claude";
        /// <summary>GitHub Copilot（クレジット）の履歴系列キー。</summary>
        public const string SeriesCopilot = "copilot";
        /// <summary>Codex 5時間枠の履歴系列キー。</summary>
        public const string SeriesCodex = "codex";

        /// <summary>
        /// 保持する最大の時間窓（F-16）。既定は AppSettings.SparklineRetentionHours（24h）。
        /// 旧実装は Claude/Codex の 5時間枠に合わせ 5h 固定だったが、断続起動での全点破棄
        /// （＝グラフ非表示）を避けるため設定化・延長した。コンストラクタで注入する（最小 1h でガード）。
        /// </summary>
        private readonly TimeSpan _retentionWindow;

        /// <summary>系列あたりの最大保持点数（リングバッファ上限）。</summary>
        private const int MaxPoints = 300;

        /// <summary>スパークライン描画時に間引く最大点数。</summary>
        private const int MaxRenderPoints = 60;

        /// <summary>同一秒内の重複記録を抑制する最小間隔（連打・二重更新対策）。</summary>
        private static readonly TimeSpan MinRecordInterval = TimeSpan.FromSeconds(1);

        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIUsageOverlay", "history.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        /// <summary>系列キー → 時系列（古い順）の点列。</summary>
        private readonly Dictionary<string, List<UsageHistoryPoint>> _series;

        /// <summary>
        /// 履歴サービスを初期化し、起動時に保持窓外の古い点を掃除する。
        /// </summary>
        /// <param name="retentionHours">
        /// 履歴保持時間（時間）。<see cref="Models.AppSettings.SparklineRetentionHours"/> を渡す。
        /// 0/負値は誤設定として最小 1h に丸める（F-16）。
        /// </param>
        public UsageHistoryService(int retentionHours = 24)
        {
            // F-16: 保持窓を設定値から決定する（最小 1h でガード）
            _retentionWindow = TimeSpan.FromHours(Math.Max(1, retentionHours));

            _series = Load();
            // 起動時に保持窓外の古い点を掃除する
            var now = DateTime.Now;
            foreach (var list in _series.Values)
                Trim(list, now);
        }

        /// <summary>
        /// 指定系列に 1 点を記録する。取得成功のたびに呼ぶ。
        /// 直近 1 秒以内の同系列記録は間引く（二重更新でグラフが密集するのを防ぐ）。
        /// </summary>
        /// <param name="series">系列キー（<see cref="SeriesClaude"/> 等）</param>
        /// <param name="sessionPercent">主使用率（%、0〜100）</param>
        /// <param name="weeklyPercent">週間使用率（%、無い場合は 0）</param>
        public void Record(string series, double sessionPercent, double weeklyPercent)
        {
            var now = DateTime.Now;

            if (!_series.TryGetValue(series, out var list))
            {
                list = new List<UsageHistoryPoint>();
                _series[series] = list;
            }

            // 直近点と時刻が近すぎる場合は最新値で上書きし、点の増殖を防ぐ
            if (list.Count > 0 && now - list[^1].Timestamp < MinRecordInterval)
            {
                list[^1].Session = Clamp(sessionPercent);
                list[^1].Weekly  = Clamp(weeklyPercent);
            }
            else
            {
                list.Add(new UsageHistoryPoint
                {
                    Timestamp = now,
                    Session   = Clamp(sessionPercent),
                    Weekly    = Clamp(weeklyPercent)
                });
            }

            Trim(list, now);
            Save();
        }

        /// <summary>
        /// 指定系列の主使用率(%)を古い順に返す。スパークライン描画用に最大 60 点へ等間隔リサンプリングする。
        /// F-17: 返す前に保持窓 Trim を適用し、実行中と再起動で同じ窓に揃える。
        /// F-16: 点が 1 個のときは水平線として描けるよう同値 2 点へ複製する（取得 1 回でも表示）。
        /// 点が 0 のときのみ空配列（＝スパークライン非表示）。
        /// </summary>
        /// <param name="series">系列キー</param>
        /// <returns>0〜100 の使用率列（古い→新しい順）</returns>
        public IReadOnlyList<double> GetSessionSeries(string series)
        {
            if (!_series.TryGetValue(series, out var list) || list.Count == 0)
                return Array.Empty<double>();

            // F-17: 描画時にも保持窓 Trim を適用する。Trim が Record 内だけだと、取得停止中は
            //       古い点がメモリ上に残り、再起動時（コンストラクタ Trim）のみ消える非対称が生じ、
            //       「昨日は出たのに再起動したら消えた」という症状になるため揃える。
            Trim(list, DateTime.Now);
            if (list.Count == 0)
                return Array.Empty<double>();

            // F-16: 点が 1 個なら水平線として同値 2 点を返す（点<2 での常態非表示を回避）。
            if (list.Count == 1)
            {
                var single = list[0].Session;
                return new[] { single, single };
            }

            var values = list.Select(p => p.Session).ToList();
            return Resample(values, MaxRenderPoints);
        }

        // ── 内部ヘルパー ─────────────────────────────────────────────

        /// <summary>使用率を 0〜100 にクランプする。</summary>
        private static double Clamp(double v) => Math.Max(0.0, Math.Min(100.0, v));

        /// <summary>
        /// 保持窓外の古い点と、最大点数（300）超過分を先頭から削除する。
        /// 保持窓は <see cref="_retentionWindow"/>（設定値、既定 24h）。
        /// </summary>
        private void Trim(List<UsageHistoryPoint> list, DateTime now)
        {
            var cutoff = now - _retentionWindow;
            // 5 時間より古い点を先頭から除去
            int removeCount = 0;
            while (removeCount < list.Count && list[removeCount].Timestamp < cutoff)
                removeCount++;
            if (removeCount > 0)
                list.RemoveRange(0, removeCount);

            // 最大点数を超える分を先頭から除去
            if (list.Count > MaxPoints)
                list.RemoveRange(0, list.Count - MaxPoints);
        }

        /// <summary>
        /// 値列を最大 maxCount 点へ等間隔リサンプリングする（間引き）。
        /// count &lt;= maxCount のときはそのまま返す。両端は必ず含める。
        /// </summary>
        private static List<double> Resample(List<double> values, int maxCount)
        {
            int n = values.Count;
            if (n <= maxCount) return values;

            var result = new List<double>(maxCount);
            // インデックスを 0..n-1 に等間隔でマッピングして抽出する
            for (int i = 0; i < maxCount; i++)
            {
                int idx = (int)Math.Round((double)i * (n - 1) / (maxCount - 1));
                result.Add(values[idx]);
            }
            return result;
        }

        /// <summary>history.json を読み込む。存在しない・破損時は空を返す（握りつぶさず空で継続）。</summary>
        private static Dictionary<string, List<UsageHistoryPoint>> Load()
        {
            if (!File.Exists(HistoryFilePath))
                return new Dictionary<string, List<UsageHistoryPoint>>();
            try
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<UsageHistoryPoint>>>(
                    File.ReadAllText(HistoryFilePath));
                return loaded ?? new Dictionary<string, List<UsageHistoryPoint>>();
            }
            catch
            {
                return new Dictionary<string, List<UsageHistoryPoint>>();
            }
        }

        /// <summary>現在の履歴を history.json へ保存する。書き込み失敗は無視する（描画は継続可能）。</summary>
        private void Save()
        {
            try
            {
                File.WriteAllText(HistoryFilePath, JsonSerializer.Serialize(_series, JsonOptions));
            }
            catch
            {
                // 永続化失敗はスパークラインの継続性を損なうだけなので致命ではない
            }
        }
    }
}
