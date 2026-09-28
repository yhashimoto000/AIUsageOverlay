# Cowork 使用量レポート対応 詳細設計書

| 項目 | 内容 |
|---|---|
| 版数 | Rev 1.0（実装反映） |
| 作成日 | 2026-09-24 |
| 対象 | AIUsageOverlay v2.4.0 以降の使用量レポート |
| 位置付け | Cowork ローカル履歴の取り込みと、金額を表示しない API 換算指標の実装仕様 |
| 閲覧用 HTML | `SPEC_Cowork_Usage_Report.html`。11 章のトリアージ表で対応可否を記録できる |

本 Markdown が正であり、HTML は生成物なので直接編集しない。

再生成:

```powershell
python %USERPROFILE%\.codex\skills\design-doc-authoring\scripts\gen_design_html.py docs\SPEC_Cowork_Usage_Report.md docs\SPEC_Cowork_Usage_Report.html
python docs\fix_layout_width.py docs\SPEC_Cowork_Usage_Report.html
python %USERPROFILE%\.codex\skills\design-doc-authoring\scripts\verify_design_html.py docs\SPEC_Cowork_Usage_Report.html --md=docs\SPEC_Cowork_Usage_Report.md
```

改訂履歴:

| Rev | 日付 | 内容 |
|---|---|---|
| 0.1 | 2026-09-17 | 実機保存形式・現行コード・Anthropic 公式仕様を調査し、初版を作成 |
| 1.0 | 2026-09-24 | 実装・自動テスト・文書同期を反映。Q1〜Q4 を設計既定案で確定し、限定走査・品質状態・キャッシュ規則を実装結果へ合わせた |

---

## 1. 目的とスコープ

### 1.1 目的

現行の使用量レポートは Claude Code / Codex / Cursor を集計するが、Claude Desktop の Cowork で消費した
トークンは含まれない。このため、同じ利用者が AI を使った実績のうち Cowork 分が欠け、日別・プロジェクト別・
モデル別の合計を過小に表示する。

また、現行レポートの「コスト換算」は API 公開単価で重み付けした**構成比**だけを表示する。
利用量全体を期間間で比較できる指標が無いため、「この Cowork 利用を API で行った場合にどの程度の重さか」を
金額を出さずに把握できない。

本改修は、Windows 上に残る Cowork のローカルセッションを第 4 のツールとして取り込み、
API 公開単価を係数にした無通貨の「API 換算ポイント」を追加する。

### 1.2 スコープ（S）

| # | 項目 | 内容 |
|---|---|---|
| S1 | Cowork ローカル履歴の取り込み | `%AppData%\Claude\local-agent-mode-sessions` 配下の Cowork ローカルセッションだけを読み取る |
| S2 | Cowork の独立表示 | Claude Code と混ぜず、日別・セッション・ツール内訳・データ品質で `Cowork` として表示する |
| S3 | 既存集計への統合 | 消費トークン、処理トークン、モデル、ツール実行、作業分類、プロジェクト、経過時間へ Cowork を加える |
| S4 | API 換算ポイント | API 公開単価を係数にした加算可能な無通貨指標を、期間・ツール・セッション単位で表示する |
| S5 | 換算対象率 | 単価不明モデルを除外し、ポイントの対象になった消費トークン割合と除外モデルを表示する |
| S6 | 増分キャッシュ | 既存の `(path, length, lastWriteTimeUtc)` 差分判定と履歴削除後の保持を Cowork にも適用する |
| S7 | 既存レポートとの互換 | 既存 3 ツールの数値・表示・キャッシュを変えず、Cowork が無い PC では現行と同じ結果にする |

### 1.3 非スコープ（N）

| # | 項目 | 理由・代替 |
|---|---|---|
| N1 | Cowork クラウドセッション | 公式仕様ではクラウドセッションは Anthropic 側に保存され、ローカルに同じ JSONL が残る保証がない。レポートには「ローカル Cowork のみ」と明示する |
| N2 | 実請求額・通貨表示 | Pro / Max / Team / Enterprise の定額利用料や契約割引と一致しない。`$`、`USD`、円換算、請求額という語は合計表示に使わない |
| N3 | Compliance API / OpenTelemetry 連携 | プラン・管理者設定・外部送信先が必要になり、ローカル完結という現行方針を変えるため別機能とする |
| N4 | claude.ai 使用率取得の変更 | 既存の 5 時間枠・週間枠のオーバーレイ表示とは責務が違う。今回は事後レポートだけを変更する |
| N5 | Cowork の会話本文・成果物・監査ログ | 使用量の集計に不要で、機密情報の露出面を増やす。`.claude\projects\**\*.jsonl` 以外は読まない |
| N6 | 過去単価の自動復元 | 履歴には単価適用日が無い。生成時に同梱された単価表を使い、レポート内へ前提を残す |
| N7 | ターン単位の明細 | 既存レポートと同じく、キャッシュへ保存したセッション集計値だけを表示する |

---

## 2. 用語

| 用語 | 定義 |
|---|---|
| Cowork | Claude Desktop のエージェント型タスク実行機能。2026-09-17 時点で新しい個人向け画面では Chat と統合が進行中だが、本書ではローカル保存形式の `entrypoint=local-agent` を Cowork と呼ぶ |
| ローカル Cowork | エージェントループとコード実行の履歴が利用者 PC の `local-agent-mode-sessions` に残るセッション |
| クラウド Cowork | Anthropic のサーバー上で動き、会話とファイルがアカウント側に保存されるセッション。N1 の対象 |
| 消費トークン | 既存レポートの主指標。Claude 系は `input + cache_creation + output` |
| 処理トークン | モデルが処理した量。Claude 系は消費トークンに `cache_read` を加えた値 |
| API 換算ポイント | API 公開単価の数値を係数としてトークンへ掛けた無通貨の相対量。略称 `AP` |
| 換算対象率 | 単価表で換算できた消費トークン ÷ 全消費トークン |
| 換算係数スナップショット | レポート生成時にアプリへ組み込まれていたモデル別 API 単価の数値部分。画面では通貨単位を付けない |

> 注意: API 換算ポイントは請求額ではない。定額プランの支払額、使用率枠の減り方、Anthropic 内部の
> 原価を表さない。

---

## 3. 調査結果と方式選択

### 3.1 公式仕様で確認したこと

| 事項 | 確認結果 | 設計への反映 |
|---|---|---|
| Cowork の保存場所 | ローカルセッションの会話履歴は利用者 PC に保存される。クラウドセッションはアカウント側へ保存される | ローカルとクラウドを分け、S1 / N1 とする |
| 利用枠 | Cowork を含む Claude の利用はプランの使用枠を消費し、長いエージェントタスクほど多く消費する | API 換算値を実際のプラン使用率と呼ばない |
| 監視経路 | Team / Enterprise の OpenTelemetry はリクエスト単位のモデル、トークン数、推定コストを出せる | 技術的な代替として記録するが N3 とする |
| API 単価 | Opus 5 は入力 5 / 出力 25、Fable 5 は入力 10 / 出力 50 USD/MTok。5分キャッシュ書込 1.25倍、1時間 2倍、通常キャッシュ読込 0.1倍 | 既存 `ModelPricing` の絶対単価方式を継続する |

参照:

- [Use Claude Cowork on Team and Enterprise plans](https://support.claude.com/en/articles/13455879-use-claude-cowork-on-team-and-enterprise-plans)
- [Claude Cowork architecture overview](https://support.claude.com/en/articles/14479288-claude-cowork-architecture-overview)
- [Monitor Claude Cowork activity with OpenTelemetry](https://support.claude.com/en/articles/14477985-monitor-claude-cowork-activity-with-opentelemetry)
- [Claude API pricing](https://platform.claude.com/docs/en/about-claude/pricing)

### 3.2 Windows 実機で確認した保存形式

2026-09-17 に、内容本文を出力せずファイル構造・JSON キー・数値項目だけを確認した。

| 項目 | 実測 |
|---|---|
| 保存ルート | `%AppData%\Claude\local-agent-mode-sessions` |
| Cowork セッションメタデータ | 43 件の `local_<UUID>.json`。`sessionId` / `cliSessionId` / `createdAt` / `lastActivityAt` / `model` 等を持つ |
| 使用量 JSONL | 289 ファイル、268,893,728 bytes。各セッション配下の `.claude\projects\**\*.jsonl` |
| 除外すべき監査 JSONL | 43 ファイル、244,923,472 bytes の `audit.jsonl` |
| 主セッション対応 | 43 件すべての `cliSessionId` と主 JSONL のファイル名が対応 |
| ID の一意性 | `sessionId` / `cliSessionId` / 主 JSONL 名はいずれも 43 件すべて一意 |
| Claude Code との重複 | Cowork 主 ID 43 件とホスト側 Claude Code JSONL 名 609 件の重複は 0 件 |
| 識別子 | 調べた主 JSONL 43 件すべて `entrypoint=local-agent` / `userType=external` |
| モデル | 主 JSONL の先頭 usage は Opus 5 が 39 件、Fable 5 が 4 件。メタデータは Opus 5 が 40 件、Fable 5 が 3 件 |
| プロジェクトパス | 主 JSONL 43 件すべて `cwd` は Windows パス。既存のプロジェクト正規化を適用できる |
| 選択フォルダ数 | 0 個 14件、1 個 21件、2 個 5件、3〜5 個 各1件 |

`assistant` 行の構造は Claude Code と同じで、`message.model` と次の `message.usage` を持つ。

```text
input_tokens
cache_creation_input_tokens
cache_read_input_tokens
output_tokens
cache_creation.ephemeral_5m_input_tokens
cache_creation.ephemeral_1h_input_tokens
```

一例では `model=claude-opus-5`、5分 / 1時間 TTL 内訳も取得できた。
したがって、固定モデルによる推定は行わず、履歴に記録されたモデルとトークン種別を使う。

### 3.3 方式比較

| 方式 | 内容 | 判定 |
|---|---|---|
| A | `local-agent-mode-sessions` の `.claude\projects` JSONL を直接読む | **採用。** ローカル完結で、既存 Claude Code パーサと単価表を再利用できる |
| B | claude.ai Usage ページの通信を傍受する | 不採用。現行 API は 5時間 / 週間の利用率だけを処理し、セッション・モデル・トークン種別を保持しない |
| C | OpenTelemetry / Compliance API を使う | 不採用。Team / Enterprise と管理者設定に依存し、外部送信・資格情報・保持方針が必要 |
| D | Cowork メタデータの `model` とファイルサイズから推定する | 不採用。usage 実値が読めるため、推定は精度を落とすだけである |

> 方式 A を採る理由: 現行レポートはローカル履歴を読み取り専用で集計する機能である。
> 同じデータ境界を維持したまま実測値を取得でき、利用データの外部送信も増えない。

---

## 4. 全体構成

### 4.1 データフロー

```text
%AppData%\Claude\local-agent-mode-sessions
  └─ <内部ID>\<アカウントID>\<セッション>\.claude\projects\**\*.jsonl
       │  読み取り専用 / audit・credentials・outputs は列挙しない
       ▼
SessionStatsScanner
  ├─ Cowork ルート探索と対象 JSONL の列挙                 [改修]
  ├─ ClaudeCodeHistoryParser.ParseLine                    [既存流用]
  ├─ (requestId, message.id) 重複排除                     [既存流用]
  └─ tool=cowork / key=cowork:<sessionId> へ正規化        [改修]
       ▼
session-stats-cache.json                                  [既存形式を維持]
       ▼
SessionStatsAggregator
  ├─ 期間・日・プロジェクト・モデル・ツール別集計          [改修]
  └─ API 換算ポイント / 対象率                            [改修]
       ▼
report-template.html
  └─ Cowork 系列・フィルタ・品質・API 換算ポイント         [改修]
```

### 4.2 責務の分界

| 層 | 持つ責務 | 持たない責務 |
|---|---|---|
| `SessionStatsScanner` | Cowork ルート探索、対象ファイル限定、差分判定、I/O、ツール識別 | JSON 1 行の構造解釈、単価計算、HTML 描画 |
| `ClaudeCodeHistoryParser` | Claude Code / Cowork 共通 JSONL の 1 行パース | パス探索、Cowork 判定、重複排除 |
| `SessionStatsRecord` | `ToolCowork`、共通トークン式、`cowork:` キー | 単価表、画面文言 |
| `ModelPricing` / `CostAccumulator` | モデル別単価、構成比、API 換算ポイント、対象 / 除外量 | 実請求額、プラン利用率 |
| `SessionStatsAggregator` | Cowork を含む期間・日・品質の集計 | 生 JSONL の再読込 |
| HTML テンプレート | 描画、絞り込み、並べ替え | 再集計、外部通信、単価の再取得 |

新しい `CoworkHistoryParser` は作らない。実機で JSONL 形式が共通と確認できたため、
同じ構造を 2 箇所で実装すると将来のフィールド追加時に差が出る。
`ClaudeCodeHistoryParser` の XML コメントだけを「Claude 系ローカル履歴」に広げる。

### 4.3 キャッシュ互換

`session-stats-cache.json` の `schemaVersion=2` は維持する。

- セッションの `tool` は文字列であり、`cowork` の追加は既存 JSON 形式を壊さない
- ファイルキャッシュのキーはフルパスなので、Cowork JSONL も同じ辞書へ追加できる
- API 換算ポイントはレポート生成時にモデル別内訳から再計算し、キャッシュへ新フィールドを持たせない
- スキーマ版を上げると、元履歴が既に削除された既存セッションを再構築できず失うため、不要な版上げをしない

### 4.4 状態

| 状態 | 条件 | レポートの扱い |
|---|---|---|
| `notUsed` | Cowork ルートが無い、または既知階層はあるが対象 JSONL が無い | 新規 Cowork 系列を 0 とする。既にキャッシュ済みの履歴は保持し、データ品質に「未使用」 |
| `ok` | 対象 JSONL を 1 件以上読み、致命エラーなし | 通常表示 |
| `partial` | 一部ファイルが消失・更新中・読取不能・破損 | 読めた分を表示し、件数と理由を品質画面へ出す |
| `unsupported` | セッション階層はあるが既知の `.claude\projects` 階層を確認できない | 古い Cowork キャッシュも合計へ入れず、形式変更の可能性を表示 |
| `unreadable` | Cowork ルート自体を列挙できない | 新規走査結果は入れず、既にキャッシュ済みの履歴は保持してルート読取失敗を表示 |

---

## 5. 入力・正規化仕様

### 5.1 ルート探索

既定ルートは次で求める。

```csharp
Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Claude",
    "local-agent-mode-sessions")
```

ルート直下の GUID 名は内部実装であり、固定値としてコードへ入れない。
探索は `<内部ID>\<アカウントID>\<セッションID>` のちょうど3階層だけを順に列挙し、
候補セッション配下の `.claude\projects` が存在する場合だけ、その中の `*.jsonl` を再帰列挙する。

次は**列挙対象にしない**。

```text
audit.jsonl
.claude\.credentials.json
.claude\.claude.json
.claude\tasks\**
outputs\**
ユーザーが Cowork に接続した作業フォルダ
```

> 禁止: `local-agent-mode-sessions` 全体を `*.jsonl` で再帰走査しない。
> 実機では `audit.jsonl` だけで約245 MBあり、同じ操作を二重計上するうえ、資格情報・成果物の近傍まで
> 不要に探索範囲を広げる。

### 5.2 ファイル識別

対象ファイルは `.claude\projects\**\*.jsonl` とする。パースできた行に `entrypoint` がある場合は、
少なくとも1行の `local-agent` を確認する。明示された値がすべて非 `local-agent` なら採用しない。
全行で `entrypoint` が欠ける古い形式は、Cowork 専用ルート配下であることを根拠に採用し、
品質画面へ「識別子なし」を計上して `partial` とする。

主セッションとサブエージェントは、既存 Claude Code と同じ規則を使う。

| 種別 | キー |
|---|---|
| 主セッション | `cowork:<sessionId>` |
| サブエージェント | `cowork:<sessionId>#<subagents以下の相対パス>` |
| 親キー | `cowork:<sessionId>` |

`claude:` と `cowork:` を分けるため、万一同じ `sessionId` が両方に存在しても衝突しない。

### 5.3 重複排除とトークン式

重複排除は Claude Code と同じ `(requestId, message.id)` とする。
同一 API 応答が複数 content block の行へ繰り返し記録されるため、全行加算しない。
キーを持たない usage 行は捨てずに加算し、品質画面へ件数を出す。

| 指標 | Cowork の式 |
|---|---|
| 消費トークン | `input + cache_creation + output` |
| 処理トークン | `input + cache_creation + cache_read + output` |
| キャッシュ作成 5分 | `usage.cache_creation.ephemeral_5m_input_tokens` |
| キャッシュ作成 1時間 | `usage.cache_creation.ephemeral_1h_input_tokens` |
| TTL 不明 | `cache_creation_input_tokens - 5分 - 1時間`。負値は 0 に丸める |

### 5.4 プロジェクトと時刻

- `cwd` は既存 `SessionNormalizer.NormalizeProjectKey` で大文字小文字を無視して集約する
- 表示は最初に観測した Windows パスを使う
- `timestamp` は UTC として読み、日別集計時にローカル時刻へ変換する
- メタデータの `userSelectedFolders` は複数フォルダを持ち得るため、プロジェクト名の上書きには使わない
- `createdAt` / `lastActivityAt` は JSONL 時刻が無い場合の推定値に使わない。推定値と実測値を混ぜない

### 5.5 読み取り中の変更

既存 Scanner と同じく、読取前後のファイル長・更新時刻を比較する。
変化したファイルは今回のキャッシュ更新へ採用せず、次回再走査する。
開くときは `FileShare.ReadWrite` とし、動作中の Cowork を妨げない。

---

## 6. API 換算ポイント仕様

### 6.1 指標の定義

API 換算ポイント（AP）は、API 単価表に記載された **USD / 100万トークンの数値部分**を係数として、
各トークン数へ掛けた合計とする。通貨への除算は行わず、画面に通貨記号を出さない。

```text
AP = input       × inputPrice
   + cache5m     × cacheWrite5mPrice
   + cache1h     × cacheWrite1hPrice
   + cacheUnknown× cacheWrite5mPrice
   + cacheRead   × cacheReadPrice
   + output      × outputPrice
```

例: `claude-opus-5` の係数は input 5 / cache5m 6.25 / cache1h 10 /
cacheRead 0.5 / output 25。1,000 input tokens は 5,000 AP となる。

AP は次の性質を持つ。

- 期間・ツール・セッション間で加算・比較できる
- モデルとトークン種別の単価差を維持する
- 既存 `CostAccumulator` の `total` は「トークン数 ÷ 1,000,000 × 係数」であるため、`AP = total × 1,000,000` で算出できる
- 単価表が変わると同じ履歴でも値が変わるため、換算係数スナップショットの併記が必須

### 6.2 表示単位

| 値 | 表示 |
|---|---|
| 1,000 未満 | `999 AP` |
| 1,000 以上 | `1.23 kAP` |
| 1,000,000 以上 | `1.23 MAP` |
| 1,000,000,000 以上 | `1.23 GAP` |

小数は表示桁の丸めにだけ使い、内部は `decimal` で合算する。
0.005 未満を 0 と表示しても、対象率と内訳計算からは落とさない。

### 6.3 対象ツールとモデル

| ツール | AP の扱い |
|---|---|
| Claude Code | 現行どおり `ModelPricing` にあるモデルだけ対象 |
| Cowork | Claude Code と同じ。履歴の実モデル名を使う |
| Codex | 対象外。内部モデル名と公開 API 単価の対応、キャッシュ書込 TTL が確定できない |
| Cursor | 対象外。ローカル履歴にトークンが無い |

モデル名を固定値へフォールバックしない。`ModelPricing.Lookup` が `null` の場合は、そのモデルの消費トークンを
`excludedTokens` に加え、モデル名を表示する。

### 6.4 対象率

```text
対象率 = coveredTokens / (coveredTokens + excludedTokens) × 100
```

- 分母 0 の場合は `—`
- 100% 未満なら注意色にし、除外モデルを直下へ出す
- 期間全体とセッション明細の両方へ出す
- Codex / Cursor はツール仕様として対象外なので、Claude 系の対象率の分母へ混ぜない

### 6.5 金額を出さないための境界

画面へ出してよいもの:

- AP 合計とツール別 AP
- 入力 / キャッシュ書込 / キャッシュ読込 / 出力の構成比
- 換算対象率、除外トークン、除外モデル
- 計算に使った換算係数（通貨単位なし）、参照した公開単価表の日付 / URL、「生成時点の参考値」という注記

画面へ出さないもの:

- AP を 1,000,000 で割った値
- `$` / `USD` / `円` / 為替レート
- 「節約額」「請求額」「料金」「支払額」というラベル
- 定額プランの月額との比較

---

## 7. 画面仕様

### 7.1 概要

既存の「コスト換算の内訳」を「API 利用相当（参考）」ブロックへ改称する。

```text
┌ API 利用相当（参考）──────────────────────────────────┐
│ API換算ポイント   18.4 MAP       換算対象率 99.8%      │
│ Claude Code  7.1 MAP   Cowork 11.3 MAP                 │
│ [入力][5分cache][1時間cache][読込][出力]  構成比       │
│ ※ API公開係数で重み付けした比較用の参考指標です           │
│ 除外: 0.2% / model-x                                   │
└───────────────────────────────────────────────────────┘
```

既存の構成比を残し、その上に AP 合計・Claude Code・Cowork・対象率を追加する。
Cowork が 0 件なら Cowork 行は出さない。Claude 系が全量除外なら AP は `—` とする。

### 7.2 日別グラフ

日別の積み上げ系列を 3 系列から 4 系列へ増やす。

| 系列 | 表示名 | 色 |
|---|---|---|
| Claude Code | Claude Code | 既存色を維持 |
| Cowork | Cowork | `#14b8a6`（ダーク時は `#5eead4`） |
| Codex | Codex | 既存色を維持 |
| Cursor | Cursor | 既存色を維持 |

AP の日別グラフは追加しない。概要ブロックと期間切替で比較でき、同種のグラフを増やす必要はない。

### 7.3 セッション

- ツール絞り込みへ `Cowork` を追加する
- Cowork バッジは日別グラフと同じ色にする
- 未知ツールを Claude Code と表示する現行フォールバックを廃止し、明示的な switch にする
- セッション明細の「API 利用相当」に AP・対象率・構成比を表示する
- 主 / サブエージェントの折り畳みは既存 Claude Code と同じ

### 7.4 データ品質

| 項目 | Cowork で表示する内容 |
|---|---|
| 状態 | `notUsed` / `ok` / `partial` / `unsupported` / `unreadable` |
| 走査ルート | `%AppData%\Claude\local-agent-mode-sessions` |
| 対象ファイル | `.claude\projects\**\*.jsonl` の件数と容量 |
| 除外対象 | `audit.jsonl` 等は「仕様により未走査」と表示し、エラーに数えない |
| 重複排除 | raw / accepted / duplicate / keyless |
| パース品質 | parse error / last line broken / 読取中変更 / 読取不能 |
| 対象範囲 | 「ローカル Cowork のみ。クラウドセッションは含まれない」 |

### 7.5 未定義ケースの扱い

| 状況 | 挙動 |
|---|---|
| ルートが無い | `notUsed`。レポート生成は成功させ、既存キャッシュがあれば履歴表示を維持する |
| 既知階層はあるが対象 JSONL が無い | `notUsed`。クラウドのみ利用の可能性を注記し、既存キャッシュがあれば維持する |
| 内部階層が変わった | `unsupported`。古い Cowork キャッシュも抑止して警告し、他ツールは生成する |
| ルートを列挙できない | `unreadable`。既存キャッシュがあれば履歴表示を維持し、読取失敗を表示して他ツールも生成する |
| JSONL 1 行が壊れている | 行だけを除外し `parseErrors` を加算する |
| 末尾が書込中 | ファイルを今回不採用にし、前回キャッシュがあれば維持する |
| 同じ Cowork ID が複数ファイルで見つかる | 同じファイル内容と確認できない限り自動で捨てず、キー衝突として `partial` にする |
| 単価不明モデル | トークン集計は含め、AP だけ除外する |
| 全モデルが単価不明 | AP は `—`、対象率 0%、除外モデルを表示する |
| AP が非常に大きい | `decimal` で保持し、SI接頭辞で表示する。JavaScript の整数精度に依存させないため表示用文字列も C# で作る |

---

## 8. セキュリティと制約

### 8.1 リスク

| # | リスク | 対策 | 残るリスク（受容する） |
|---|---|---|---|
| R1 | Cowork 保存領域には資格情報・指示文・成果物がある | `.claude\projects\**\*.jsonl` だけを列挙し、資格情報・監査ログ・outputs を開かない | JSONL 自体には最初の指示文と Windows パスが含まれる。既存の64文字設定と配布時確認が必要 |
| R2 | 内部保存形式が予告なく変わる | `unsupported` と品質画面、合成テスト、実履歴オプトインテストで検知する | 公式の安定スキーマではないため、Claude Desktop 更新直後に一時的に集計できない可能性がある |
| R3 | Cowork 動作中の読み取りで部分データになる | FileShare.ReadWrite、前後メタデータ比較、前回キャッシュ維持 | レポート生成直前の未確定ターンは次回まで反映されない |
| R4 | API 換算値を請求額と誤認する | 無通貨 AP、請求額ではない注記、対象率、換算係数スナップショット | 換算係数から利用者が概算額を逆算することは防がない |
| R5 | クラウド Cowork が欠け、合計を全利用量と誤認する | 画面ヘッダと品質画面へ「ローカルのみ」を表示 | ローカル / クラウドの利用割合は本機能から判定できない |

### 8.2 既知の制約（C）

| # | 制約 |
|---|---|
| C1 | Cowork の合計は Windows PC に残るローカルセッションだけであり、Claude アカウント全体の Cowork 使用量ではない |
| C2 | AP はレポート生成時の換算係数で全履歴を再評価する。同じ履歴でもアプリ更新後に値が変わり得る |
| C3 | AP は実請求額・定額プランの消費率・レート制限の使用率ではない |
| C4 | Cowork 内部フォルダ構造は公式の公開スキーマではない |
| C5 | プロジェクト表示は JSONL の `cwd` を使い、`userSelectedFolders` の複数フォルダ構成は表現しない |
| C6 | 既存どおり、キャッシュのスキーマ版更新時には既に元履歴が消えたセッションを復元できない |

---

## 9. ファイル構成と変更範囲

新規の実装ファイルは作らず、既存の責務へ最小差分で追加する。

| 対象 | 変更 |
|---|---|
| `AIUsageOverlay/Models/SessionStatsRecord.cs` | `ToolCowork = "cowork"`、XML コメント、Cowork が Claude 系トークン式を使うことを明示 |
| `AIUsageOverlay/Services/Parsing/ClaudeCodeHistoryParser.cs` | 実装変更なしを基本とし、Cowork 共通形式を扱うコメントへ更新。必要な場合だけ `entrypoint` を結果へ追加 |
| `AIUsageOverlay/Services/SessionStats/SessionStatsScanner.cs` | CoworkRoot、4引数コンストラクタ、限定列挙、状態、`ParseCoworkFile` 薄いラッパ、`cowork:` キー |
| `AIUsageOverlay/Services/SessionStats/SessionStatsAggregator.cs` | Cowork の期間・日別・品質系列、AP 合計 / ツール別 / 対象率のビュー項目 |
| `AIUsageOverlay/Services/SessionStats/ModelPricing.cs` | `ToolCowork` を Claude Code と同じ換算対象にし、既存内部合計を 1,000,000 倍して AP として返す |
| `AIUsageOverlay/Resources/Report/report-template.html` | Cowork 色・系列・フィルタ・バッジ・品質、AP 表示、未知ツールフォールバック廃止 |
| `AIUsageOverlay.Tests/ParsingTests.cs` | Cowork 共通パース、重複排除、キー、親子、トークン式 |
| `AIUsageOverlay.Tests/ScanAndRenderTests.cs` | ルート探索、除外境界、状態、キャッシュ、4系列、HTML 表示 |
| `AIUsageOverlay.Tests/CostTests.cs` | Cowork AP、ツール別合計、対象率、単価不明、金額非表示 |
| `AIUsageOverlay.Tests/RealHistoryScanTests.cs` | `AIUSAGE_REAL_SCAN=1` 時だけ Cowork 実履歴を走査し、本文を出力せず件数・品質を確認 |
| `README.md` | 対象ツール、ローカル限定、API換算ポイント、データ品質、保存範囲を更新 |
| `docs/SPEC_Usage_Dashboard.md` | N1 / C7 の解除、4ツール化、既存コスト換算節を本設計へ接続。実装完了時に更新 |

`SessionStatsCache.cs` の JSON 形式と `schemaVersion` は変更しない。

---

## 10. テスト方針と実装ステップ

2026-09-24 の自動テストは 76 件すべて PASS。`AIUSAGE_REAL_SCAN=1` の実履歴テストも 1 件 PASS。
ビルドと HTML 検証結果は 10.3 に記録する。

### 10.1 テストケース

業務情報を含む実履歴はリポジトリへ入れない。通常テストは最小の合成 JSONL と一時ディレクトリで行う。

| # | ケース | 期待 |
|---|---|---|
| T1 | Cowork ルートなし | 既存3ツールの結果が変更されず、Cowork は `notUsed` |
| T2 | ルートに `audit.jsonl` / `.credentials.json` / outputs を置く | どれも開かず、走査件数・パースエラーへ入らない |
| T3 | `entrypoint=local-agent` の主 JSONL | `tool=cowork`、`key=cowork:<sessionId>` で1セッションになる |
| T4 | Cowork サブエージェント | 親キーと子キーが `cowork:` になり、既定の親子累計が成立する |
| T5 | 同じ `(requestId, message.id)` が3行 | usage は1回だけ加算し、duplicate=2 |
| T6 | requestId 欠落 | 捨てずに加算し、keyless を増やす |
| T7 | TTL 別 cache creation | 5分 / 1時間 / 不明の合計が `cache_creation_input_tokens` と一致する |
| T8 | 読取中に長さ・mtime変更 | 今回不採用、前回キャッシュ維持、状態 `partial` |
| T9 | 既存キャッシュに Cowork を追加 | schemaVersion=2 のまま既存セッションと Cowork の両方が残る |
| T10 | Opus 5 の既知トークン | 6.1 の式どおりの AP、構成比、対象率100% |
| T11 | Claude Code + Cowork | AP 合計が両ツールの和、ツール別 AP が分離される |
| T12 | 単価不明 Cowork モデル | トークン合計へ含め、AP から除外、対象率とモデル名を表示 |
| T13 | Codex / Cursor | AP 対象へ入らず、Claude 系の対象率分母を下げない |
| T14 | HTML | Cowork 4系列・フィルタ・バッジ・品質があり、未知ツールを Claude Code と誤表示しない |
| T15 | 金額非表示 | 生成 HTML の表示文字列全体に `$` / `USD` / 円換算 / 請求額がなく、係数表にも通貨単位が無い |
| T16 | 自己完結性 | HTML に外部 URL、外部 script、外部 CSS、ネットワーク処理が無い |
| T17 | 危険文字 | 既存の `</script>` / U+2028 / U+2029 エスケープが Cowork のパス・指示文にも効く |
| T18 | 実履歴オプトイン | 対象 / 除外件数、モデル、重複排除、parseErrors を出し、指示文・パス本文をテストログへ出さない |

### 10.2 自動化できない範囲

- Claude Desktop 更新後も内部階層と `entrypoint` が同じか
- Cowork 実行中に生成しても Claude Desktop 側へ影響しないか
- 実ブラウザで 4 系列・色・ツール絞り込み・空状態が読みやすいか
- クラウド Cowork が欠ける注記を利用者が全利用量と誤認しないか
- AP が請求額ではないことがラベルと説明だけで理解できるか

### 10.3 実装状況

> 2026-09-24 時点で A0〜B2 と実履歴オプトインテストは完了。Z1 のうち実ブラウザ表示と Claude Desktop 動作中の手動確認は未実施。

<!-- TRIAGE: id=policy filter-cols=1 -->

| # | 依存 | 規模 | ステップ | Done の定義 |
|---|---|---|---|---|
| A0 | — | S | 実機保存形式と公式仕様の確認 | 本書 3.1 / 3.2 と付録 A に証拠があり、推測と未確認を分けていること |
| A1 | A0 | M | Cowork ルート探索と正規化 | **完了。** T1〜T9 相当の合成テストを追加 |
| A2 | A1 | M | 集計と4ツール表示 | **完了。** 日別4系列・フィルタ・品質表示を追加 |
| B1 | A0 | M | API 換算ポイント | **完了。** AP・ツール別 AP・対象率・係数スナップショットを追加 |
| B2 | A2, B1 | S | 文書と単価前提の更新 | **完了。** README・既存設計書・本書を同期 |
| Z1 | A2, B1, B2 | M | 実機通し確認 | **一部未実施。** 自動テスト・実履歴走査・静的 HTML 検証は完了。実ブラウザと動作中 Cowork は手動確認待ち |

依存関係:

```text
A0 ─┬─ A1 ─ A2 ─┐
    └─ B1 ───────┼─ B2 ─ Z1
                 ┘
```

| 事項 | 内容 |
|---|---|
| 並行できる | A1 の Scanner 系と B1 の AP 計算系。接点は `ToolCowork` と集計ビュー |
| 並行できない | A2 は A1 の Cowork レコードが必要。B2 は画面仕様確定後。Z1 は全実装後 |
| 最長経路 | A0 → A1 → A2 → B2 → Z1 |

> **Z1 が受け入れ基準**である。ビルド・単体テスト成功だけでは完了にしない。

---

## 11. 決定事項・対応方針一覧

### 11.1 実装時に確定した判断

| # | 決定 | 根拠 |
|---|---|---|
| Q1 | Cowork は Claude Code と別ツール表示にする | ユーザーの実装移行指示と本書の既定案。保存ルート・利用面・品質状態が別である |
| Q2 | 指標名を「API利用相当（参考）」、値を API 換算ポイント（AP）とする | 金額・通貨を出さず、ツール間・期間間の比較指標に限定する |
| Q3 | 生成時点の係数で過去分も再評価する | 履歴に当時係数が無いため。基準日と参照元をレポートへ残す |
| Q4 | ローカル Cowork だけを v1 の対象とする | クラウド履歴の取得経路を本変更へ含めず、「ローカルのみ」を明示する |

### 11.2 対応方針一覧（操作できます）

この表の方針は Rev 1.0 の実装結果である。
HTML の判定はこの PC のこのブラウザの localStorage に保存される。共有時は JSON 出力 / 取込を使う。
1 列目の記号が保存キーなので、採番を変更しない。

<!-- TRIAGE: id=policy filter-cols=1,3 -->

| # | 区分 | 項目 | 既定の方針 | 前提 | 根拠・参照 |
|---|---|---|---|---|---|
| S1 | スコープ | Cowork ローカル履歴の取り込み | 対応する | Q4 | 1.2 / 5 章 |
| S2 | スコープ | Cowork の独立表示 | 対応する | Q1 | 1.2 / 7 章 |
| S3 | スコープ | 既存集計への統合 | 対応する | A1 | 1.2 / 4 章 |
| S4 | スコープ | API 換算ポイント | 対応する | Q2, Q3 | 1.2 / 6 章 |
| S5 | スコープ | 換算対象率 | 対応する | B1 | 1.2 / 6.4 |
| S6 | スコープ | 増分キャッシュ | 対応する | A1 | 1.2 / 4.3 |
| S7 | スコープ | 既存レポートとの互換 | 対応する | A1 | 1.2 / T1 |
| N1 | 非スコープ | Cowork クラウドセッション | 対応しない | Q4 | 1.3 |
| N2 | 非スコープ | 実請求額・通貨表示 | 対応しない | Q2 | 1.3 / 6.5 |
| N3 | 非スコープ | Compliance API / OpenTelemetry | 対応しない | — | 1.3 / 3.3 |
| N4 | 非スコープ | claude.ai 使用率取得の変更 | 対応しない | — | 1.3 |
| N5 | 非スコープ | 会話本文・成果物・監査ログ | 対応しない | — | 1.3 / R1 |
| N6 | 非スコープ | 過去単価の自動復元 | 対応しない | Q3 | 1.3 / C2 |
| N7 | 非スコープ | ターン単位の明細 | 対応しない | — | 1.3 |
| Q1 | 決定事項 | Cowork の表示単位 | 確定（別ツール） | — | 11.1 |
| Q2 | 決定事項 | API 換算指標の名前と正規化 | 確定（AP） | — | 11.1 |
| Q3 | 決定事項 | 単価の時点 | 確定（生成時係数） | — | 11.1 |
| Q4 | 決定事項 | クラウド Cowork の欠落 | 確定（ローカルのみ） | — | 11.1 |
| C1 | 制約 | ローカル Cowork のみ | 受け入れる | Q4 | 8.2 |
| C2 | 制約 | 生成時単価で再評価 | 受け入れる | Q3 | 8.2 |
| C3 | 制約 | AP は請求額・使用率ではない | 受け入れる | Q2 | 8.2 |
| C4 | 制約 | 非公開の内部フォルダ構造 | 受け入れる | — | 8.2 |
| C5 | 制約 | cwd をプロジェクト表示に使う | 受け入れる | — | 8.2 |
| C6 | 制約 | 元履歴消失後の再構築不可 | 受け入れる | — | 8.2 |
| A0 | ステップ | 実機・公式仕様調査 | 対応済み | — | 3 章 / 付録 A |
| A1 | ステップ | Cowork 走査と正規化 | 対応済み | A0 | 10.3 |
| A2 | ステップ | 集計と4ツール表示 | 対応済み | A1 | 10.3 |
| B1 | ステップ | API 換算ポイント | 対応済み | A0 | 10.3 |
| B2 | ステップ | 文書更新 | 対応済み | A2, B1 | 10.3 |
| Z1 | ステップ | 実機通し確認 | 一部未実施 | A2, B1, B2 | 10.3 |

### 11.3 判断が他へ波及する項目

| # | この項目を変えると | 影響先 |
|---|---|---|
| Q1 | Claude Code と合算表示にする | `ToolCowork`、日別4系列、フィルタ、色、品質表示、T3 / T14 |
| Q2 | AP ではなく別の指標にする | `CostView`、式、単位、画面ラベル、T10〜T15 |
| Q3 | 履歴時点の単価を使う | `ModelPricing.Table` に有効期間、モデルIDと日時の対応、キャッシュ再計算規則が必要 |
| Q4 | クラウドも必須にする | ローカル方式を単独採用できない。認証・API・外部通信・プラン差・保持方針の再設計が必要 |

---

## 付録 A. 参照した原典

| 原典 | 確認したこと |
|---|---|
| `AIUsageOverlay/Services/SessionStats/SessionStatsScanner.cs` | 3ツールのルート、差分走査、Claude Code 重複排除、キー生成、FileShare.ReadWrite |
| `AIUsageOverlay/Services/Parsing/ClaudeCodeHistoryParser.cs` | `message.usage`、モデル、requestId、message.id、cwd、sessionId、tool_use のパース |
| `AIUsageOverlay/Models/SessionStatsRecord.cs` | ツール定数、消費 / 処理トークン式、キャッシュ保存モデル |
| `AIUsageOverlay/Services/SessionStats/ModelPricing.cs` | Fable 5.1 / Fable 5 / Opus 5 等の単価表、構成比用の内部絶対単価計算 |
| `AIUsageOverlay/Services/SessionStats/SessionStatsAggregator.cs` | 期間・日・ツール・品質・セッション明細のビュー生成 |
| `AIUsageOverlay/Resources/Report/report-template.html` | 3ツール固定の色・グラフ・フィルタ・未知ツールフォールバック |
| `AIUsageOverlay.Tests/CostTests.cs` | TTL 別内訳、単価、構成比、未知モデル / Codex 除外の既存テスト |
| `docs/SPEC_Usage_Dashboard.md` | Cowork が N1 / C7 で非スコープ、金額非表示の既存判断 |
| `README.md` | 現行の操作、4タブ、用語、保存、セキュリティ、コスト構成比の説明 |
| `%AppData%\Claude\local-agent-mode-sessions` | 3.2 の件数、容量、階層、JSON キー、usage、モデル、entrypoint、cwd。内容本文は出力していない |
| Anthropic Help Center / Platform Docs | ローカル / クラウドの保存境界、監視経路、現行 API 単価 |

## 付録 B. 現行仕様書との関係

`docs/SPEC_Usage_Dashboard.md` は実装済みの基本機能を定義しているが、次の記述を本設計の実装完了時に更新する。

| 現行記述 | 本設計による変更 |
|---|---|
| N1: Cowork は形式確認不能 | 実機で形式確認済み。ローカル Cowork を S1 へ移す |
| 4.3: Cowork は収集対象外 | クラウド Cowork だけを対象外にする |
| C7: 合計は Claude Code + Codex のみ | Claude Code + Cowork + Codex + Cursor の各ツール仕様に基づく合計へ改訂 |
| 7.1.1: 構成比のみ | 構成比を維持し、AP と対象率を追加 |
| 8.2 / 8.4.1 | Cowork 系列・AP・ローカル限定注記を追加 |
| 13.3 N1 の波及 | 本設計 Q1 / Q4 の波及表へ移す |
| B.4: Cowork 形式は確認不能 | 3.2 の実測へ差し替える |

現行仕様書には Cursor 未反映、ヘッダ Rev / 対象バージョン / テスト件数、Codex の旧一意化記述などの
既存ドリフトもある。これらは本機能の実装時に B2 で現行コードと突合して直す。

## 付録 C. レビュー結果

### C.1 事実誤認の修正

- 初稿は既存 `CostAccumulator` の内部合計をそのまま AP に使えるように読めた。実装は `tokens / 1,000,000 × price` なので、AP へは 1,000,000 倍が必要と明記した。
- 初稿は表示許可項目を「公開単価表」としており、金額非表示テストと矛盾した。画面表示は通貨単位のない「換算係数」とし、原典の日付 / URL だけを併記する形へ修正した。

### C.2 未定義ケースの定義

- Q4 は走査実装の採否に関わるため、期限と A1 の依存へ移した。Z1 まで保留できる構成は取り消した。
- 金額非表示の判定範囲を AP ブロックだけでなく、生成 HTML の表示文字列全体へ広げた。

### C.3 過剰設計の削除

指摘なし。

### C.4 突合して問題なしと確認できた項目

- Cowork 主 JSONL の usage / model / sessionId / entrypoint は既存 Claude Code parser で読める。
- Cowork ルートと通常の `%USERPROFILE%\.claude\projects` で主 JSONL のファイル名重複は実測 0 件だった。
- キャッシュは tool を文字列で保持し、AP の再計算に必要なモデル別トークンも既に保存するため、schemaVersion を上げずに拡張できる。
- 別コンテキストのレビューを 2 回試行したが、いずれも Codex の利用上限で開始できなかった。このため本付録は原典とのセルフレビュー結果であり、独立レビュー済みとは扱わない。

### C.5 実装レビューで修正した項目

- Cowork ルート全体の再帰列挙を避け、既知の3階層と `.claude\projects` だけを走査するよう固定した。
- ファイル読取前後の長さ・更新時刻が変わった場合、今回値を採用せず前回キャッシュを維持する順序へ修正した。
- `entrypoint` 欠落を即時拒否せず `partial` として採用し、明示された非 `local-agent` は拒否する三状態判定にした。
- `unsupported` では古い Cowork キャッシュを抑止し、`notUsed` では履歴保持方針に従ってキャッシュを維持するよう区別した。
- AP 対象率の分母から Codex / Cursor を外し、Claude Code / Cowork の係数既知率だけを示すよう修正した。
- 未知ツールを Claude Code とみなすフォールバックを Scanner / Aggregator / HTML から除去した。
