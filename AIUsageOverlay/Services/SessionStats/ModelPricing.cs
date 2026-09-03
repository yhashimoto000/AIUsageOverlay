using AIUsageOverlay.Models;

namespace AIUsageOverlay.Services.SessionStats
{
    /// <summary>
    /// モデル 1 種類分の単価。金額そのものは表示しないが、
    /// 種別（入力 / 出力 / キャッシュ）ごとの重みを正しく比較するために絶対単価で持つ。
    ///
    /// 「入力単価に対する倍率」だけで持つと、単価の異なるモデル（Opus と Sonnet 等）が
    /// 混ざったときに構成比が歪む。実際の履歴は複数モデルが混在するため、絶対単価が必要。
    /// </summary>
    /// <param name="InputPerMillion">入力 100 万トークンあたりの USD。</param>
    /// <param name="OutputPerMillion">出力 100 万トークンあたりの USD。</param>
    /// <param name="CacheReadRatio">キャッシュ読込の単価。入力単価に対する倍率で持つ。</param>
    public sealed record ModelPrice(
        decimal InputPerMillion,
        decimal OutputPerMillion,
        decimal CacheReadRatio);

    /// <summary>
    /// モデル名から単価を引く表。
    ///
    /// 単価が判明しているモデルだけを載せる。表に無いモデルはコスト換算から除外し、
    /// 除外した量をレポートに明示する。既定値でのフォールバックはしない
    /// （新しいモデルが出たとき、静かに誤った構成比を出すのを避けるため）。
    /// 新モデルへ対応するときはこの表に 1 行足すだけでよい。
    /// </summary>
    public static class ModelPricing
    {
        /// <summary>キャッシュ書き込み（5 分 TTL）の単価。入力単価に対する倍率。</summary>
        public const decimal CacheWrite5mRatio = 1.25m;

        /// <summary>キャッシュ書き込み（1 時間 TTL）の単価。入力単価に対する倍率。</summary>
        public const decimal CacheWrite1hRatio = 2.00m;

        /// <summary>キャッシュ読込の既定単価。入力単価に対する倍率。</summary>
        public const decimal DefaultCacheReadRatio = 0.10m;

        /// <summary>
        /// 単価表。キーはモデル名の接頭辞で、日付サフィックス付きの名前
        /// （claude-haiku-4-5-20251001 等）にも当たるようにする。
        /// 長い接頭辞から先に照合するため、claude-fable-5-1 と claude-fable-5 は取り違えない。
        ///
        /// claude-mythos-5-1 は載せない。キャッシュ読込の単価が確定していないため、
        /// 推測で載せるより除外として明示するほうが正しい。
        /// </summary>
        private static readonly (string Prefix, ModelPrice Price)[] Table =
        {
            ("claude-fable-5-1",  new ModelPrice(10m, 50m, 0.025m)),
            ("claude-fable-5",    new ModelPrice(10m, 50m, DefaultCacheReadRatio)),
            ("claude-opus-5",     new ModelPrice( 5m, 25m, DefaultCacheReadRatio)),
            ("claude-opus-4-8",   new ModelPrice( 5m, 25m, DefaultCacheReadRatio)),
            ("claude-opus-4-7",   new ModelPrice( 5m, 25m, DefaultCacheReadRatio)),
            ("claude-opus-4-6",   new ModelPrice( 5m, 25m, DefaultCacheReadRatio)),
            ("claude-sonnet-5",   new ModelPrice( 2m, 10m, DefaultCacheReadRatio)),
            ("claude-sonnet-4-6", new ModelPrice( 3m, 15m, DefaultCacheReadRatio)),
            ("claude-haiku-4-5",  new ModelPrice( 1m,  5m, DefaultCacheReadRatio)),
        };

        /// <summary>
        /// モデル名から単価を引く。表に無ければ null を返す（呼び出し側で除外する）。
        /// </summary>
        /// <param name="model">履歴に記録されていたモデル名</param>
        /// <returns>単価。判明しない場合は null</returns>
        public static ModelPrice? Lookup(string? model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            // 長い接頭辞を優先する。表の順序に依存させないため、都度長さで並べ替える。
            foreach (var (prefix, price) in Table.OrderByDescending(e => e.Prefix.Length))
            {
                if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return price;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// 単価で重み付けしたコストを溜め込み、構成比へ変換する。
    ///
    /// トークン数の比では、キャッシュ読込（入力の 0.1 倍）が実態より大きく見える。
    /// 逆に出力（入力の 5 倍）は小さく見える。何にコストが偏っているかを見るには
    /// 単価で重み付けした構成比が要る。
    /// </summary>
    internal sealed class CostAccumulator
    {
        // 単価を掛けた後の相対コスト。金額として表示はせず、構成比の計算だけに使う。
        private decimal _input;
        private decimal _write5m;
        private decimal _write1h;
        private decimal _writeUnknownTtl;
        private decimal _read;
        private decimal _output;

        /// <summary>換算に含めたトークン数（課金相当）。</summary>
        private long _coveredTokens;

        /// <summary>単価不明で除外したトークン数（課金相当）。</summary>
        private long _excludedTokens;

        /// <summary>除外したモデル名。レポートに列挙して、除外を隠さない。</summary>
        private readonly SortedSet<string> _excludedModels = new(StringComparer.Ordinal);

        /// <summary>
        /// モデル 1 種類分の内訳を加算する。
        ///
        /// 対象は Claude Code のみ。Codex はキャッシュ書込量が常に 0 で記録され、
        /// TTL 別の区分も無く、モデル名（gpt-5.6-sol 等）が公開単価表と対応しないため、
        /// 換算対象から外して除外量に計上する。
        /// </summary>
        /// <param name="tool">SessionStatsRecord.ToolClaudeCode または ToolCodex</param>
        /// <param name="model">モデル名</param>
        /// <param name="tokens">そのモデルのトークン内訳</param>
        public void Add(string tool, string model, TokenTotals tokens)
        {
            var price = tool == SessionStatsRecord.ToolClaudeCode
                ? ModelPricing.Lookup(model)
                : null;

            if (price is null)
            {
                var billable = tokens.Billable(tool);
                if (billable > 0)
                {
                    _excludedTokens += billable;
                    _excludedModels.Add(string.IsNullOrWhiteSpace(model) ? "(モデル不明)" : model);
                }
                return;
            }

            _coveredTokens += tokens.Billable(tool);

            // TTL の内訳が取れなかった分。古い履歴には usage.cache_creation が無い。
            // 既定 TTL は 5 分なのでその単価で見積もるが、量は別に持って画面へ出す。
            var unknownTtl = Math.Max(
                0, tokens.CacheCreate - tokens.CacheCreate5m - tokens.CacheCreate1h);

            var inputUnit = price.InputPerMillion;

            _input           += Weight(tokens.Input, inputUnit);
            _write5m         += Weight(tokens.CacheCreate5m, inputUnit * ModelPricing.CacheWrite5mRatio);
            _write1h         += Weight(tokens.CacheCreate1h, inputUnit * ModelPricing.CacheWrite1hRatio);
            _writeUnknownTtl += Weight(unknownTtl, inputUnit * ModelPricing.CacheWrite5mRatio);
            _read            += Weight(tokens.CacheRead, inputUnit * price.CacheReadRatio);
            _output          += Weight(tokens.Output, price.OutputPerMillion);
        }

        /// <summary>トークン数に 100 万トークンあたりの単価を掛ける。</summary>
        /// <param name="tokens">トークン数</param>
        /// <param name="perMillion">100 万トークンあたりの単価</param>
        private static decimal Weight(long tokens, decimal perMillion)
            => tokens <= 0 ? 0m : tokens / 1_000_000m * perMillion;

        /// <summary>溜め込んだ結果を構成比へ変換する。</summary>
        /// <returns>画面へ渡すコスト換算の内訳</returns>
        public CostView Build()
        {
            var total = _input + _write5m + _write1h + _writeUnknownTtl + _read + _output;
            var view = new CostView
            {
                CoveredTokens = _coveredTokens,
                ExcludedTokens = _excludedTokens,
                ExcludedModels = _excludedModels.ToList(),
                Available = total > 0m,
            };
            if (!view.Available)
            {
                return view;
            }

            view.InputPercent           = Percent(_input, total);
            view.CacheWrite5mPercent    = Percent(_write5m, total);
            view.CacheWrite1hPercent    = Percent(_write1h, total);
            view.CacheWriteUnknownPercent = Percent(_writeUnknownTtl, total);
            view.CacheReadPercent       = Percent(_read, total);
            view.OutputPercent          = Percent(_output, total);
            return view;
        }

        /// <summary>構成比（0〜100）を小数第 1 位まで求める。</summary>
        /// <param name="part">対象の相対コスト</param>
        /// <param name="total">全体の相対コスト</param>
        private static double Percent(decimal part, decimal total)
            => total <= 0m ? 0d : Math.Round((double)(part / total) * 100d, 1);
    }
}
