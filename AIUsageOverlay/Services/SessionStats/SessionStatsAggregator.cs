using AIUsageOverlay.Models;
using System.Globalization;
using System.IO;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>レポート全体。HTML へ埋め込む JSON の最上位（設計書 3.2）。</summary>
    public class UsageReport
    {
        /// <summary>生成時刻（ローカル、表示用の文字列）。</summary>
        public string GeneratedAt { get; set; } = string.Empty;

        /// <summary>生成したアプリのバージョン。</summary>
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>指示文を出力しているか（S11）。false のとき画面は説明を出す。</summary>
        public bool IncludePrompt { get; set; }

        /// <summary>データ品質の情報（設計書 8.5）。</summary>
        public QualityInfo Quality { get; set; } = new();

        /// <summary>セッション一覧。期間による絞り込みは periods 側のキー一覧で行う。</summary>
        public List<SessionView> Sessions { get; set; } = new();

        /// <summary>期間ごとの集計。キーは "7" / "30" / "all"。</summary>
        public Dictionary<string, PeriodView> Periods { get; set; } = new();

        /// <summary>
        /// Cursor の月別トークン累積（cursor-tokens.json 由来）。
        /// キーは "yyyy-MM"。ローカル履歴にはトークン情報が無いため
        /// ダッシュボード API 傍受で取得した値を別途注入する。
        /// </summary>
        public Dictionary<string, Models.CursorMonthlyTokens> CursorMonthly { get; set; } = new();

        /// <summary>
        /// 効果・稼働タブのデータ（全期間固定）。
        ///
        /// 上部の期間ボタンでは切り替えない。月ごとの推移は Periods の "month:yyyy-MM" を参照する。
        /// 期間を絞ると定着率や時間帯別の母数が小さくなり、かえって読みづらくなるため。
        /// </summary>
        public ImpactView Impact { get; set; } = new();
    }

    /// <summary>セッション一覧の 1 行。</summary>
    public class SessionView
    {
        /// <summary>一意キー。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>ツール識別子。</summary>
        public string Tool { get; set; } = string.Empty;

        /// <summary>親セッションのキー。無ければ null。</summary>
        public string? ParentKey { get; set; }

        /// <summary>サブエージェントかどうか。</summary>
        public bool IsSubagent { get; set; }

        /// <summary>表示用のエージェント名。</summary>
        public string? AgentLabel { get; set; }

        /// <summary>プロジェクトの表示名。</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>開始時刻（ローカル、表示用）。</summary>
        public string Start { get; set; } = string.Empty;

        /// <summary>開始日（yyyy-MM-dd、絞り込み用）。</summary>
        public string StartDay { get; set; } = string.Empty;

        /// <summary>経過時間（秒）。測定不可なら 0。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>経過時間を測定できたか。false なら「測定不可」バッジを出す。</summary>
        public bool ElapsedMeasurable { get; set; }

        /// <summary>末尾行が壊れていたか。true なら過小計上の可能性バッジを出す。</summary>
        public bool LastLineBroken { get; set; }

        /// <summary>最初の指示文（先頭 64 文字）。出力しない設定なら null。</summary>
        public string? Prompt { get; set; }

        /// <summary>ターン数。</summary>
        public int Turns { get; set; }

        /// <summary>主なモデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>課金相当トークン（単独値）。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン（単独値）。</summary>
        public long Processed { get; set; }

        /// <summary>課金相当トークン（自身 + 配下の全サブエージェント）。</summary>
        public long BillableCumulative { get; set; }

        /// <summary>総処理トークン（自身 + 配下の全サブエージェント）。</summary>
        public long ProcessedCumulative { get; set; }

        /// <summary>配下のサブエージェント数（間接を含む）。</summary>
        public int SubagentCount { get; set; }

        /// <summary>フェーズ別の実行回数。</summary>
        public Dictionary<string, int> Phases { get; set; } = new();

        // ── 明細（行を開いたときに出す）──────────────────────────

        /// <summary>終了時刻（ローカル、表示用）。</summary>
        public string End { get; set; } = string.Empty;

        /// <summary>会話ツリーのルート ID。同じツリーの兄弟を辿る手掛かり。</summary>
        public string? RootId { get; set; }

        /// <summary>トークンの内訳。どこに量が寄っているかを見るために持つ。</summary>
        public TokenDetailView Tokens { get; set; } = new();

        /// <summary>
        /// このセッションのコスト換算（単価表に無いモデルと Codex は除外）。
        /// 単価の一覧は含めない（期間側に 1 つだけ出す）。
        /// </summary>
        public CostView Cost { get; set; } = new();

        /// <summary>モデル別の内訳。セッション内でモデルが変わる場合に効く。</summary>
        public List<SessionModelView> ByModel { get; set; } = new();

        /// <summary>日別の内訳。日をまたいだセッションで 2 件以上になる。</summary>
        public List<SessionDayView> ByDay { get; set; } = new();

        /// <summary>ツール別の実行回数（多い順）。</summary>
        public List<CountView> Tools { get; set; } = new();

        /// <summary>判別できなかったコマンド（多い順・上位のみ）。</summary>
        public List<CountView> UnknownCommands { get; set; } = new();

        /// <summary>読み取った課金レコード数（重複排除前）。</summary>
        public int RawRecords { get; set; }

        /// <summary>採用した課金レコード数（重複排除後）。</summary>
        public int AcceptedRecords { get; set; }

        /// <summary>鍵が無く無条件に加算した行数。</summary>
        public int KeylessRecords { get; set; }

        /// <summary>JSON として壊れていた行数。</summary>
        public int ParseErrors { get; set; }
    }

    /// <summary>トークンの内訳。画面では「どこに量が寄っているか」を見るために使う。</summary>
    public class TokenDetailView
    {
        /// <summary>入力トークン。</summary>
        public long Input { get; set; }

        /// <summary>出力トークン。</summary>
        public long Output { get; set; }

        /// <summary>キャッシュ作成トークン。</summary>
        public long CacheCreate { get; set; }

        /// <summary>キャッシュ読込トークン。</summary>
        public long CacheRead { get; set; }

        /// <summary>推論トークン（Codex のみ。出力の内数）。</summary>
        public long Reasoning { get; set; }
    }

    /// <summary>セッション明細のモデル別 1 行。</summary>
    public class SessionModelView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>そのモデルで処理したターン数。</summary>
        public int Turns { get; set; }

        /// <summary>消費トークン。</summary>
        public long Billable { get; set; }

        /// <summary>処理トークン。</summary>
        public long Processed { get; set; }
    }

    /// <summary>セッション明細の日別 1 行。</summary>
    public class SessionDayView
    {
        /// <summary>ローカル日付。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>その日のターン数。</summary>
        public int Turns { get; set; }

        /// <summary>その日の消費トークン。</summary>
        public long Billable { get; set; }
    }

    /// <summary>期間 1 つ分の集計結果。</summary>
    public class PeriodView
    {
        /// <summary>画面に出す期間名。</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>集計に含めたセッション数。</summary>
        public int SessionCount { get; set; }

        /// <summary>集計に含めたターン数。</summary>
        public int TurnCount { get; set; }

        /// <summary>プロジェクト数。</summary>
        public int ProjectCount { get; set; }

        /// <summary>課金相当トークンの合計（セッション基準）。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークンの合計（セッション基準）。</summary>
        public long Processed { get; set; }

        /// <summary>キャッシュ読込トークンの合計。</summary>
        public long CacheRead { get; set; }

        /// <summary>推論トークンの合計（Codex のみ）。</summary>
        public long Reasoning { get; set; }

        /// <summary>単価で重み付けしたコスト換算の構成比（Claude Code のみ）。</summary>
        public CostView Cost { get; set; } = new();

        /// <summary>経過時間の合計（秒）。測定可能なセッションのみ。作業時間ではない。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>経過時間を測定できなかったセッション数。</summary>
        public int ElapsedUnmeasurableCount { get; set; }

        /// <summary>フェーズ分類率（0〜100）。母数に未分類を含む。</summary>
        public double ClassifiedPercent { get; set; }

        /// <summary>日別の推移（ターン基準）。</summary>
        public List<DayView> ByDay { get; set; } = new();

        /// <summary>プロジェクト別（セッション基準、単独値の総和）。</summary>
        public List<ProjectView> ByProject { get; set; } = new();

        /// <summary>フェーズ別の実行回数。</summary>
        public Dictionary<string, int> ByPhase { get; set; } = new();

        /// <summary>モデル別。</summary>
        public List<ModelView> ByModel { get; set; } = new();

        /// <summary>ツール別の実行回数。</summary>
        public List<CountView> ByTool { get; set; } = new();

        /// <summary>未分類コマンドの上位。次版で語彙表へ足す判断材料（設計書 7.3.4）。</summary>
        public List<CountView> UnknownCommands { get; set; } = new();

        /// <summary>この期間に含まれるセッションのキー一覧。</summary>
        public List<string> SessionKeys { get; set; } = new();

        /// <summary>この期間の稼働・効果データ（効果・稼働タブ）。</summary>
        public PeriodImpactView Impact { get; set; } = new();
    }

    /// <summary>日別の 1 点。</summary>
    public class DayView
    {
        /// <summary>ローカル日付（yyyy-MM-dd）。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>ツール別の課金相当トークン。積み上げ棒の各段になる。</summary>
        public long ClaudeBillable { get; set; }

        /// <summary>Cowork の課金相当トークン。</summary>
        public long CoworkBillable { get; set; }

        /// <summary>Codex の課金相当トークン。</summary>
        public long CodexBillable { get; set; }

        /// <summary>
        /// Cursor の課金相当トークン。
        /// ローカル履歴は常に 0。ダッシュボード API 蓄積（cursor-tokens.json の日別）を
        /// <see cref="SessionStatsAggregator.ApplyCursorDailyTokens"/> で上書きする。
        /// </summary>
        public long CursorBillable { get; set; }

        /// <summary>その日のターン数。</summary>
        public int Turns { get; set; }
    }

    /// <summary>プロジェクト別の 1 行。</summary>
    public class ProjectView
    {
        /// <summary>プロジェクトの表示名（実パス）。</summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>セッション数。</summary>
        public int SessionCount { get; set; }

        /// <summary>課金相当トークン。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン。</summary>
        public long Processed { get; set; }

        /// <summary>経過時間の合計（秒）。</summary>
        public long ElapsedSec { get; set; }

        /// <summary>フェーズ別の実行回数（構成比バー用）。</summary>
        public Dictionary<string, int> Phases { get; set; } = new();
    }

    /// <summary>モデル別の 1 行。</summary>
    public class ModelView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>課金相当トークン。</summary>
        public long Billable { get; set; }

        /// <summary>総処理トークン。</summary>
        public long Processed { get; set; }

        /// <summary>ターン数。</summary>
        public int Turns { get; set; }
    }

    /// <summary>
    /// 単価で重み付けしたコスト換算の内訳。すべて構成比（0〜100）で持ち、金額は出さない。
    ///
    /// トークン数の比とは別物であることが分かるように、画面では併記せず別枠に出す。
    /// 例えばキャッシュ読込はトークン数では 9 割を占めても、単価が入力の 0.1 倍のため
    /// コスト換算では半分以下になる。
    /// </summary>
    public class CostView
    {
        /// <summary>換算できる実績があったか。false のとき画面は理由だけ出す。</summary>
        public bool Available { get; set; }

        /// <summary>API公開単価の数値部分を係数にした無通貨の換算ポイント。</summary>
        public decimal ApiEquivalentPoints { get; set; }

        /// <summary>API換算ポイントの表示文字列。大値をJavaScript整数精度へ依存させない。</summary>
        public string ApiEquivalentPointsText { get; set; } = string.Empty;

        /// <summary>ツール別のAPI換算ポイント。</summary>
        public List<ToolPointView> ApiEquivalentPointsByTool { get; set; } = new();

        /// <summary>Claude系トークンのうち換算できた割合。対象が無ければ null。</summary>
        public double? CoveragePercent { get; set; }

        /// <summary>換算係数を確認した日。</summary>
        public string CoefficientReferenceDate { get; set; } = string.Empty;

        /// <summary>換算係数の参照元。</summary>
        public string CoefficientSourceUrl { get; set; } = string.Empty;

        /// <summary>新規入力（キャッシュに当たらなかった分）の構成比。</summary>
        public double InputPercent { get; set; }

        /// <summary>キャッシュ書き込み（5 分 TTL、入力単価の 1.25 倍）の構成比。</summary>
        public double CacheWrite5mPercent { get; set; }

        /// <summary>キャッシュ書き込み（1 時間 TTL、入力単価の 2.0 倍）の構成比。</summary>
        public double CacheWrite1hPercent { get; set; }

        /// <summary>
        /// TTL の内訳が記録されていなかったキャッシュ書き込みの構成比。
        /// 既定 TTL（5 分）の単価で見積もっている。
        /// </summary>
        public double CacheWriteUnknownPercent { get; set; }

        /// <summary>キャッシュ読込（入力単価の 0.1 倍）の構成比。</summary>
        public double CacheReadPercent { get; set; }

        /// <summary>出力（入力単価の 5 倍）の構成比。</summary>
        public double OutputPercent { get; set; }

        /// <summary>
        /// 従量課金だった場合の費用（USD）。API 公開単価でトークンを換算した金額。
        ///
        /// 定額プランで実際に支払った額ではない。同じ量を API で使ったらいくらになるか、
        /// という参考値であり、画面でもそう明記する。
        /// </summary>
        public decimal UsdAmount { get; set; }

        /// <summary>
        /// モデル別の USD。どのモデルに費用が寄っているかを出すために持つ。
        /// セッション明細では空にする（同じ表が何百回も JSON に載るため）。
        /// </summary>
        public Dictionary<string, decimal> UsdByModel { get; set; } = new();

        /// <summary>換算に含めたトークン数（課金相当）。</summary>
        public long CoveredTokens { get; set; }

        /// <summary>単価が判明せず除外したトークン数（課金相当）。</summary>
        public long ExcludedTokens { get; set; }

        /// <summary>除外したモデル名。除外を隠さないため必ず出す。</summary>
        public List<string> ExcludedModels { get; set; } = new();

        /// <summary>
        /// 換算に使った単価の一覧（参考値）。単価は改定され得るため、
        /// どの前提で計算したレポートかが後から分かるように残す。
        /// セッションごとの明細では空にする（同じ表が何百回も JSON に載るため）。
        /// </summary>
        public List<ModelPriceView> Prices { get; set; } = new();
    }

    /// <summary>ツール別API換算ポイント。</summary>
    public class ToolPointView
    {
        /// <summary>ツール識別子。</summary>
        public string Tool { get; set; } = string.Empty;

        /// <summary>API換算ポイント。</summary>
        public decimal Points { get; set; }

        /// <summary>API換算ポイントの表示文字列。</summary>
        public string PointsText { get; set; } = string.Empty;
    }

    /// <summary>
    /// 換算に使った係数 1 行。API公開単価の数値部分だけを保持し、通貨単位は表示しない。
    /// 種別ごとの重みと生成時点の前提を示すための参考値として出す。
    /// </summary>
    public class ModelPriceView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>入力の単価。</summary>
        public double Input { get; set; }

        /// <summary>出力の単価。</summary>
        public double Output { get; set; }

        /// <summary>キャッシュ読み込みの単価。</summary>
        public double CacheRead { get; set; }

        /// <summary>キャッシュ書き込み（5 分 TTL）の単価。</summary>
        public double CacheWrite5m { get; set; }

        /// <summary>キャッシュ書き込み（1 時間 TTL）の単価。</summary>
        public double CacheWrite1h { get; set; }
    }

    /// <summary>名前と件数だけの汎用行。</summary>
    public class CountView
    {
        /// <summary>名前。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>件数。</summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// 効果額・費用換算に使う前提値。レポート生成時に設定から渡す。
    ///
    /// いずれも実測値ではなく置き方の問題であり、値次第で金額は何倍にも変わる。
    /// レポートには計算式と併せて必ず表示し、画面上でも変更できるようにする。
    /// </summary>
    /// <param name="HourlyRateJpy">社内工数単価（円 / 時間）</param>
    /// <param name="ReductionFactor">削減係数 k（AI 稼働時間に掛ける倍率）</param>
    /// <param name="UsdJpyRate">為替レート（円 / USD）</param>
    public sealed record ReportAssumptions(
        int HourlyRateJpy, double ReductionFactor, double UsdJpyRate)
    {
        /// <summary>設定を読めない場合の既定値。AppSettings の既定と揃える。</summary>
        public static ReportAssumptions Default { get; } = new(5000, 2.0, 150.0);
    }

    /// <summary>
    /// 効果・稼働タブのうち、期間に依存しない部分。
    ///
    /// 稼働時間・効果額・定着率は上部の期間ボタンで切り替わるため、
    /// 実数は期間ごとに <see cref="PeriodView.Impact"/> へ入れる。
    /// </summary>
    public class ImpactView
    {
        /// <summary>金額計算の前提値。</summary>
        public AssumptionView Assumptions { get; set; } = new();
    }

    /// <summary>
    /// 期間 1 つ分の稼働・効果データ。
    ///
    /// 稼働枠は「枠の日付が期間内か」で絞る。セッションの開始日では絞らない
    /// （日をまたいだセッションの枠が、期間の境界で丸ごと落ちたり入ったりするため）。
    /// </summary>
    public class PeriodImpactView
    {
        /// <summary>時間帯別の稼働率（0 時から 23 時まで 24 件）。</summary>
        public List<HourRateView> Hourly { get; set; } = new();

        /// <summary>時間帯別の母数になった平日の日数（期間内で実際に稼働した平日）。</summary>
        public int HourlyWeekdayCount { get; set; }

        /// <summary>日別の稼働時間（分）。土日も含む。</summary>
        public List<DayMinutesView> ActiveByDay { get; set; } = new();

        /// <summary>稼働時間の合計（分）。効果額の入力になる値。</summary>
        public long TotalActiveMinutes { get; set; }

        /// <summary>定着率。</summary>
        public AdoptionView Adoption { get; set; } = new();

        /// <summary>サブエージェントの内訳。</summary>
        public SubagentView Subagent { get; set; } = new();

        /// <summary>モデル別の費用換算。</summary>
        public List<ModelCostView> ModelCost { get; set; } = new();
    }

    /// <summary>効果額・費用換算の前提値（画面表示用）。</summary>
    public class AssumptionView
    {
        /// <summary>社内工数単価（円 / 時間）。</summary>
        public int HourlyRateJpy { get; set; }

        /// <summary>削減係数 k。</summary>
        public double ReductionFactor { get; set; }

        /// <summary>為替レート（円 / USD）。生成時点の参考値。</summary>
        public double UsdJpyRate { get; set; }

        /// <summary>稼働枠の幅（分）。</summary>
        public int SlotMinutes { get; set; }

        /// <summary>API 単価を公式表と照合した日。</summary>
        public string CoefficientReferenceDate { get; set; } = string.Empty;

        /// <summary>API 単価の参照元。</summary>
        public string CoefficientSourceUrl { get; set; } = string.Empty;
    }

    /// <summary>時間帯 1 つ分の稼働率。</summary>
    public class HourRateView
    {
        /// <summary>時（0〜23）。</summary>
        public int Hour { get; set; }

        /// <summary>稼働していた枠の延べ数。</summary>
        public int ActiveSlots { get; set; }

        /// <summary>稼働率（0〜100）。その時間帯の枠のうち稼働していた割合。</summary>
        public double Rate { get; set; }
    }

    /// <summary>日ごとの稼働時間。</summary>
    public class DayMinutesView
    {
        /// <summary>ローカル日付（yyyy-MM-dd）。</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>稼働時間（分）。</summary>
        public int Minutes { get; set; }
    }

    /// <summary>定着率。母数は土日を除いた営業日で、祝日は考慮しない。</summary>
    public class AdoptionView
    {
        /// <summary>最初に稼働した日。</summary>
        public string FirstDay { get; set; } = string.Empty;

        /// <summary>最後に稼働した日。</summary>
        public string LastDay { get; set; } = string.Empty;

        /// <summary>期間内の営業日数。</summary>
        public int BusinessDays { get; set; }

        /// <summary>そのうち実際に稼働した営業日数。</summary>
        public int UsedBusinessDays { get; set; }

        /// <summary>稼働した日数（土日を含む）。</summary>
        public int ActiveDays { get; set; }

        /// <summary>最長の連続稼働日数（営業日ベース）。</summary>
        public int BestStreak { get; set; }

        /// <summary>最終稼働日から遡った連続稼働日数（営業日ベース）。</summary>
        public int CurrentStreak { get; set; }
    }

    /// <summary>サブエージェントの内訳。</summary>
    public class SubagentView
    {
        /// <summary>親セッション数。</summary>
        public int Parents { get; set; }

        /// <summary>サブエージェント数。</summary>
        public int Subagents { get; set; }

        /// <summary>親セッションの課金相当トークン（単独値の総和）。</summary>
        public long ParentBillable { get; set; }

        /// <summary>サブエージェントの課金相当トークン（単独値の総和）。</summary>
        public long SubagentBillable { get; set; }
    }

    /// <summary>モデル別の費用換算 1 行。</summary>
    public class ModelCostView
    {
        /// <summary>モデル名。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>ターン数。</summary>
        public int Turns { get; set; }

        /// <summary>課金相当トークン。</summary>
        public long Billable { get; set; }

        /// <summary>従量課金だった場合の費用（USD）。換算対象外なら 0。</summary>
        public decimal Usd { get; set; }
    }

    /// <summary>データ品質タブに出す情報（設計書 8.5）。</summary>
    public class QualityInfo
    {
        /// <summary>Claude Code 履歴の状態（notUsed / ok / unreadable）。</summary>
        public string ClaudeStatus { get; set; } = string.Empty;

        /// <summary>Codex 履歴の状態。</summary>
        public string CodexStatus { get; set; } = string.Empty;

        /// <summary>Cowork 履歴の状態。</summary>
        public string CoworkStatus { get; set; } = string.Empty;

        /// <summary>Cursor 履歴の状態。</summary>
        public string CursorStatus { get; set; } = string.Empty;

        /// <summary>Claude Code の履歴ルート。</summary>
        public string ClaudeRoot { get; set; } = string.Empty;

        /// <summary>Codex の履歴ルート。</summary>
        public string CodexRoot { get; set; } = string.Empty;

        /// <summary>Cowork の履歴ルート。</summary>
        public string CoworkRoot { get; set; } = string.Empty;

        /// <summary>Cursor の履歴ルート。</summary>
        public string CursorRoot { get; set; } = string.Empty;

        /// <summary>今回パースしたファイル数。</summary>
        public int ParsedFiles { get; set; }

        /// <summary>キャッシュから流用したファイル数。</summary>
        public int ReusedFiles { get; set; }

        /// <summary>読めなかったファイルの一覧（パスと理由）。</summary>
        public List<SkippedFileView> SkippedFiles { get; set; } = new();

        /// <summary>ワークフローの journal.jsonl の件数。</summary>
        public int JournalFiles { get; set; }

        /// <summary>走査に要した時間（ミリ秒）。Q6 の計測値。</summary>
        public long ScanElapsedMs { get; set; }

        /// <summary>Claude Code で重複排除前に読んだ課金レコード数。</summary>
        public int ClaudeRawRecords { get; set; }

        /// <summary>Claude Code で採用した課金レコード数。</summary>
        public int ClaudeAcceptedRecords { get; set; }

        /// <summary>Claude Code で鍵が無く無条件加算した行数。</summary>
        public int ClaudeKeylessRecords { get; set; }

        /// <summary>Cowork で重複排除前に読んだ課金レコード数。</summary>
        public int CoworkRawRecords { get; set; }

        /// <summary>Cowork で採用した課金レコード数。</summary>
        public int CoworkAcceptedRecords { get; set; }

        /// <summary>Cowork で鍵が無く無条件加算した行数。</summary>
        public int CoworkKeylessRecords { get; set; }

        /// <summary>Cowork の対象ファイル数。</summary>
        public int CoworkFiles { get; set; }

        /// <summary>Cowork の対象ファイル容量。</summary>
        public long CoworkBytes { get; set; }

        /// <summary>entrypoint が無く、専用ルートを根拠に採用したCoworkセッション数。</summary>
        public int CoworkIdentityMissingSessions { get; set; }

        /// <summary>同一キー衝突を別セッションとして保持したCowork件数。</summary>
        public int CoworkKeyCollisions { get; set; }

        /// <summary>Codex のスレッド数（= 一意化後のセッション数）。</summary>
        public int CodexThreads { get; set; }

        /// <summary>Codex のサブエージェント数。</summary>
        public int CodexSubagents { get; set; }

        /// <summary>Cursor のセッション数。</summary>
        public int CursorSessions { get; set; }

        /// <summary>Cursor のサブエージェント数。</summary>
        public int CursorSubagents { get; set; }

        /// <summary>JSON として壊れていた行の総数。</summary>
        public int ParseErrors { get; set; }

        /// <summary>末尾行が壊れていたセッション数（静かな過小計上の恐れ）。</summary>
        public int LastLineBrokenSessions { get; set; }

        /// <summary>経過時間を測定できなかったセッション数。</summary>
        public int UnmeasurableSessions { get; set; }

        /// <summary>キャッシュが壊れていて全件再パースしたか。</summary>
        public bool CacheWasCorrupted { get; set; }

        /// <summary>履歴の最古の日付（保持期間の目安。Q3）。</summary>
        public string OldestRecord { get; set; } = string.Empty;

        /// <summary>履歴の最新の日付。</summary>
        public string NewestRecord { get; set; } = string.Empty;
    }

    /// <summary>読めなかったファイル 1 件。</summary>
    public class SkippedFileView
    {
        /// <summary>ファイルパス。</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>読めなかった理由。</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// セッションレコードを画面用の集計へ畳む（設計書 7 章）。
    ///
    /// 日別はターン基準、プロジェクト別はセッション基準で切る。
    /// 両者の合計は一致しない。これは意図した差であり、画面にも明記する（設計書 7.4 / C9）。
    /// </summary>
    public static class SessionStatsAggregator
    {
        /// <summary>
        /// 期間セレクタの定義（キー、ラベル、遡る日数。日数 0 は全期間）。
        /// 「今日」は当日分だけを見たいという要求のために置く（既定の表示期間）。
        /// </summary>
        private static readonly (string Key, string Label, int Days)[] Periods =
        {
            ("today", "今日", 1),
            ("7", "直近 7 日", 7),
            ("30", "直近 30 日", 30),
            ("all", "全期間", 0),
        };

        /// <summary>ツール別・未分類コマンドの一覧に載せる上限件数。</summary>
        private const int TopCountLimit = 20;

        /// <summary>
        /// セッション明細に載せるツールの上限件数。
        /// 全セッション分を HTML へ埋め込むため、件数を絞ってサイズを抑える。
        /// </summary>
        private const int SessionToolLimit = 12;

        /// <summary>セッション明細に載せる未分類コマンドの上限件数。</summary>
        private const int SessionUnknownLimit = 5;

        /// <summary>
        /// 走査結果からレポートを組み立てる。
        /// </summary>
        /// <param name="scan">走査結果</param>
        /// <param name="includePrompt">指示文を出力しているか</param>
        /// <param name="appVersion">生成したアプリのバージョン</param>
        /// <param name="cacheWasCorrupted">キャッシュ破損により全件再パースしたか</param>
        /// <param name="nowLocal">現在時刻（ローカル）。期間の起点に使う</param>
        /// <returns>HTML へ埋め込むレポート</returns>
        public static UsageReport Build(
            ScanResult scan,
            bool includePrompt,
            string appVersion,
            bool cacheWasCorrupted,
            DateTime nowLocal,
            ReportAssumptions? assumptions = null)
        {
            var sessions = scan.Sessions;
            var cumulative = BuildCumulative(sessions);

            var report = new UsageReport
            {
                GeneratedAt = nowLocal.ToString("yyyy-MM-dd HH:mm"),
                AppVersion = appVersion,
                IncludePrompt = includePrompt,
                Quality = BuildQuality(scan, cacheWasCorrupted),
                Sessions = sessions
                    .OrderByDescending(s => s.StartUtc)
                    .Select(s => ToView(s, cumulative))
                    .ToList(),
            };

            foreach (var (key, label, days) in Periods)
            {
                var from = days == 0 ? DateTime.MinValue : nowLocal.Date.AddDays(-(days - 1));
                report.Periods[key] = BuildPeriod(label, sessions, from, DateTime.MaxValue);
            }

            AddMonthlyPeriods(report, sessions);
            report.Impact = BuildImpactAssumptions(assumptions ?? ReportAssumptions.Default);
            return report;
        }

        /// <summary>
        /// 履歴に現れる月を新しい順に期間として追加する。
        ///
        /// 履歴は各ツールの保持期間で消えるため、過去の月を後から見返す用途がある。
        /// キーは「month:yyyy-MM」。存在しない月は作らない（空の選択肢を並べない）。
        /// </summary>
        /// <param name="report">追加先のレポート</param>
        /// <param name="sessions">全セッション</param>
        private static void AddMonthlyPeriods(
            UsageReport report, IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var months = sessions
                .Select(s => s.StartUtc.ToLocalTime())
                .Select(d => new DateTime(d.Year, d.Month, 1))
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            foreach (var month in months)
            {
                var label = $"{month.Year} 年 {month.Month} 月";
                report.Periods[$"month:{month:yyyy-MM}"] =
                    BuildPeriod(label, sessions, month, month.AddMonths(1));
            }
        }

        /// <summary>金額計算の前提値を画面用へ写す（期間に依存しない）。</summary>
        /// <param name="assumptions">設定から渡された前提値</param>
        /// <returns>効果・稼働タブの前提ブロック</returns>
        private static ImpactView BuildImpactAssumptions(ReportAssumptions assumptions)
            => new()
            {
                Assumptions = new AssumptionView
                {
                    HourlyRateJpy = assumptions.HourlyRateJpy,
                    ReductionFactor = assumptions.ReductionFactor,
                    UsdJpyRate = assumptions.UsdJpyRate,
                    SlotMinutes = SessionStatsRecord.ActiveSlotMinutes,
                    CoefficientReferenceDate = ModelPricing.CoefficientReferenceDate,
                    CoefficientSourceUrl = ModelPricing.CoefficientSourceUrl,
                },
            };

        /// <summary>
        /// 期間 1 つ分の稼働・効果データを作る。
        ///
        /// 稼働時間は 5 分枠の数から求める。セッションの経過時間（開始〜終了の差）は
        /// 離席・レビュー待ちを含むため使わない（設計書 7.2 / 7.2.1）。
        /// 枠はセッションをまたいで重複排除するので、サブエージェントを並列に走らせても
        /// 同じ時刻が二重計上されることはない。
        ///
        /// 枠は全セッションから「枠の日付が期間内か」で絞る。セッションの開始日で絞ると、
        /// 日をまたいだセッションの枠が期間の境界で丸ごと落ちたり入ったりする。
        /// 一方でトークンとモデル別費用はセッション基準（開始日）で、他の指標と揃える。
        /// </summary>
        /// <param name="allSessions">全セッション（枠の抽出に使う）</param>
        /// <param name="inPeriod">開始日が期間内のセッション（トークン系の集計に使う）</param>
        /// <param name="fromLocal">期間の開始日（ローカル）。MinValue なら下限なし</param>
        /// <param name="toLocalExclusive">期間の終端（この日を含まない）。MaxValue なら上限なし</param>
        /// <returns>期間 1 つ分の稼働・効果データ</returns>
        private static PeriodImpactView BuildPeriodImpact(
            IReadOnlyCollection<SessionStatsRecord> allSessions,
            IReadOnlyCollection<SessionStatsRecord> inPeriod,
            DateTime fromLocal,
            DateTime toLocalExclusive)
        {
            var slotsByDay = MergeActiveSlots(allSessions, fromLocal, toLocalExclusive);

            var impact = new PeriodImpactView
            {
                ActiveByDay = slotsByDay
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new DayMinutesView
                    {
                        Date = kv.Key,
                        Minutes = kv.Value.Count * SessionStatsRecord.ActiveSlotMinutes,
                    })
                    .ToList(),
                Adoption = BuildAdoption(slotsByDay),
                Subagent = BuildSubagent(inPeriod),
                ModelCost = BuildModelCost(inPeriod),
            };
            impact.TotalActiveMinutes = impact.ActiveByDay.Sum(d => (long)d.Minutes);
            impact.Hourly = BuildHourly(slotsByDay, out var weekdayCount);
            impact.HourlyWeekdayCount = weekdayCount;
            return impact;
        }

        /// <summary>
        /// 全セッションの稼働枠を日付ごとに重ね合わせる。
        /// 同じ枠に複数のセッションが居ても 1 回しか数えない。
        /// </summary>
        /// <param name="sessions">全セッション</param>
        /// <param name="fromLocal">この日より前の枠は捨てる。MinValue なら下限なし</param>
        /// <param name="toLocalExclusive">この日以降の枠は捨てる。MaxValue なら上限なし</param>
        /// <returns>ローカル日付 → 稼働していた枠番号の集合</returns>
        private static Dictionary<string, HashSet<int>> MergeActiveSlots(
            IReadOnlyCollection<SessionStatsRecord> sessions,
            DateTime fromLocal,
            DateTime toLocalExclusive)
        {
            var slotsByDay = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var session in sessions)
            {
                foreach (var (day, slots) in session.ActiveSlots)
                {
                    if (!TryParseDay(day, out var date))
                    {
                        continue;
                    }
                    if (date < fromLocal.Date ||
                        (toLocalExclusive != DateTime.MaxValue && date >= toLocalExclusive.Date))
                    {
                        continue;
                    }
                    if (!slotsByDay.TryGetValue(day, out var set))
                    {
                        set = new HashSet<int>();
                        slotsByDay[day] = set;
                    }
                    set.UnionWith(slots);
                }
            }
            return slotsByDay;
        }

        /// <summary>
        /// 時間帯別の稼働率を求める。
        ///
        /// 母数は「期間内で実際に稼働した平日」。使わなかった日まで分母へ入れると
        /// 「使う日は何時に使うか」という問いが薄まる。使った日の割合は定着率で別に出す。
        /// </summary>
        /// <param name="slotsByDay">日付ごとの稼働枠（期間で絞り込み済み）</param>
        /// <param name="weekdayCount">母数になった平日の日数</param>
        /// <returns>24 件の稼働率</returns>
        private static List<HourRateView> BuildHourly(
            Dictionary<string, HashSet<int>> slotsByDay, out int weekdayCount)
        {
            var target = new List<HashSet<int>>();
            foreach (var (day, slots) in slotsByDay)
            {
                if (!TryParseDay(day, out var date))
                {
                    continue;
                }
                if (!IsBusinessDay(date))
                {
                    continue;
                }
                target.Add(slots);
            }
            weekdayCount = target.Count;

            var denominator = target.Count * SessionStatsRecord.ActiveSlotsPerHour;
            var hourly = new List<HourRateView>(24);
            for (var hour = 0; hour < 24; hour++)
            {
                var lo = hour * SessionStatsRecord.ActiveSlotsPerHour;
                var hi = lo + SessionStatsRecord.ActiveSlotsPerHour;
                var active = target.Sum(slots => slots.Count(slot => slot >= lo && slot < hi));
                hourly.Add(new HourRateView
                {
                    Hour = hour,
                    ActiveSlots = active,
                    Rate = denominator == 0 ? 0d : Math.Round((double)active / denominator * 100d, 1),
                });
            }
            return hourly;
        }

        /// <summary>
        /// 定着率を求める。母数は初回稼働日から最終稼働日までの営業日。
        /// 土日を除くだけで祝日は考慮しない（社内カレンダーを持ち込まない）。
        /// 連続日数も営業日で数え、土日は切れ目にしない。
        /// </summary>
        /// <param name="slotsByDay">日付ごとの稼働枠</param>
        /// <returns>定着率</returns>
        private static AdoptionView BuildAdoption(Dictionary<string, HashSet<int>> slotsByDay)
        {
            var days = new SortedSet<DateTime>();
            foreach (var day in slotsByDay.Keys)
            {
                if (TryParseDay(day, out var date))
                {
                    days.Add(date);
                }
            }
            if (days.Count == 0)
            {
                return new AdoptionView();
            }

            var first = days.Min;
            var last = days.Max;
            var businessDays = 0;
            var used = 0;
            var streak = 0;
            var best = 0;
            for (var cur = first; cur <= last; cur = cur.AddDays(1))
            {
                if (!IsBusinessDay(cur))
                {
                    continue;
                }
                businessDays++;
                if (days.Contains(cur))
                {
                    used++;
                    streak++;
                    best = Math.Max(best, streak);
                }
                else
                {
                    streak = 0;
                }
            }

            // 現在の連続は、最終稼働日から営業日を遡って数える
            var current = 0;
            for (var cur = last; cur >= first; cur = cur.AddDays(-1))
            {
                if (!IsBusinessDay(cur))
                {
                    continue;
                }
                if (!days.Contains(cur))
                {
                    break;
                }
                current++;
            }

            return new AdoptionView
            {
                FirstDay = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LastDay = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                BusinessDays = businessDays,
                UsedBusinessDays = used,
                ActiveDays = days.Count,
                BestStreak = best,
                CurrentStreak = current,
            };
        }

        /// <summary>
        /// サブエージェントの内訳を作る。トークンは単独値を使う。
        /// 累計値（配下を含む値）を足すと二重計上になる（設計書 5.4）。
        /// </summary>
        /// <param name="sessions">全セッション</param>
        /// <returns>サブエージェントの内訳</returns>
        private static SubagentView BuildSubagent(IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var view = new SubagentView();
            foreach (var session in sessions)
            {
                var billable = session.Tokens.Billable(session.Tool);
                if (session.IsSubagent)
                {
                    view.Subagents++;
                    view.SubagentBillable += billable;
                }
                else
                {
                    view.Parents++;
                    view.ParentBillable += billable;
                }
            }
            return view;
        }

        /// <summary>
        /// モデル別の「従量課金だった場合の費用」を作る。
        ///
        /// 単価表に無いモデルと Codex は CostAccumulator が換算から外すため USD は 0 になる。
        /// ターン数と課金相当トークンは除外分も出す（量を隠さない。設計書 7.1.1）。
        /// </summary>
        /// <param name="sessions">全セッション</param>
        /// <returns>課金相当トークンの多い順に並べたモデル別の行</returns>
        private static List<ModelCostView> BuildModelCost(
            IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var turns = new Dictionary<string, int>(StringComparer.Ordinal);
            var tokens = new Dictionary<string, long>(StringComparer.Ordinal);
            var accumulator = new CostAccumulator();

            foreach (var session in sessions)
            {
                foreach (var model in session.ByModel)
                {
                    var name = string.IsNullOrWhiteSpace(model.Model) ? "(モデル不明)" : model.Model;
                    turns[name] = turns.GetValueOrDefault(name) + model.Turns;
                    tokens[name] = tokens.GetValueOrDefault(name) + model.Tokens.Billable(session.Tool);
                    accumulator.Add(session.Tool, model.Model, model.Tokens);
                }
            }

            var usdByModel = accumulator.Build(withPrices: true).UsdByModel;
            return tokens
                .Select(kv => new ModelCostView
                {
                    Model = kv.Key,
                    Turns = turns.GetValueOrDefault(kv.Key),
                    Billable = kv.Value,
                    Usd = usdByModel.GetValueOrDefault(kv.Key),
                })
                .OrderByDescending(v => v.Billable)
                .ToList();
        }

        /// <summary>yyyy-MM-dd をローカル日付として解釈する。</summary>
        /// <param name="text">日付文字列</param>
        /// <param name="date">解釈できた日付</param>
        /// <returns>解釈できたか</returns>
        private static bool TryParseDay(string text, out DateTime date)
            => DateTime.TryParseExact(
                text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        /// <summary>営業日か。土日を除くだけで祝日は考慮しない。</summary>
        /// <param name="date">判定する日</param>
        /// <returns>営業日なら true</returns>
        private static bool IsBusinessDay(DateTime date)
            => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        /// <summary>
        /// cursor-tokens.json の日別集計を各 PeriodView.ByDay の CursorBillable へ流し込む。
        /// Cursor のみの日も棒グラフに出るよう、該当日が無ければ DayView を追加する。
        /// </summary>
        /// <param name="report">注入先レポート</param>
        /// <param name="store">Cursor トークン蓄積</param>
        /// <param name="nowLocal">現在時刻（ローカル）。固定期間の起点</param>
        public static void ApplyCursorDailyTokens(
            UsageReport report,
            Models.CursorTokenStore store,
            DateTime nowLocal)
        {
            if (store.Daily.Count == 0) return;

            foreach (var (key, _, days) in Periods)
            {
                if (!report.Periods.TryGetValue(key, out var period)) continue;
                var from = days == 0 ? DateTime.MinValue : nowLocal.Date.AddDays(-(days - 1));
                ApplyCursorDailyToPeriod(period, store, from, DateTime.MaxValue);
            }

            foreach (var (key, period) in report.Periods)
            {
                if (!key.StartsWith("month:", StringComparison.Ordinal)) continue;
                var ym = key["month:".Length..];
                if (!DateTime.TryParse(ym + "-01", out var monthStart)) continue;
                ApplyCursorDailyToPeriod(period, store, monthStart, monthStart.AddMonths(1));
            }
        }

        /// <summary>1 期間の ByDay に Cursor 日別トークンを載せる。</summary>
        private static void ApplyCursorDailyToPeriod(
            PeriodView period,
            Models.CursorTokenStore store,
            DateTime fromLocal,
            DateTime toLocalExclusive)
        {
            var map = period.ByDay.ToDictionary(d => d.Date, StringComparer.Ordinal);
            foreach (var (date, tokens) in store.Daily)
            {
                if (!DateTime.TryParse(date, out var dt)) continue;
                if (dt.Date < fromLocal.Date) continue;
                if (toLocalExclusive != DateTime.MaxValue && dt.Date >= toLocalExclusive.Date) continue;

                if (!map.TryGetValue(date, out var day))
                {
                    day = new DayView { Date = date };
                    map[date] = day;
                }
                day.CursorBillable = tokens.ChartTokens;
            }
            period.ByDay = map.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 親子を辿って累計値（自身 + 配下の全サブエージェント）を求める（設計書 5.4）。
        /// プロジェクト別の集計には単独値を使う。累計値を足すと二重計上になる。
        /// </summary>
        /// <param name="sessions">対象セッション</param>
        private static Dictionary<string, (long Billable, long Processed, int Count)> BuildCumulative(
            IReadOnlyCollection<SessionStatsRecord> sessions)
        {
            var children = new Dictionary<string, List<SessionStatsRecord>>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                if (string.IsNullOrEmpty(s.ParentKey))
                {
                    continue;
                }
                if (!children.TryGetValue(s.ParentKey, out var list))
                {
                    list = new List<SessionStatsRecord>();
                    children[s.ParentKey] = list;
                }
                list.Add(s);
            }

            var result = new Dictionary<string, (long, long, int)>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                long billable = s.Tokens.Billable(s.Tool);
                long processed = s.Tokens.Processed(s.Tool);
                var count = 0;

                // 幅優先で配下を辿る。循環は SessionNormalizer が事前に断ち切っている
                var queue = new Queue<string>();
                queue.Enqueue(s.Key);
                var visited = new HashSet<string>(StringComparer.Ordinal) { s.Key };
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!children.TryGetValue(current, out var list))
                    {
                        continue;
                    }
                    foreach (var child in list)
                    {
                        if (!visited.Add(child.Key))
                        {
                            continue;
                        }
                        billable += child.Tokens.Billable(child.Tool);
                        processed += child.Tokens.Processed(child.Tool);
                        count++;
                        queue.Enqueue(child.Key);
                    }
                }
                result[s.Key] = (billable, processed, count);
            }
            return result;
        }

        /// <summary>セッションレコードを画面用の行へ変換する。</summary>
        /// <param name="s">セッションレコード</param>
        /// <param name="cumulative">累計値の索引</param>
        private static SessionView ToView(
            SessionStatsRecord s,
            IReadOnlyDictionary<string, (long Billable, long Processed, int Count)> cumulative)
        {
            var local = s.StartUtc.ToLocalTime();
            var cum = cumulative.TryGetValue(s.Key, out var c) ? c : (0L, 0L, 0);
            return new SessionView
            {
                Key = s.Key,
                Tool = s.Tool,
                ParentKey = s.ParentKey,
                IsSubagent = s.IsSubagent,
                AgentLabel = s.AgentLabel,
                Project = s.ProjectDisplay,
                Start = local.ToString("yyyy-MM-dd HH:mm"),
                StartDay = local.ToString("yyyy-MM-dd"),
                ElapsedSec = s.ElapsedSec,
                ElapsedMeasurable = s.ElapsedMeasurable,
                LastLineBroken = s.LastLineBroken,
                Prompt = s.FirstPrompt,
                Turns = s.Turns,
                Model = s.ByModel.Count > 0 ? s.ByModel[0].Model : SessionNormalizer.UnknownModel,
                Billable = s.Tokens.Billable(s.Tool),
                Processed = s.Tokens.Processed(s.Tool),
                BillableCumulative = cum.Item1,
                ProcessedCumulative = cum.Item2,
                SubagentCount = cum.Item3,
                Phases = s.PhaseCounts,

                End = s.EndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                RootId = s.RootId,
                Tokens = new TokenDetailView
                {
                    Input = s.Tokens.Input,
                    Output = s.Tokens.Output,
                    CacheCreate = s.Tokens.CacheCreate,
                    CacheRead = s.Tokens.CacheRead,
                    Reasoning = s.Tokens.Reasoning,
                },
                Cost = BuildSessionCost(s),
                ByModel = s.ByModel
                    .Select(m => new SessionModelView
                    {
                        Model = m.Model,
                        Turns = m.Turns,
                        Billable = m.Tokens.Billable(s.Tool),
                        Processed = m.Tokens.Processed(s.Tool),
                    })
                    .ToList(),
                ByDay = s.ByDay
                    .Select(d => new SessionDayView
                    {
                        Date = d.DateLocal,
                        Turns = d.Turns,
                        Billable = d.Tokens.Billable(s.Tool),
                    })
                    .ToList(),
                Tools = s.ToolCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(SessionToolLimit)
                    .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                    .ToList(),
                UnknownCommands = s.UnknownCommands
                    .OrderByDescending(kv => kv.Value)
                    .Take(SessionUnknownLimit)
                    .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                    .ToList(),
                RawRecords = s.Dedup.RawRecords,
                AcceptedRecords = s.Dedup.AcceptedRecords,
                KeylessRecords = s.Dedup.KeylessRecords,
                ParseErrors = s.ParseErrors,
            };
        }

        /// <summary>期間 1 つ分を集計する。</summary>
        /// <param name="label">期間名</param>
        /// <param name="sessions">全セッション</param>
        /// <param name="fromLocal">期間の開始日（ローカル）。MinValue なら全期間</param>
        /// <param name="toLocalExclusive">期間の終端（ローカル・この日を含まない）。MaxValue なら上限なし</param>
        private static PeriodView BuildPeriod(
            string label,
            IReadOnlyCollection<SessionStatsRecord> sessions,
            DateTime fromLocal,
            DateTime toLocalExclusive)
        {
            var view = new PeriodView { Label = label };

            // セッション基準: 開始時刻が期間内のもの（設計書 7.4）
            var inPeriod = sessions
                .Where(s =>
                {
                    var day = s.StartUtc.ToLocalTime().Date;
                    return day >= fromLocal.Date &&
                           (toLocalExclusive == DateTime.MaxValue || day < toLocalExclusive.Date);
                })
                .ToList();

            var projects = new Dictionary<string, ProjectView>(StringComparer.Ordinal);
            var models = new Dictionary<string, ModelView>(StringComparer.Ordinal);
            var tools = new Dictionary<string, int>(StringComparer.Ordinal);
            var phases = new Dictionary<string, int>(StringComparer.Ordinal);
            var unknownCommands = new Dictionary<string, int>(StringComparer.Ordinal);
            var cost = new CostAccumulator();

            foreach (var s in inPeriod)
            {
                var billable = s.Tokens.Billable(s.Tool);
                var processed = s.Tokens.Processed(s.Tool);

                view.SessionCount++;
                view.TurnCount += s.Turns;
                view.Billable += billable;
                view.Processed += processed;
                view.CacheRead += s.Tokens.CacheRead;
                view.Reasoning += s.Tokens.Reasoning;
                view.SessionKeys.Add(s.Key);

                if (s.ElapsedMeasurable)
                {
                    view.ElapsedSec += s.ElapsedSec;
                }
                else
                {
                    view.ElapsedUnmeasurableCount++;
                }

                if (!projects.TryGetValue(s.ProjectKey, out var project))
                {
                    project = new ProjectView { Project = s.ProjectDisplay };
                    projects[s.ProjectKey] = project;
                }
                project.SessionCount++;
                project.Billable += billable;
                project.Processed += processed;
                if (s.ElapsedMeasurable)
                {
                    project.ElapsedSec += s.ElapsedSec;
                }
                Merge(project.Phases, s.PhaseCounts);

                foreach (var m in s.ByModel)
                {
                    if (!models.TryGetValue(m.Model, out var model))
                    {
                        model = new ModelView { Model = m.Model };
                        models[m.Model] = model;
                    }
                    model.Billable += m.Tokens.Billable(s.Tool);
                    model.Processed += m.Tokens.Processed(s.Tool);
                    model.Turns += m.Turns;

                    // 単価はモデルごとに違うため、コスト換算もモデル別内訳から積む
                    cost.Add(s.Tool, m.Model, m.Tokens);
                }

                Merge(tools, s.ToolCounts);
                Merge(phases, s.PhaseCounts);
                Merge(unknownCommands, s.UnknownCommands);
            }

            // 日別はターン基準。期間内の日付に属する内訳をすべて拾う（設計書 7.4 / C9）
            var days = new Dictionary<string, DayView>(StringComparer.Ordinal);
            foreach (var s in sessions)
            {
                foreach (var d in s.ByDay)
                {
                    if (!DateTime.TryParse(d.DateLocal, out var date) || date.Date < fromLocal.Date)
                    {
                        continue;
                    }
                    if (toLocalExclusive != DateTime.MaxValue && date.Date >= toLocalExclusive.Date)
                    {
                        continue;
                    }
                    if (!days.TryGetValue(d.DateLocal, out var day))
                    {
                        day = new DayView { Date = d.DateLocal };
                        days[d.DateLocal] = day;
                    }
                    var billable = d.Tokens.Billable(s.Tool);
                    switch (s.Tool)
                    {
                        case SessionStatsRecord.ToolClaudeCode:
                            day.ClaudeBillable += billable;
                            break;
                        case SessionStatsRecord.ToolCowork:
                            day.CoworkBillable += billable;
                            break;
                        case SessionStatsRecord.ToolCodex:
                            day.CodexBillable += billable;
                            break;
                        case SessionStatsRecord.ToolCursor:
                            day.CursorBillable += billable;
                            break;
                    }
                    day.Turns += d.Turns;
                }
            }

            view.ProjectCount = projects.Count;
            view.ByProject = projects.Values.OrderByDescending(p => p.Billable).ToList();
            view.ByModel = models.Values.OrderByDescending(m => m.Billable).ToList();
            view.Cost = cost.Build(withPrices: true);
            view.ByPhase = phases;
            view.ByTool = tools
                .OrderByDescending(kv => kv.Value)
                .Take(TopCountLimit)
                .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                .ToList();
            view.UnknownCommands = unknownCommands
                .OrderByDescending(kv => kv.Value)
                .Take(TopCountLimit)
                .Select(kv => new CountView { Name = kv.Key, Count = kv.Value })
                .ToList();
            view.ByDay = days.Values.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
            view.Impact = BuildPeriodImpact(sessions, inPeriod, fromLocal, toLocalExclusive);

            var totalPhase = phases.Values.Sum();
            var unknown = phases.GetValueOrDefault(PhaseClassifier.Unknown);
            view.ClassifiedPercent = totalPhase == 0
                ? 0
                : Math.Round((double)(totalPhase - unknown) * 100 / totalPhase, 1);

            return view;
        }

        /// <summary>データ品質タブ用の情報を組み立てる。</summary>
        /// <param name="scan">走査結果</param>
        /// <param name="cacheWasCorrupted">キャッシュ破損により全件再パースしたか</param>
        private static QualityInfo BuildQuality(ScanResult scan, bool cacheWasCorrupted)
        {
            var claude = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolClaudeCode).ToList();
            var cowork = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCowork).ToList();
            var codex = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCodex).ToList();
            var cursor = scan.Sessions.Where(s => s.Tool == SessionStatsRecord.ToolCursor).ToList();
            var coworkFiles = scan.Files
                .Where(f => IsUnderRoot(f.Path, scan.CoworkRootPath))
                .ToList();
            var withTime = scan.Sessions.Where(s => s.StartUtc > DateTime.UnixEpoch).ToList();

            return new QualityInfo
            {
                ClaudeStatus = StatusText(scan.ClaudeStatus),
                CoworkStatus = StatusText(scan.CoworkStatus),
                CodexStatus = StatusText(scan.CodexStatus),
                CursorStatus = StatusText(scan.CursorStatus),
                ClaudeRoot = scan.ClaudeRootPath,
                CoworkRoot = scan.CoworkRootPath,
                CodexRoot = scan.CodexRootPath,
                CursorRoot = scan.CursorRootPath,
                ParsedFiles = scan.ParsedFileCount,
                ReusedFiles = scan.ReusedFileCount,
                JournalFiles = scan.JournalFileCount,
                ScanElapsedMs = scan.ElapsedMs,
                SkippedFiles = scan.Files
                    .Where(f => f.SkipReason is not null)
                    .Select(f => new SkippedFileView { Path = f.Path, Reason = f.SkipReason! })
                    .ToList(),
                ClaudeRawRecords = claude.Sum(s => s.Dedup.RawRecords),
                ClaudeAcceptedRecords = claude.Sum(s => s.Dedup.AcceptedRecords),
                ClaudeKeylessRecords = claude.Sum(s => s.Dedup.KeylessRecords),
                CoworkRawRecords = cowork.Sum(s => s.Dedup.RawRecords),
                CoworkAcceptedRecords = cowork.Sum(s => s.Dedup.AcceptedRecords),
                CoworkKeylessRecords = cowork.Sum(s => s.Dedup.KeylessRecords),
                CoworkFiles = coworkFiles.Count,
                CoworkBytes = coworkFiles.Sum(f => f.SizeBytes),
                CoworkIdentityMissingSessions = cowork.Count(s => s.SourceIdentityMissing),
                CoworkKeyCollisions = cowork.Count(s => s.KeyCollision),
                CodexThreads = codex.Count,
                CodexSubagents = codex.Count(s => s.IsSubagent),
                CursorSessions = cursor.Count,
                CursorSubagents = cursor.Count(s => s.IsSubagent),
                ParseErrors = scan.Sessions.Sum(s => s.ParseErrors),
                LastLineBrokenSessions = scan.Sessions.Count(s => s.LastLineBroken),
                UnmeasurableSessions = scan.Sessions.Count(s => !s.ElapsedMeasurable),
                CacheWasCorrupted = cacheWasCorrupted,
                OldestRecord = withTime.Count == 0
                    ? "-"
                    : withTime.Min(s => s.StartUtc).ToLocalTime().ToString("yyyy-MM-dd"),
                NewestRecord = withTime.Count == 0
                    ? "-"
                    : withTime.Max(s => s.EndUtc).ToLocalTime().ToString("yyyy-MM-dd"),
            };
        }

        /// <summary>読み取り状態を画面用の文字列へ変換する。</summary>
        /// <param name="status">読み取り状態</param>
        private static string StatusText(ToolScanStatus status) => status switch
        {
            ToolScanStatus.Ok => "ok",
            ToolScanStatus.Unreadable => "unreadable",
            ToolScanStatus.Partial => "partial",
            ToolScanStatus.Unsupported => "unsupported",
            _ => "notUsed",
        };

        /// <summary>ファイルが指定ルート配下かを判定する。</summary>
        private static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }
            var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// セッション 1 件分のコスト換算を作る。
        /// 単価はモデルごとに違うため、セッション合計ではなくモデル別内訳から積む。
        /// 単価の一覧は含めない（同じ表がセッションの数だけ JSON に載るのを避ける）。
        /// </summary>
        /// <param name="record">対象のセッション</param>
        /// <returns>コスト換算の内訳</returns>
        private static CostView BuildSessionCost(SessionStatsRecord record)
        {
            var acc = new CostAccumulator();
            foreach (var m in record.ByModel)
            {
                acc.Add(record.Tool, m.Model, m.Tokens);
            }
            return acc.Build();
        }

        /// <summary>件数辞書を加算合成する。</summary>
        /// <param name="target">加算先</param>
        /// <param name="source">加算元</param>
        private static void Merge(Dictionary<string, int> target, IReadOnlyDictionary<string, int> source)
        {
            foreach (var kv in source)
            {
                target[kv.Key] = target.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }
    }
}
