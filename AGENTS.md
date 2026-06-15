# AGENTS.md

<!--
  このファイルは Codex-md-bootstrapper スキルの雛型から生成されたものです。
  「汎用方針 / ファイル編集ルール / Git 操作ルール / コード生成」は全プロジェクト共通の固定部分です。
  プロジェクト固有の値は「環境」「プロジェクト固有」セクションに記載しています。
-->

## 基本方針
- 回答・コメントは日本語。ただし、コード・変数名は英語で。
- 挨拶・前置き・段階報告・絵文字禁止。結論ファースト
- 指摘すべきことは率直に指摘

## ファイル編集ルール（破損防止 / 厳守）
- **ファイル全体の再生成は禁止。** 変更は必ず差分（diff / 部分置換）で行う。
- `// 以下省略` 等のプレースホルダで既存コードを省略しない。
- **編集後は必ず最終行数と末尾10行を報告する。** 末尾の欠落がないか検証可能にする。
- 大きなファイルは分割して編集し、一度に全置換しない。

## Git 操作ルール（厳守）
- **破壊的コマンド（`reset --hard` / `push --force` / `clean -fd` 等）は実行しない。** コマンドを提示し、実行は人間が行う。
- 操作前に必ず `git status` / `git log` で現状を確認してから提案する。
- 作業は使い捨てブランチ + こまめなコミットを前提に提案する。

# コードスタイル

- 関数型アプローチを優先し、副作用を最小化する
- 厳密な型付け（anyは使わずunknownを使う）
- エラーは握りつぶさず、意味のあるメッセージ付きで処理する

## コード生成
- 関数・定義には詳細コメントを付与する。
- セキュリティ／暗号トピックではトレードオフ・リスクを明示する。

## 環境（Windows / クロスプラットフォーム）
- 文字コードは UTF-8。改行は **LF** に統一（既存ソースは LF。`.gitattributes` / `.editorconfig` は未配置のため、追加する場合は LF 基準で揃える）。
- バッチ等のコンソール出力は ASCII 専用にし、文字化けを避ける（`build-release.bat` のログは英語・ASCII で記述する方針）。
- 本リポジトリは `C:\ToolCreate\Codex-UsageTool`（OneDrive 同期対象外）。同期フォルダへ移す場合は書き込み競合に注意する。

## AGENTS.md 自己改善（会話中の常時監視）
以下を検知したら、作業を一旦止めて「AGENTS.md への追記提案」を提示する。
提案はするが、AGENTS.md への書き込みは必ずユーザーの承認を得てから行う。

### 検知トリガー
- プロジェクト独自のルール・規約が新たに指摘されたとき
- 同じ種類の修正指示が2回以上繰り返されたとき（恒久ルール化の兆候）
- 「関連箇所も揃えて」等、横断的に一貫させる対応が指示されたとき

### 提案フォーマット
検知時は以下を提示する:
- 検知した事象（どの発言・どの修正が根拠か）
- AGENTS.md のどのセクションに、どの文言を追記すべきか（そのまま貼れる形）
- 既存ルールと重複・矛盾しないかの確認

### 制約
- 自動でファイルに書き込まない。提案 → 承認 → 差分で追記、の順を守る。
- 1セッションで提案を乱発しない。明確なトリガーがある時のみ。

## プロジェクト固有
- 概要: **AI Usage Overlay**（`AIUsageOverlay`）— Codex.ai / GitHub Copilot / Codex の使用量を Windows デスクトップに常時表示するオーバーレイ。タスクトレイ常駐で、使用率に応じてトレイアイコンの色が変化する。
- 言語・FW: **C# / .NET 9 (WPF)** — `net9.0-windows`、Windows 専用。配布は単一 self-contained exe。
- ビルド:
  - 開発: `cd AIUsageOverlay && dotnet restore && dotnet build -c Release`
  - 実行: `dotnet run --project AIUsageOverlay/AIUsageOverlay.csproj`
  - リリース exe: リポジトリルートの `build-release.bat`（bin/obj/publish をクリーンしてから publish。出力は `publish\AIUsageOverlay.exe`）
  - `v*` タグ push で `.github/workflows/release.yml` が自動 publish & GitHub Release 添付。
- テスト: **自動テストプロジェクトなし。** 動作確認は exe 起動 + 各サービスのログイン → 使用量表示の手動確認で行う。
- ディレクトリ構成:
  - `Models/` — POCO（`AppSettings` / `ScrapedUsageData` / `CodexUsageData` / `GitHubCopilotData` / `UsageRecord`）
  - `Services/` — `ClaudeApiClient` / `GitHubWebScraper` / `CodexWebScraper`（WebView2 で傍受）、`UsageService`（統合窓口）、`Parsing/`（JSON パース専用クラス）
  - `ViewModels/MainViewModel.cs` — `INotifyPropertyChanged` / `DispatcherTimer`
  - `App.xaml(.cs)` 起動・トレイアイコン生成 / `MainWindow` オーバーレイ / `SettingsWindow` / `LoginWindow`
- 触ってはいけない領域 / 注意:
  - データ取得は各サービスとの直接通信のみ。**外部サーバー送信（テレメトリ等）を追加しない。**
  - Codex / OpenAI 表示は API Billing のクレジット残高・課金額ではなく、Codex/ChatGPT 側の使用制限を対象にする。Claude と同様に 5時間制限の使用率％と週間使用率％を表示する。
  - パース処理は必ず `Services/Parsing/` の Parser クラスに置き、Scraper には WebView2 制御・JS 傍受のみ残す。
  - プロパティ更新は `MainViewModel.SetProperty<T>` を使う（手動で `PropertyChanged` を発火しない）。
  - `_gitHubEverLoaded` 等のフラグによる「取得失敗時の前回表示維持（ちらつき防止）」を壊さない。
  - WebView2 のアイドル時メモリ削減対応が入っているため、Scraper のライフサイクル変更時は退行に注意。
  - 認証セッション: `%TEMP%\AIUsageOverlay_WebView2`。設定・計測: `%AppData%\AIUsageOverlay\`（`settings.json` / `usage.json`）。
  - 旧名 `ClaudeUsageOverlay` からリネーム済み。新規コードは `AIUsageOverlay` を使う。
- コミット規約: Conventional Commits 風（日本語）。`feat:` `fix:` `refactor:` `perf:` `build:` `chore:` 等。例: `fix(viewmodel): GitHub Copilot表示のちらつきを修正`。
- コーディング規約: `Nullable` / `ImplicitUsings` 有効。**関数・クラス・主要フィールドに日本語の XML ドキュメントコメント（`/// <summary>`）と詳細コメントを付ける**（既存コードに合わせる）。名前空間は `AIUsageOverlay.*`。

## Imported Claude Cowork project instructions

Claudeの容量表示のためのアプリケーション開発
