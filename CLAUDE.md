# CLAUDE.md

<!--
  このファイルは cmd-bootstrapper スキルの雛型から生成されたものです。
  「基本方針 / 作業の進め方（運用ルール）/ ファイル編集ルール / Git 操作ルール / コード生成」は全プロジェクト共通の固定部分です。
  プロジェクト固有の値は「環境」「プロジェクト固有」セクションに記載しています。
-->

## 基本方針
- 回答・コメントは日本語。ただし、コード・変数名は英語で。
- 挨拶・前置き・段階報告・絵文字禁止。結論ファースト
- 指摘すべきことは率直に指摘

## 作業の進め方（運用ルール）

### 止まる条件（可逆な操作は確認せず進める）
一時停止してよいのは次の3つだけ。これ以外の可逆（取り消せる）操作は、逐一確認を取らずそのまま進める。
- 破壊的な操作（元に戻せない）
- スコープの変更
- 人間にしか判断できない場面

可逆な操作（ローカルのファイル作成・編集、`learnings.md` への追記、ブランチ作成、テスト実行など）はすべて GO。取り消せる操作で逐一止まらない。

#### 可逆でも必ず確認する例外（提案 → 承認 → 実行）
可逆に見えても「スコープ / 方針の変更」に当たるものは勝手に実行しない。
- 破壊的 git コマンド（`reset --hard` / `push --force` / `clean -fd`）→「Git 操作ルール」に従う
- リモートへの `push`
- CLAUDE.md / AGENTS.md / 規約ファイル / スキル定義の変更 →「CLAUDE.md 自己改善」に従う
- 外部サービスへの不可逆な副作用（送信・課金・公開・不可逆な削除）

### learnings.md に学びを積む
- 作業開始時に、まずプロジェクト直下の `learnings.md` を読んでから動く（存在する場合）。
- 今回得た学びを `learnings.md` に記録する。**追記のみ・重複禁止・誤りと判明したものは削除。**
- 「事実（Facts）」と「方針（Policy）」を必ず分ける。事実=観測・実行結果で確認できたこと。方針=それを踏まえた今後の進め方。
- 記載フォーマット:

~~~
## YYYY-MM-DD
### Facts（事実）
- <実行結果・観測から確認できたこと>
### Policy（方針）
- <今後こう進める、という判断>
~~~

### 進捗は証拠で報告する
- 報告の前に、すべての主張をツールの実行結果と照合する。
- 確認できていないことは「未確認」と明記する。推測を事実として書かない。
- テスト・ビルドが失敗したら、その出力をそのまま貼る（要約・改変しない）。

### チームリードとして動く（サブエージェント並行化）
- 独立して進められるタスクはサブエージェントに分割し、並行で進める。稼働中も本体の作業は止めない。
- 検証は「まっさらな別コンテキスト」（別サブエージェント）で行い、思い込みを持ち込まない。
- 依存関係のあるタスクは並行化しない（結果待ちが要るものは直列にする）。

### 出しすぎない（最小実装）
- 一番シンプルで「ちゃんと動く」ものだけを出す。余計な機能・先回りの抽象化・頼まれていないリファクタはしない（要らないものを足したら減点）。
- 各タスクの着手前に Done を一文で宣言する（例:「〇〇が▲▲できること」）。宣言した範囲を超えない。

### 難易度宣言モード（ユーザーが宣言したときのみ）
通常時は「基本方針」（結論ファースト・前置き禁止）を維持する。ユーザーが「これは難しい」等と宣言したときだけ次に切り替える。
- 即答しない。まず考えを整理する。
- 答える前に、考えるべき論点を自分で3つ挙げる。
- 根拠を示してから結論を述べる。

<!-- shared-rules:begin source=~/.claude/CLAUDE.md rev=2026-08-26 -->
<!-- ここから ~/.claude/CLAUDE.md の逐語コピー。要約・言い換えをしない（差分検証を成立させるため）。 -->
## ファイル編集ルール（破損防止 / 厳守）
- **ファイル全体の再生成は禁止。** 変更は必ず差分（diff / 部分置換）で行う。
- `// 以下省略` 等のプレースホルダで既存コードを省略しない。
- **編集後は必ず最終行数と末尾10行を報告する。** 末尾の欠落がないか検証可能にする。
- 大きなファイルは分割して編集し、一度に全置換しない。

## Git 操作ルール（厳守）
- **破壊的コマンド（`reset --hard` / `push --force` / `clean -fd` 等）は実行しない。** コマンドを提示し、実行は人間が行う。
- 操作前に必ず `git status` / `git log` で現状を確認してから提案する。
- 作業は使い捨てブランチ + こまめなコミットを前提に提案する。
<!-- shared-rules:end -->

## コードスタイル
- 言語の型システムを最大限活用し、緩い型（エスケープハッチ）を避ける。
- 副作用は最小化し、状態変更の範囲を局所化する。
- エラーは握りつぶさず、意味のあるメッセージ付きで処理する。

## コード生成
- 関数・定義には詳細コメントを付与する。
- セキュリティ／暗号トピックではトレードオフ・リスクを明示する。

## トークン削減ルール

### コード読み込み
- ファイル全体の読み込みは原則禁止。以下の手順で該当箇所のみ読む:
  1. まず Grep / Glob で該当シンボル・キーワードの位置を特定する
  2. Read は行範囲指定（offset / limit）で該当関数・該当ブロックのみ読む
  3. 依存先を追う場合も「特定 → 範囲読み」を繰り返す
- 例外: 設定ファイル・100 行未満の小ファイルは全体読み可

### 解析は索引経由
- コードベース全体の解析・調査では、生ソースを直接読み始めない。先に `docs/` 配下の既存文書を確認し、該当情報があればそちらを起点にする（仕様 `SPEC_*.md` / レビュー結果 `REVIEW_*.md` / テスト手順 `TEST_*.md`）。
- **本リポジトリに `docs/design/`（code-design-doc スキルの成果物）は未作成。** 全体解析を行った場合は成果物を `docs/design/` に残し、次回以降を差分レビューで済ませられるようにする。
- 索引に該当情報があれば、ソースは裏取りが必要な箇所のみ範囲指定で読む。

### 出力
- 調査結果の報告は要点のみ。ソースの長文引用はせず `ファイル名:行番号` の参照形式で示す。
- コード生成時の説明文は求められない限り最小限にする。
- ファイル修正時の差分編集は「ファイル編集ルール」に従う。

### 探索作業の委譲
- 「どこに実装があるか」「影響範囲の洗い出し」等の探索作業は `code-explorer` サブエージェントに委譲する（親コンテキストの汚染防止）。
- 長い仕様書・設計書・ログの内容確認は `doc-summarizer` サブエージェントに委譲する。

#### サブエージェントの定義と適用条件

定義ファイル: `.claude/agents/code-explorer.md` / `.claude/agents/doc-summarizer.md`
（Markdown + YAML frontmatter。`name` と `description` のみ必須。`tools` 省略時は全ツール継承）

| 実行環境 | 読み込まれるか |
| --- | --- |
| Claude Code（対話・`-p`） | される（プロジェクトスコープ `.claude/agents/`） |
| Claude Agent SDK | される（`CLAUDE_AGENT_SDK_DISABLE_BUILTIN_AGENTS=1` で組込み型を外せる） |
| Cowork（デスクトップのエージェントモード） | Agent ツールの仕様上 `.claude/agents/*.md` を参照する。公式ドキュメントに Cowork 固有の記載は無い |

- **プロジェクトスコープ**（`.claude/agents/`）はカレントディレクトリから上へ辿って探索される。リポジトリに commit してチームで共有する。
- **全プロジェクトで使う場合**は `~/.claude/agents/`（= `C:\Users\<user>\.claude\agents\`）へコピーする。同名定義はプロジェクト側が優先される。
- **反映タイミング**: 既存の `agents` ディレクトリ内のファイル追加・編集は数秒で自動反映（再起動不要）。ただし **`agents` ディレクトリ自体を新規作成した直後は再起動が必要**（監視対象はセッション開始時に存在したディレクトリのみ）。
- サブエージェントは `AskUserQuestion` を使えない。ユーザーへの確認が必要な判断は親側で行う。
- **定義ファイルが未配置の環境では `Explore`（探索）/ `general-purpose`（読解・要約）で代替する。** 存在しないエージェント型を指名しない。

## 環境（Windows / クロスプラットフォーム）
- 文字コードは UTF-8。改行は **LF** に統一。`.gitattributes` で `*.md` `*.cs` `*.csproj` `*.xaml` `*.yml` `*.json` を `eol=lf` に固定済み。ただし `*.bat` / `*.cmd` は cmd.exe が CRLF を要求するため `eol=crlf`（LF だと `REM` 行や `^` 行継続が壊れる）。`.editorconfig` は未配置。
- バッチ等のコンソール出力は ASCII 専用にし、文字化けを避ける（`build-release.bat` のログは英語・ASCII で記述する方針）。
- 本リポジトリは `C:\ToolCreate\Claude-UsageTool`（OneDrive 同期対象外）。同期フォルダへ移す場合は書き込み競合に注意する。

## CLAUDE.md 自己改善（会話中の常時監視）
以下を検知したら、作業を一旦止めて「CLAUDE.md への追記提案」を提示する。
提案はするが、CLAUDE.md への書き込みは必ずユーザーの承認を得てから行う。

### 検知トリガー
- プロジェクト独自のルール・規約が新たに指摘されたとき
- 同じ種類の修正指示が2回以上繰り返されたとき（恒久ルール化の兆候）
- 「関連箇所も揃えて」等、横断的に一貫させる対応が指示されたとき

### 提案フォーマット
検知時は以下を提示する:
- 検知した事象（どの発言・どの修正が根拠か）
- CLAUDE.md のどのセクションに、どの文言を追記すべきか（そのまま貼れる形）
- 既存ルールと重複・矛盾しないかの確認

### 制約
- 自動でファイルに書き込まない。提案 → 承認 → 差分で追記、の順を守る。
- 1セッションで提案を乱発しない。明確なトリガーがある時のみ。

<!-- git-rules-keeper:begin -->
## Git 運用ルール（AI Usage Overlay）

このセクションは `.claude/git-rules.json` から生成されている。手で書き換えず、
ルールを変えるときは `.claude/git-rules.json` を直して再生成する。詳細は `GIT_WORKFLOW.md`。

- 統合ブランチ: `master`
- 種別ブランチ: `feat/` `fix/` `docs/` `refactor/` `perf/` `test/` `build/` `chore/`（push する）
- 作業ブランチ: `wip`（ローカル限定・push しない）
- マージ方針: 作業→種別 `--squash` / 種別→`master` `--no-ff`
- squash 後は `git commit` を **`-m` なし**で実行し、`.git/SQUASH_MSG` の先頭 1 行（`wip` 側の代表コミットの文言）だけ残す。要約を書き直さない（squash はマージ関係を残さないため、同じ文言だけが対応の手がかりになる）
- 作業ブランチは `wip` の 1 本を使い回す。取り込んだ直後に `git update-ref refs/wip-archive/<トピック> wip` で退避してから `git switch -C wip <種別ブランチ>` で作り直す。新しい名前のブランチを作らない
- 種別ブランチの削除: リリース後に `git branch --merged master` で候補を出し、リモートとローカルの両方から削除する
- push 先: 種別 `backlog` / `github` / `master` `backlog` / `github`
- コミットメッセージ: `<種別>(<範囲>): <要約>（日本語・Conventional Commits 風。例: fix(viewmodel): GitHub Copilot表示のちらつきを修正）`
- 配布: ソース公開。`github` にはコードもドキュメントも push する

禁止:

- `master` / 種別ブランチへの直接コミット（必ず 1 段下からマージする）
- `wip` のリモート push（ローカル限定）
- `git push --all`（push しない約束の作業ブランチまで送られる）
- 退避 ref（`refs/wip-archive/*`）の削除（squash 前の内訳が復元できなくなる）
- `git push --mirror`（`refs/wip-archive/*` まで送られ、隠したはずの試行錯誤が公開される）
- `push --force` / `reset --hard` / `clean -fd` などの破壊的コマンド
- ビルド成果物・生成物のコミット（`.gitignore` 準拠）
<!-- git-rules-keeper:end -->

## プロジェクト固有
- 概要: **AI Usage Overlay**（`AIUsageOverlay`）— Claude.ai / GitHub Copilot / Codex の使用量を Windows デスクトップに常時表示するオーバーレイ。タスクトレイ常駐で、使用率に応じてトレイアイコンの色が変化する。
- 言語・FW: **C# / .NET 9 (WPF)** — `net9.0-windows`、Windows 専用。配布は単一 self-contained exe。
- ビルド:
  - 開発: `cd AIUsageOverlay && dotnet restore && dotnet build -c Release`
  - 実行: `dotnet run --project AIUsageOverlay/AIUsageOverlay.csproj`
  - リリース exe: リポジトリルートの `build-release.bat`（bin/obj/publish をクリーンしてから publish。出力は `publish\AIUsageOverlay.exe`）
  - `v*` タグ push で `.github/workflows/release.yml` が自動 publish & GitHub Release 添付。
- テスト: `AIUsageOverlay.Tests/`（xUnit 2.9.2 / `net9.0-windows`。`ParsingTests.cs` / `ScanAndRenderTests.cs` / `RealHistoryScanTests.cs`）。実行は `dotnet test AIUsageOverlay.Tests/AIUsageOverlay.Tests.csproj`。**カバーするのはパース処理とレポート生成のみ**で、WebView2 経由の取得・オーバーレイ表示・トレイ常駐は対象外。これらは exe 起動 + 各サービスのログイン → 使用量表示の手動確認で行う。
- ディレクトリ構成:
  - `Models/` — POCO（`AppSettings` / `ScrapedUsageData` / `CodexUsageData` / `GitHubCopilotData` / `UsageRecord`）
  - `Services/` — `ClaudeApiClient` / `GitHubWebScraper` / `CodexWebScraper`（WebView2 で傍受）、`UsageService`（統合窓口）、`Parsing/`（JSON パース専用クラス）
  - `ViewModels/MainViewModel.cs` — `INotifyPropertyChanged` / `DispatcherTimer`
  - `App.xaml(.cs)` 起動・トレイアイコン生成 / `MainWindow` オーバーレイ / `SettingsWindow` / `LoginWindow`
- 触ってはいけない領域 / 注意:
  - データ取得は各サービス（Claude / GitHub Copilot / Codex）との直接通信、および更新チェック等に必要な公開メタデータの HTTPS GET のみに限定する。**利用データ・認証情報・テレメトリ等を外部サーバーへ送信しない。**
  - パース処理は必ず `Services/Parsing/` の Parser クラスに置き、Scraper には WebView2 制御・JS 傍受のみ残す。
  - プロパティ更新は `MainViewModel.SetProperty<T>` を使う（手動で `PropertyChanged` を発火しない）。
  - `_gitHubEverLoaded` 等のフラグによる「取得失敗時の前回表示維持（ちらつき防止）」を壊さない。
  - WebView2 のアイドル時メモリ削減対応が入っているため、Scraper のライフサイクル変更時は退行に注意。
  - 認証セッション: `%TEMP%\AIUsageOverlay_WebView2`。設定・計測: `%AppData%\AIUsageOverlay\`（`settings.json` / `usage.json`）。
  - 旧名 `ClaudeUsageOverlay` からリネーム済み。新規コードは `AIUsageOverlay` を使う。
- コミット規約: Conventional Commits 風（日本語）。`feat:` `fix:` `refactor:` `perf:` `build:` `chore:` 等。例: `fix(viewmodel): GitHub Copilot表示のちらつきを修正`。
- コーディング規約: `Nullable` / `ImplicitUsings` 有効。**関数・クラス・主要フィールドに日本語の XML ドキュメントコメント（`/// <summary>`）と詳細コメントを付ける**（既存コードに合わせる）。名前空間は `AIUsageOverlay.*`。
