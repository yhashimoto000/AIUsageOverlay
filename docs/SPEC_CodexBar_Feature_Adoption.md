# AIUsageOverlay 変更仕様書 — CodexBar 機能取込

| 項目 | 内容 |
|---|---|
| 作成日 | 2026-07-06（同日改訂: Win-CodexBar 参照を追加） |
| 対象 | AIUsageOverlay（C# / .NET 9 WPF, `net9.0-windows`） |
| 参考実装 1 | CodexBar（macOS 14+ / Swift, steipete 氏, MIT License）`C:\sample-code\CodexBar-main\CodexBar-main` |
| 参考実装 2 | Win-CodexBar（Windows / Rust + Tauri, Finesssee 氏, MIT License）`C:\sample-code\Win-CodexBar-main` — CodexBar の公認的コミュニティ移植。winget 配布あり |
| 粒度 | 詳細設計レベル（ファイル・クラス・プロパティ単位、フェーズ分け付き） |

---

## 1. 目的・背景

macOS メニューバーアプリ CodexBar は 57 の AI プロバイダの使用量・リセット時刻・コストを常時可視化するツールであり、本アプリ（AIUsageOverlay）と目的が同一。CodexBar が備える以下の要素を Windows / WPF 環境へ取り込み、本アプリの実用性を高める。

- 消費ペース予測（リセットまで持つか、いつ枯渇するか）
- 閾値超過・リセット完了の通知
- ローカルログからのトークン・コスト集計
- 取得失敗時の stale 表示、適応更新間隔、スヌーズ等の運用品質
- 2段バー方式のトレイアイコンデザイン

UI 方針はユーザー決定に基づき「**現行オーバーレイの改良 + トレイアイコン刷新**」とする（トレイクリックのポップアップカード追加はスコープ外）。

### 1.1 Windows 版既製品（Win-CodexBar）との関係

CodexBar には Windows 移植版 **Win-CodexBar**（Rust + Tauri、winget: `Finesssee.Win-CodexBar`）が存在し、本仕様の対象機能の多く（2段バートレイ、ペース、通知、コスト集計、ステータス監視）を既に実装している。乗り換えも選択肢だが、本アプリには次の差別化点があるため**継続開発を前提**とし、Win-CodexBar は「Windows 環境での実装解が実証済みの一次参考」として活用する。

- **常時表示オーバーレイ**: Win-CodexBar はトレイクリックで開くパネル方式。作業中に視界へ常駐させる本アプリの用途とは異なる。
- **認証方式**: Win-CodexBar の Claude 取得はブラウザ Cookie 抽出が優先（Chrome の App-Bound Encryption 対策等が必要で環境依存が強い）。本アプリの WebView2 ログインセッション方式は自己完結しており、この安定性は維持する価値がある。
- **日本語UI・インストーラー不要のzip配布**。

## 2. 参照

### 2.1 CodexBar 側の主要参照ファイル

| ファイル | 参照内容 |
|---|---|
| `Sources/CodexBarCore/UsagePace.swift` | ペース計算ロジック（本仕様 F-05 の移植元。実コード確認済み） |
| `Sources/CodexBar/IconRenderer.swift` | 2段バーアイコン描画（上=セッション、下=週間、stale 時 55% アルファ） |
| `Sources/CodexBar/AppNotifications.swift` | 通知（閾値 70/80/90 既定、セッション/週間リセット通知） |
| `Sources/CodexBarCore/CostUsageModels.swift` | Claude Code / Codex ローカルログのパース仕様（`costUSD` 合算、`message.id + requestId` で重複排除） |
| `Sources/CodexBarCore/CostUsageScanExecutor.swift` | スキャンを専用直列スレッドへ隔離する設計（UI スレッド飢餓防止） |
| `Sources/CodexBar/AdaptiveRefreshPolicy.swift` | 操作からの経過時間・電源状態に応じた更新間隔制御 |
| `docs/claude.md` / `docs/codex.md` / `docs/copilot.md` | 各プロバイダのウィンドウ定義（five_hour / seven_day 等） |

### 2.2 Win-CodexBar 側の主要参照ファイル（Windows での実装解。実コード確認済み）

| ファイル | 参照内容 |
|---|---|
| `rust/src/tray/render.rs` | 32×32 の2段バー描画（上=セッション y=8..15 / 下=週間 y=18..23、x=4..28）。エラー時はグレースケール化。%数字アイコンの別モードあり |
| `rust/src/tray/icon.rs` | 使用率レベル 4 段階（<50 緑 / <80 アンバー / <95 オレンジ / ≥95 赤） |
| `rust/src/notifications.rs` | Windows トースト通知を **PowerShell 経由の WinRT `ToastNotificationManager`** で送出（COM 登録・追加依存なし）。既定閾値 high=70 / critical=90。SessionDepleted（100%到達）/ SessionRestored（回復）通知あり。サウンド対応 |
| `rust/src/core/usage_pace.rs` | UsagePace の Rust 移植（stage 閾値 2/6/12 は macOS 版と同一） |
| `rust/src/cost_scanner.rs` / `core/jsonl_scanner.rs` | `%USERPROFILE%\.claude\projects` / `%USERPROFILE%\.codex\sessions` のスキャン（`CODEX_HOME` 対応、重複排除つき） |
| `rust/src/core/cost_pricing.rs` | モデル別静的単価表からのコスト算出（ccusage 着想、段階価格対応）。F-08 の対案 |
| `rust/src/status/indicators.rs` | Statuspage `indicator` のレベル変換（maintenance 含む）と `status_url` 保持 |

### 2.3 本アプリ側の現状（変更対象）

| ファイル | 現状 |
|---|---|
| `AIUsageOverlay/App.xaml.cs` | トレイアイコン: ドーナツ + 中央%テキスト。色閾値 50%/80% 固定。`CreateSessionBitmap()` |
| `AIUsageOverlay/MainWindow.xaml` | オーバーレイ: Claude / Copilot / Codex 各セクション（バー + % + 残り時間） |
| `AIUsageOverlay/ViewModels/MainViewModel.cs` | `DispatcherTimer` 固定間隔（既定30秒）。`_gitHubEverLoaded` / `_codexEverLoaded` による前回表示維持 |
| `AIUsageOverlay/Services/UsageService.cs` | Claude API 傍受 + ローカル計算フォールバック |
| `AIUsageOverlay/Services/Parsing/ClaudeUsageParser.cs` | `five_hour` / `seven_day` の `utilization` と `resets_at` → 残り分数に変換 |
| `AIUsageOverlay/Models/AppSettings.cs` | 設定 9 項目（通知・ペース・コスト関連なし） |

## 3. 機能対比サマリ

| 機能 | CodexBar | AIUsageOverlay 現状 | 本仕様 |
|---|---|---|---|
| セッション/週間 使用率表示 | ○ | ○ | 維持 |
| リセットまでのカウントダウン | ○（相対/絶対 切替可） | △（相対のみ） | F-04 |
| 消費ペース予測（枯渇予測 ETA） | ○ | × | F-05/F-06 |
| 警告閾値マーカー（バー上の目盛） | ○（70/80/90） | × | F-03 |
| 閾値超過・リセット通知 | ○ | × | F-07 |
| トレイ/メニューバーアイコン | 2段バー（上=セッション、下=週間） | ドーナツ+%（セッションのみ） | F-01 |
| 取得失敗時の stale 表示 | ○（アルファ 55% に減光） | △（前回値保持のみ、視覚区別なし） | F-02 |
| ローカルログのコスト集計 | ○（Claude Code / Codex CLI） | × | F-08/F-09 |
| 適応更新間隔 | ○（2分〜30分） | ×（固定30秒） | F-10 |
| 更新スヌーズ | ○ | × | F-11 |
| プロバイダ稼働ステータス監視 | ○ | × | F-12 |
| トレイポップアップカード / Merge Icons / 複数アカウント / ウィジェット / CLI / 自動更新 | ○ | ×（本仕様策定時） | 本仕様ではスコープ外 |

## 4. 変更方針とスコープ

> **【実装状況メモ（2026-07-06 更新）】**
> - **P1: 実装済み**（master 反映）。F-01〜F-04。
> - **P3: 非対応（shelved・未マージ）**。F-08/F-09 を `feat/p3-cost-usage` で試作したが、
>   本機能が集計できるのは Claude Code / Codex の **CLI ローカルログのみ**で、
>   **Claude Desktop / claude.ai の利用はローカルにトークン・コスト明細が残らず集計不可**。
>   利用実態が Desktop 中心のためトークン表示は「一旦非対応」とし、master へはマージしない。
>   将来 CLI を常用する場合は同ブランチ（Codex パーサ修正済み）から再開可能。詳細は
>   `docs/REVIEW_P3_CodexBar.md` / `docs/TEST_P3_CodexBar.md`（ブランチ側）参照。
> - **P2 / P4: 未着手**。

### 4.1 スコープ（フェーズ構成）

| フェーズ | 機能 | 狙い |
|---|---|---|
| P1: 表示基盤 | F-01 トレイアイコン刷新 / F-02 stale 表示 / F-03 警告閾値・マーカー / F-04 リセット表示切替 | 低リスクで視認性を改善。後続機能のデータ基盤（`resets_at` 保持）を整備 |
| P2: ペースと通知 | F-05 ペース計算 / F-06 ペース表示 / F-07 通知 | 「リセットまで持つか」を提示する本命機能 |
| P3: コスト集計 | F-08 ローカルログスキャン / F-09 コスト表示 | Claude Code / Codex CLI 利用者向けのコスト可視化 |
| P4: 運用改善 | F-10 適応更新間隔 / F-11 スヌーズ / F-12 稼働ステータス監視 | 負荷・電力・障害時の運用品質 |

### 4.2 スコープ外と理由

- **トレイポップアップカード（CodexBar の MenuCardView 相当）**: ユーザー決定によりオーバーレイ改良方針を採用。将来フェーズ候補。
- **Merge Icons / プロバイダ別トレイアイコン複数表示**: トレイは1アイコン維持。ツールチップ拡充で代替。
- **複数アカウント対応**: WebView2 プロファイルが1系統のため大規模改修になる。
- **ウィジェット / CLI / Sparkle型自動更新**: 本仕様ではスコープ外。自動更新は後続の
  [`SPEC_Auto_Update_Feature.md`](SPEC_Auto_Update_Feature.md)で、自前実装のP5（検知・通知・手動ダウンロード導線）としてv2.0.0に実装済み。
  Sparkle型の更新方式と、ダウンロード・自己適用を行うP6は引き続き未実装。
- **OAuth / ブラウザ Cookie 直読みによる取得方式変更**: 現行 WebView2 傍受方式は安定稼働しており、取得層は変更しない。

### 4.3 プロジェクト制約との整合（CLAUDE.md 準拠）

- **外部通信の制限**: 利用データ・認証情報・テレメトリ等を外部送信しない。本仕様のF-12は各ベンダー公式ステータスページ（Statuspage API）への**読み取りGET**のみだが、通信先を増やすため既定OFFのオプトイン設定とする。後続P5の更新確認は、承認済みルールに基づきGitHubの公開メタデータをGETする。
- **パーサは `Services/Parsing/` に配置**: F-08 の JSONL パーサは `Services/Parsing/` に新設し、ファイル列挙・キャッシュは Service 層に置く。
- **`MainViewModel.SetProperty<T>` を使用**: 追加プロパティはすべて同ヘルパー経由。
- **`_gitHubEverLoaded` 等のちらつき防止を壊さない**: F-02 は「前回値保持」を置換せず、その上に stale 視覚表現を追加する。
- **WebView2 ライフサイクル非変更**: 取得層（Scraper/ApiClient）には手を入れない（F-04 のパーサ拡張のみ）。

---

## 5. 機能仕様

### F-01: トレイアイコン刷新（2段バー方式）【P1】

**概要**: CodexBar の IconRenderer と同じ「上段=セッション、下段=週間」の2段バーをトレイアイコンに採用する。現行ドーナツ型も設定で選択可能として残す。

**CodexBar での実装**: 36×36px キャンバスに上バー（primary window）・下バー（secondary window、細め）を左から右へフィル。トラック（背景）はアルファ 28%、stale 時はフィル 55% / トラック 18%。

**本アプリでの仕様**:

- 描画クラスを `App.xaml.cs` から分離し、新規 `Services/TrayIconRenderer.cs`（static クラス）へ移動する。`CreateSessionBitmap()` は `RenderDonut()` として移設、新規 `RenderDualBar()` を追加。
- `RenderDualBar(int sessionPercent, int weeklyPercent, bool stale)` の描画仕様（32×32px、GDI+）:

| 要素 | 位置・サイズ | 色 |
|---|---|---|
| 上段バー（セッション） | x=2, y=7, w=28, h=10, 角丸 r=3 | フィル: 閾値色（F-03）。トラック: `#808080` アルファ 28% |
| 下段バー（週間） | x=2, y=21, w=28, h=6, 角丸 r=3 | フィル: 週間の閾値色。トラック: 同上 |
| stale 時 | 全体 | フィル アルファ 55%、トラック アルファ 18%（CodexBar と同値） |

- フィル幅は `w * percent / 100` を整数ピクセルにスナップして描画（サブピクセル起因のにじみ防止。CodexBar の pixel-snap 相当）。
- ツールチップは現行を維持し、ペース導入後（F-06）は `"セッション: 75% (+8% 超過)  週間: 10%"` 形式に拡張。63 文字制限のクランプ処理は現行踏襲。
- `UpdateTrayIcon()` は `AppSettings.TrayIconStyle` に応じて `RenderDualBar` / `RenderDonut` を呼び分ける。アイコンハンドルの `DestroyIcon()` 解放フローは現行のまま。
- 同一の (sessionPercent, weeklyPercent, stale, style) では再描画をスキップする（直前キーをフィールド保持。CodexBar の LRU キャッシュの簡易版で、トレイは1アイコンのためキー1件で足りる）。

**変更・新規ファイル**:

| ファイル | 変更 |
|---|---|
| `Services/TrayIconRenderer.cs` | 新規。`RenderDualBar()` / `RenderDonut()` |
| `App.xaml.cs` | `CreateSessionBitmap()` 削除→Renderer 呼び出しに置換。`UpdateTrayIcon()` に weekly / stale / style 引数追加 |
| `Models/AppSettings.cs` | `TrayIconStyle`（`"dualBar"` 既定 / `"donut"`）追加 |

**Win-CodexBar での実証値**（`tray/render.rs`）: 32×32 で上バー y=8..15（高さ7px）/ 下バー y=18..23（高さ5px）/ 左右 x=4..28。背景に暗色角丸矩形 `#3C3C46` を敷いてトラック `#50505A` を載せる方式で、明暗どちらのタスクバーでも視認できる。本仕様の座標はこの実証値に寄せてよい。エラー時は減光ではなく**グレースケール化**（彩度除去）を採用しており、F-02 の stale 表現の代替案となる（本仕様は macOS 版準拠のアルファ減光を既定とし、実機比較で選択）。

**リスク**: 32×32 の下段 5〜6px バーは 100% スケールでは視認しづらい可能性。実機確認の上、下段の高さは 5〜8px で調整余地を残す。

### F-02: 取得失敗時の stale 表示【P1】

**概要**: 取得失敗時に前回値を保持したまま「情報が古い」ことを減光で示す。現行は前回値保持のみで、新旧の区別がつかない。

**本アプリでの仕様**:

- `MainViewModel` に以下を追加:
  - `private bool _claudeStale; / _gitHubStale; / _codexStale;`（取得失敗時 true、成功時 false）
  - `public double ClaudeSectionOpacity / GitHubSectionOpacity / CodexSectionOpacity`（stale なら `0.55`、通常 `1.0`。`SetProperty` 経由）
- 判定: `RefreshUsageAsync()` 内で Claude は `isFromApi == false` のとき stale。Copilot / Codex は `data == null` かつ `_xxxEverLoaded == true`（前回値保持ルートに入ったとき）のとき stale。
- `MainWindow.xaml`: 各セクションのルート `Grid` に `Opacity="{Binding XxxSectionOpacity}"` を追加。
- トレイアイコン: Claude が stale のとき `TrayIconRenderer` に `stale: true` を渡す（F-01 の減光描画）。
- `StatusText` は現行の `"エラー: ..."` 表示を維持（stale の理由提示として兼用）。

**変更ファイル**: `ViewModels/MainViewModel.cs`、`MainWindow.xaml`、`App.xaml.cs`（PropertyChanged 購読対象に stale を追加）。

### F-03: 警告閾値の設定可能化とバー上マーカー【P1】

**概要**: 現行はトレイ色の閾値 50%/80% がハードコード。これを設定可能にし、オーバーレイのプログレスバー上にも閾値位置の目盛（マーカー）を描画する。CodexBar の `warningMarkerPercents`（既定 70/80/90）に相当。

**本アプリでの仕様**:

- `AppSettings` に追加:
  - `CautionThresholdPercent: int = 50`（注意=オレンジ開始）
  - `WarningThresholdPercent: int = 80`（警告=赤開始）
  - `ShowThresholdMarkers: bool = true`
- 色決定ロジックを新規 `Services/UsageLevelHelper.cs`（static）に集約:
  ```csharp
  /// <summary>使用率と閾値から表示レベル（通常/注意/警告）を判定する</summary>
  public static UsageLevel GetLevel(int percent, AppSettings s);
  /// <summary>レベルに対応する色を返す（緑 #4CAF50 / オレンジ #FF8C00 / 赤 #F44336）</summary>
  public static Color GetColor(UsageLevel level);
  ```
  トレイ（F-01）・週間インジケータドット（`MainWindow.UpdateWeeklyIndicatorColor()`）・オーバーレイのバー色は本ヘルパーに一本化し、閾値の二重定義を解消する。
  - 現状の不統一（要解消）: 注意色がトレイ `#FF8C00`・週間ドット `#FFC107`（MainWindow.xaml.cs L129-143）と食い違っている。一本化後は `#FF8C00` に統一する。
- マーカー描画: 新規 `Controls/ThresholdMarkerOverlay.cs`（`FrameworkElement` 派生、`OnRender` で描画）。
  - 依存関係プロパティ: `Thresholds (double[])`、`MarkerBrush`（既定 `#FFFFFF` アルファ 35%）。
  - 各閾値位置 `ActualWidth * t / 100` に幅 1px・バー高の縦線を描画。
  - `MainWindow.xaml` で各 `ProgressBar` と同セルに重ねて配置（`Grid` の同一セルに後置）。
- 既定値は現行互換（50/80）とし、CodexBar 既定（70/80/90）との違いを設定画面の説明文に記載。
- 参考: Win-CodexBar（`tray/icon.rs`）は 4 段階（<50 緑 / <80 アンバー `#FFC107` / <95 オレンジ `#FF9800` / ≥95 赤）。95% の最終段は「ほぼ枯渇」の視覚差として有効なため、実装時に 3 段で物足りなければ `CriticalThresholdPercent: int = 95` を追加して 4 段化してよい（`UsageLevelHelper` に閉じるため影響局所）。

**変更・新規ファイル**: `Services/UsageLevelHelper.cs`（新規）、`Controls/ThresholdMarkerOverlay.cs`（新規）、`Models/AppSettings.cs`、`MainWindow.xaml(.cs)`、`App.xaml.cs`、`SettingsWindow.xaml(.cs)`。

### F-04: リセット時刻の絶対/相対表示切替【P1】

**概要**: 現行は「残り 1時間13分」（相対）のみ。CodexBar 同様に「14:32 リセット」（絶対時刻）表示を選択可能にする。

**本アプリでの仕様**:

- `Models/ScrapedUsageData.cs` に追加（**後続の F-05/F-07 もこの値を使用する基盤変更**）:
  ```csharp
  /// <summary>セッション（5時間枠）のリセット日時。API の resets_at をそのまま保持する</summary>
  public DateTime? SessionResetAt { get; set; }
  /// <summary>週間枠のリセット日時</summary>
  public DateTime? WeeklyResetAt { get; set; }
  ```
- `Services/Parsing/ClaudeUsageParser.cs`: 現行は `resets_at` を残り分数へ変換して破棄している。変換に加えて `DateTime`（ローカル時刻へ変換）を上記プロパティに格納する。
- `AppSettings.ResetDisplayMode: string = "relative"`（`"relative"` / `"absolute"`）を追加。
- `MainViewModel`: `SessionRemainingText` / `WeeklyRemainingText` の組み立てを分岐。
  - relative: 現行 `FormatMinutes()`（変更なし）
  - absolute: 当日中なら `"14:32 リセット"`、翌日以降なら `"7/8 14:32 リセット"`（`ResetAt` が null のときは relative にフォールバック）
- Codex セクションは取得済みの `SessionResetText` / `WeeklyResetText`（`CodexUsageData` に既存）を absolute モード時に表示へ回す。Copilot は既存の「更新まで N日」表記を維持（月次のため絶対表示の価値が薄い）。

**変更ファイル**: `Models/ScrapedUsageData.cs`、`Services/Parsing/ClaudeUsageParser.cs`、`Models/AppSettings.cs`、`ViewModels/MainViewModel.cs`、`SettingsWindow.xaml(.cs)`。

### F-05: 消費ペース計算（UsagePace の移植）【P2】

**概要**: 「経過時間に対して使用率が先行しているか」を算出し、このペースならリセット前に枯渇するか（枯渇予測時刻 ETA）を求める。CodexBar `UsagePace.swift` の線形モデルをそのまま移植する（実コード確認済み）。

**計算仕様**（`UsagePace.swift` L43-124 準拠）:

```text
入力: actual = 使用率(0-100), windowMinutes = 窓長(300 or 10080), remainingMinutes = リセットまでの残り分
duration        = windowMinutes * 60          [秒]
timeUntilReset  = remainingMinutes * 60       [秒]  （0 以下 or duration 超なら計算不能 → null）
elapsed         = duration - timeUntilReset
expected        = elapsed / duration * 100                    … 予定消費率
delta           = actual - expected                            … 予定比（+が先行=悪化方向）
stage           = |delta| <= 2 → OnTrack
                  |delta| <= 6 → SlightlyAhead / SlightlyBehind
                  |delta| <=12 → Ahead / Behind
                  それ以外     → FarAhead / FarBehind
rate            = actual / elapsed                             … 消費速度 [%/秒]
candidate       = (100 - actual) / rate                        … 枯渇までの秒数
willLastToReset = candidate >= timeUntilReset （持つ場合 Eta = null）
Eta             = 持たない場合 candidate [秒]
speedMultiplierToReset = (100 - actual) / (actual * timeUntilReset / elapsed)
                         … 「この倍率までペースを落とせば持つ」係数
表示ゲート       = elapsed / duration >= 0.03（窓開始直後3%未満は非表示。ノイズ抑制）
特例            = elapsed == 0 かつ actual > 0 → null（リセット直後の不整合データ）
```

- CodexBar の「営業日補正（workDays）」は初期実装ではスコープ外（週間窓を営業日だけで按分する高度機能。必要になれば後続で追加）。

**本アプリでの実装**:

- 新規 `Models/UsagePace.cs`:
  ```csharp
  /// <summary>消費ペースの段階。予定より先行（Ahead）が悪化方向</summary>
  public enum PaceStage { OnTrack, SlightlyAhead, Ahead, FarAhead, SlightlyBehind, Behind, FarBehind }

  /// <summary>ペース計算結果（不変）。CodexBar UsagePace.swift 互換</summary>
  public sealed record UsagePace(
      PaceStage Stage,
      double DeltaPercent,            // 予定比（actual - expected）
      double ExpectedUsedPercent,     // 予定消費率
      double ActualUsedPercent,       // 実消費率
      TimeSpan? Eta,                  // 枯渇予測（null = リセットまで持つ）
      bool WillLastToReset,
      double? SpeedMultiplierToReset);
  ```
- 新規 `Services/UsagePaceCalculator.cs`（static、副作用なしの純関数）:
  ```csharp
  /// <summary>使用率・窓長・残り時間からペースを計算する。計算不能時は null</summary>
  public static UsagePace? Compute(double actualUsedPercent, int windowMinutes, int remainingMinutes);
  ```
- 適用対象: Claude セッション（300分）・Claude 週間（10080分）・Codex 5時間・Codex 週間。Copilot は月次クレジットのため `windowMinutes` を請求周期から動的算出（`DaysUntilRenewal` 利用、精度は参考値と明記）。

**新規ファイル**: `Models/UsagePace.cs`、`Services/UsagePaceCalculator.cs`。既存コード変更なし（利用は F-06/F-07）。

**テスト容易性**: 純関数のため、自動テスト導入時の最初の対象候補（境界値: elapsed=0 / actual=100 / remaining=window）。

### F-06: ペース表示（オーバーレイ）【P2】

**概要**: F-05 の結果をオーバーレイ各セクションに 1 行追加して表示する。CodexBar の「N% in deficit / in reserve」「Runs out in … / Lasts until reset」に相当する日本語表記。

**表示仕様**:

| 状態 | 表示例 | 色 |
|---|---|---|
| OnTrack（±2%以内） | `ペース: 順調` | `#888888` |
| 先行（Slightly/Ahead/FarAhead かつ持つ） | `ペース: 予定比 +8%` | `#FF8C00` |
| 先行かつ枯渇予測あり | `ペース: 予定比 +15% ・ 16:40頃 上限` | `#F44336` |
| 余裕（Behind 系） | `ペース: 予定比 -12% ・ リセットまで余裕` | `#4CAF50` |
| 計算不能 / ゲート未達 | 行を非表示（`Visibility.Collapsed`） | — |

- ETA の時刻表記: 当日中は `"16:40頃"`、日をまたぐ場合 `"明日 9:20頃"`（`DateTime.Now + Eta` から算出）。
- `MainViewModel` に追加（すべて `SetProperty` 経由）:
  - `SessionPaceText: string` / `SessionPaceColor: Brush` / `SessionPaceVisibility: Visibility`
  - Codex 用に `CodexPaceText` ほか同 3 点（Codex はセクション下部に 1 行で統合表示: 5時間枠を優先、5時間枠が OnTrack のときのみ週間枠のペースを表示）
  - Claude 週間のペースは行を増やさず、既存 `WeeklyRemainingText` の後ろに `"（+5%）"` 形式で付加する（オーバーレイの高さ増加を最小化）
- `MainWindow.xaml`: Claude セクションの Row 構成を 3 行 → 4 行にし、最下行に `TextBlock`（FontSize 10）を追加。Codex セクションも同様。
- トレイツールチップへ `DeltaPercent` を付加（F-01 記載）。

**変更ファイル**: `ViewModels/MainViewModel.cs`、`MainWindow.xaml`。

### F-07: 通知（閾値超過・リセット完了）【P2】

**概要**: 使用率が閾値を跨いだとき、およびセッション/週間枠がリセットされたときに Windows 通知を出す。CodexBar の quota warning（既定 70/80/90）+ reset 通知に相当。

**通知手段の選定**（トレードオフ）:

| 方式 | 利点 | 欠点 |
|---|---|---|
| `NotifyIcon.ShowBalloonTip`（採用） | 追加依存なし。既存 NotifyIcon で完結。Windows 10/11 ではトースト形式で表示される | クリックアクション等のリッチ表現不可 |
| WinRT Toast 直呼び（TFM を `net9.0-windows10.0.17763.0` へ変更） | NuGet 不要で `ToastNotificationManager` を直接使用可能 | TFM 変更が publish 構成全体に波及。unpackaged での AppUserModelID 登録が必要 |
| `Microsoft.Toolkit.Uwp.Notifications`（将来案） | ボタン・画像等リッチなトースト | NuGet 依存追加。unpackaged アプリでの COM 登録が single-file publish と相性問題を起こす事例あり |

初期実装は `ShowBalloonTip` とし、`NotificationService` のインターフェースを分離して将来差し替え可能にする。参考: Win-CodexBar（`notifications.rs`）は第4の解として **PowerShell 子プロセス経由で WinRT Toast（ToastGeneric）を送出**している（`CREATE_NO_WINDOW`、失敗時 stderr 回収）。TFM 変更なしでリッチトーストが必要になった場合の現実解として記録しておく。

**発火仕様**:

- 閾値超過: 対象窓（Claude セッション / Claude 週間 / Codex 5時間 / Codex 週間）ごとに、`前回取得値 < 閾値 <= 今回取得値` の跨ぎを検知して 1 回通知する。
  - 既定閾値: `[70, 90]`（CodexBar 既定は 70/80/90。3 段は通知過多と判断し 2 段を既定、設定でカンマ区切り編集可。**Win-CodexBar の既定も high=70 / critical=90 の 2 段であり、本判断と一致**）
  - 重複抑止: 窓ごとに「通知済み閾値集合」をメモリ保持し、リセット検知（下記）でクリア。アプリ再起動時は再通知を許容する（永続化しない。仕様として明記）。
- リセット完了: F-04 で保持する `SessionResetAt` / `WeeklyResetAt` を記憶し、`DateTime.Now` が通過した後の最初の取得成功時に「セッション枠がリセットされました（現在 3%）」を通知。`ResetAt` が取れない場合は「使用率が 30pt 以上急落」を代替トリガーとする。
- 文言例:
  - `AIUsageOverlay — Claude セッション 70% 到達（リセットは 16:00）`
  - `AIUsageOverlay — Codex 週間枠がリセットされました`
- 追加通知タイプ（Win-CodexBar `NotificationType` より採用）: **100% 到達（Exhausted）** を閾値リストと独立に通知する（`NotifyOnExhausted: bool = true`。上限到達は閾値設定に関わらず知りたい情報のため）。SessionRestored（100%からの回復）はリセット通知と重複するため採用しない。
- サウンド: Win-CodexBar は通知音を持つが、本仕様では OS のトースト音に委ねる（独自再生は実装しない）。

**実装**:

- 新規 `Services/NotificationService.cs`:
  ```csharp
  /// <summary>通知の発火判定と送出を担う。判定状態（通知済み閾値）はメモリのみ保持</summary>
  public sealed class NotificationService
  {
      /// <summary>窓ごとの現在値を通知し、閾値跨ぎ・リセットを検知して通知を発火する</summary>
      public void Evaluate(UsageWindowKey window, int percent, DateTime? resetsAt);
      /// <summary>NotifyIcon への参照を受け取る（App から注入）</summary>
      public void Attach(NotifyIcon icon);
  }
  /// <summary>通知判定対象の窓の識別子</summary>
  public enum UsageWindowKey { ClaudeSession, ClaudeWeekly, CodexSession, CodexWeekly }
  ```
- `MainViewModel.RefreshUsageAsync()` の各取得成功箇所から `Evaluate()` を呼ぶ。stale（取得失敗）時は判定しない（誤発火防止）。
- `AppSettings` に追加: `NotificationsEnabled: bool = true`、`NotificationThresholds: int[] = [70, 90]`、`NotifyOnReset: bool = true`。
- 注意: Windows の「応答不可（フォーカスアシスト）」設定下では通知が表示されない。アプリ側では制御不能である旨を設定画面に注記。

**変更・新規ファイル**: `Services/NotificationService.cs`（新規）、`ViewModels/MainViewModel.cs`、`App.xaml.cs`（NotifyIcon 注入）、`Models/AppSettings.cs`、`SettingsWindow.xaml(.cs)`。

### F-08: ローカルログのコスト・トークン集計【P3】

**概要**: Claude Code / Codex CLI がローカルに残すセッションログ（JSONL）をスキャンし、今日・直近30日のトークン数とコスト（USD）を集計する。CodexBar の Cost Usage 機能（`CostUsageModels.swift` / `CostUsageScanExecutor.swift`）の Windows 移植。**ネットワーク通信は一切発生しない**（ローカルファイル読み取りのみ）。

**スキャン対象**（Windows パス）:

| ソース | パス | 形式 |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\projects\**\*.jsonl`（環境変数 `CLAUDE_CONFIG_DIR` があれば `\projects` を優先） | 1行1メッセージの JSONL |
| Codex CLI | `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\*.jsonl`（`CODEX_HOME` 環境変数対応） | ロールアウト JSONL |

**Claude Code JSONL のパース仕様**（CodexBar `CostUsageModels.swift` 確認済みの仕様に準拠）:

- `"type": "assistant"` の行のみ対象。
- トークン: `message.usage` の `input_tokens` / `output_tokens` / `cache_creation_input_tokens` / `cache_read_input_tokens` を合算。
- コスト: 行トップレベルの `costUSD` が存在する行のみ合算（存在しない新形式ログではトークンのみ集計し、コストは「—」表示。CodexBar の `sawCost` フラグと同じ扱い。**単価テーブルからの自前推計は行わない**—モデル改定への追随コストが高く誤額リスクがあるため）。
  - 対案（不採用・記録）: Win-CodexBar `core/cost_pricing.rs` はモデル別静的単価表（段階価格・キャッシュ単価対応、ccusage 着想）で全行のコストを推計する。`costUSD` 無しログでも金額を出せる利点はあるが、単価表のメンテナンス責務を負うため本仕様では見送る。将来必要になればこのファイルが単価表の雛型になる。
- 重複排除: `message.id + requestId` をキーにする（ストリーミングで同一メッセージが複数行に出るため）。
- 日付: 行の `timestamp`（ISO8601）をローカル日付に変換して日別バケットへ集計。

**Codex JSONL のパース仕様**: `event_msg` 中の `token_count` イベントからトークン情報を抽出する（形式のバリエーションが多いため、実装時に CodexBar `CostUsageModels.swift` の `CodexSessionEntry` パース系列を一次資料として参照すること。本仕様では Claude Code を必須、Codex を努力目標とする）。

**パフォーマンス設計**（CodexBar `CostUsageScanExecutor.swift` の設計判断を踏襲）:

- スキャンはログが大きいと分単位かかり得るため、**専用の直列実行**にする: `Task.Run` + `SemaphoreSlim(1,1)` で多重スキャン禁止。UI スレッド・使用量取得（WebView2）とは完全独立。
- 増分キャッシュ: ファイルごとの `(fullPath, length, lastWriteTimeUtc)` をキーに日別集計値をキャッシュし、変化のないファイルは再パースしない。キャッシュ保存先: `%AppData%\AIUsageOverlay\cost-cache\claude-v1.json` / `codex-v1.json`（バージョン付きファイル名。スキーマ変更時は v2 に切替えて旧を破棄）。
- 実行間隔: `CostScanIntervalMinutes`（既定 15 分）+ 起動 60 秒後に初回（使用量取得の初回 5 秒遅延と重ねない）。

**実装**:

| ファイル | 内容 |
|---|---|
| `Services/Parsing/ClaudeCodeLogParser.cs`（新規） | JSONL 1 ファイル分のパース（純関数。`Stream` → 日別集計）。パーサ配置ルール準拠 |
| `Services/Parsing/CodexSessionLogParser.cs`（新規） | 同上（Codex 用） |
| `Services/CostScanService.cs`（新規） | ファイル列挙・増分キャッシュ・直列実行制御・結果保持（`CostSummary`） |
| `Models/CostSummary.cs`（新規） | `record CostSummary(long TodayTokens, double? TodayCostUsd, long Last30dTokens, double? Last30dCostUsd, DateTime ScannedAt)` |

**設定**: `CostScanEnabled: bool = true`。ただし対象ディレクトリが存在しない環境では自動的に何もしない（オーバーレイの行も非表示）。

### F-09: コスト表示（オーバーレイ）【P3】

**概要**: F-08 の集計結果をオーバーレイ最下部（StatusText の上）に 1 行表示する。

- 表示例: `Claude Code 今日 1.2M tok / $3.40 ・ 30日 45.8M / $72.10`（コスト無しログでは `$` 部を省略）
- トークン数の表記: `< 1000 → そのまま` / `K`（千） / `M`（百万）を 1 桁小数で丸め。
- Codex 集計が有効ならツールチップに Codex 分を表示（行は増やさない）。
- `MainViewModel` に `CostSummaryText: string` / `CostRowVisibility: Visibility` を追加。`CostScanService` の完了イベントを購読して `Dispatcher` 経由で反映。
- `MainWindow.xaml`: Grid に Row 追加（StatusText の直前、FontSize 10、`#777777`）。

**変更ファイル**: `ViewModels/MainViewModel.cs`、`MainWindow.xaml`。

### F-10: 適応更新間隔（AdaptiveRefreshPolicy）【P4】

**概要**: 現行の固定間隔（既定 30 秒）は WebView2 スクレイピングとしては高頻度で、放置中は無駄が大きい。CodexBar の AdaptiveRefreshPolicy（操作直後は短く、放置時は長く、電源制約時は最長）を移植する。

**間隔決定表**（`Compute(now, lastInteractionAt, isOverlayVisible, powerSaver)`）:

| 条件（上から評価） | 間隔 |
|---|---|
| バッテリー駆動かつ残量 20% 未満、または省電力モード | 30 分 |
| オーバーレイ非表示（トレイのみ常駐） | 15 分 |
| 最終操作から 5 分以内 | `RefreshIntervalSeconds`（既定 30 秒） |
| 〜1 時間 | 2 分 |
| 〜4 時間 | 5 分 |
| 4 時間超 | 15 分 |

（CodexBar は 2/5/15/30 分 + 制約時 30 分。本アプリは「操作直後だけ既存の秒単位設定を尊重する」点が差分）

- 「操作」の定義: 手動更新ボタン、オーバーレイの表示切替・ドラッグ、設定保存、ログイン完了。`MainViewModel.NotifyUserInteraction()` を各イベントから呼び、`_lastInteractionAt` を更新。
- 実装: `DispatcherTimer.Tick` の先頭で次間隔を再計算して `Interval` を更新（タイマー再生成しない）。
- 電源状態: `System.Windows.Forms.SystemInformation.PowerStatus`（`PowerLineStatus` / `BatteryLifePercent`）を使用。
- `AppSettings.AdaptiveRefreshEnabled: bool = true`。OFF なら現行動作（固定間隔）。

**変更・新規ファイル**: `Services/AdaptiveRefreshPolicy.cs`（新規、純関数 `Compute()`）、`ViewModels/MainViewModel.cs`、`MainWindow.xaml.cs`（操作フック）、`Models/AppSettings.cs`、`SettingsWindow.xaml(.cs)`。

### F-11: 更新スヌーズ【P4】

**概要**: 会議中・配信中などに WebView2 の自動巡回を止めたい場合の一時停止。CodexBar の "Pause refresh" 相当。

- トレイ右クリックメニュー（`App.BuildTrayContextMenu()`）に「更新を一時停止」サブメニューを追加: `30分` / `1時間` / `3時間` / `再開`。
- `MainViewModel.SnoozeUntil: DateTime?` を追加。`RefreshUsageAsync()` 冒頭で `SnoozeUntil > DateTime.Now` なら即 return（手動更新ボタンはスヌーズを解除して実行）。
- スヌーズ中の表示: `StatusText = "一時停止中（〜14:30）"`、トレイアイコンは stale 描画（F-01 の減光）を流用。
- 永続化しない（アプリ再起動で解除）。

**変更ファイル**: `App.xaml.cs`、`ViewModels/MainViewModel.cs`。

### F-12: プロバイダ稼働ステータス監視【P4・既定OFF】

**概要**: 「使用量が取れないのは自分のログイン切れか、サービス障害か」を切り分けるため、各ベンダー公式ステータスページを定期確認して異常時にオーバーレイへバッジを出す。CodexBar の Status Pages 機能に相当。

**制約整合（重要）**: CLAUDE.md の「データ取得は各サービスとの直接通信のみ・外部送信禁止」に対し、本機能は各ベンダー公式のステータス API への**読み取り GET のみ**（送信データなし・認証不要・Cookie 不要）。ただし通信先が増えることに変わりはないため、**既定 OFF（オプトイン）**とし、設定画面に通信先を明記する。

**取得先**（いずれも Statuspage.io 標準 API、10 分間隔、タイムアウト 10 秒）:

| サービス | URL | 判定フィールド |
|---|---|---|
| Claude | `https://status.anthropic.com/api/v2/status.json` | `status.indicator`（`none` / `minor` / `major` / `critical`） |
| GitHub | `https://www.githubstatus.com/api/v2/status.json` | 同上 |
| OpenAI | `https://status.openai.com/api/v2/status.json` | 同上 |

**表示**: `indicator != "none"` のとき、該当セクションのラベル（例: `セッション`）の左に `▲`（minor=オレンジ、major/critical=赤）を表示し、ツールチップに `status.description` を出す。取得失敗（ステータスページ自体に届かない）は表示なし（誤報回避）。Statuspage はコンポーネント個別ページで `indicator` に `maintenance` / `degraded_performance` 等を返す場合があるため、Win-CodexBar `status/indicators.rs` の `from_statuspage()` と同様に「既知の異常値以外は警告扱いにしない」ホワイトリスト方式でパースする（パーサ: `Services/Parsing/StatusPageParser.cs`）。

**実装**: 新規 `Services/ProviderStatusService.cs`（`HttpClient` 使用。パースは `Services/Parsing/StatusPageParser.cs` に分離）、`Models/ProviderStatus.cs`（`record ProviderStatus(string Indicator, string? Description)`）。`AppSettings.StatusMonitorEnabled: bool = false`。

**変更・新規ファイル**: 上記新規 3 点、`ViewModels/MainViewModel.cs`、`MainWindow.xaml`、`SettingsWindow.xaml(.cs)`。

---

## 6. AppSettings 追加プロパティ一覧

すべて `[JsonPropertyName]` 付与・既定値ありで追加するため、**既存 settings.json との後方互換は自動的に保たれる**（未知キーは既定値で補完）。

| プロパティ | 型 | 既定値 | 機能 |
|---|---|---|---|
| `TrayIconStyle` | string | `"dualBar"` | F-01（`"donut"` で従来型） |
| `CautionThresholdPercent` | int | `50` | F-03 |
| `WarningThresholdPercent` | int | `80` | F-03 |
| `ShowThresholdMarkers` | bool | `true` | F-03 |
| `ResetDisplayMode` | string | `"relative"` | F-04（`"absolute"`） |
| `PaceEnabled` | bool | `true` | F-05/F-06。ペース行の表示（OFF で計算もスキップ） |
| `NotificationsEnabled` | bool | `true` | F-07 |
| `NotificationThresholds` | int[] | `[70, 90]` | F-07 |
| `NotifyOnReset` | bool | `true` | F-07 |
| `NotifyOnExhausted` | bool | `true` | F-07。100% 到達通知 |
| `CostScanEnabled` | bool | `true` | F-08/F-09。コスト行の表示（OFF でスキャンも停止。対象ディレクトリ無しなら自動不活性） |
| `CostScanIntervalMinutes` | int | `15` | F-08（UI 非公開。settings.json でのみ変更可） |
| `AdaptiveRefreshEnabled` | bool | `true` | F-10 |
| `StatusMonitorEnabled` | bool | `false` | F-12。ステータスバッジの表示（OFF でポーリングも停止。オプトイン） |

表示項目の ON/OFF はすべて設定画面「表示項目」タブに集約する（§7.2）。

追加後の `settings.json` 例（抜粋）:

```json
{
  "refreshIntervalSeconds": 30,
  "trayIconStyle": "dualBar",
  "cautionThresholdPercent": 50,
  "warningThresholdPercent": 80,
  "showThresholdMarkers": true,
  "resetDisplayMode": "relative",
  "paceEnabled": true,
  "notificationsEnabled": true,
  "notificationThresholds": [70, 90],
  "notifyOnReset": true,
  "notifyOnExhausted": true,
  "costScanEnabled": true,
  "costScanIntervalMinutes": 15,
  "adaptiveRefreshEnabled": true,
  "statusMonitorEnabled": false
}
```

## 7. 画面変更

### 7.1 オーバーレイ（MainWindow）Before / After

```text
[Before]                                  [After]
┌────────────────────────────┐            ┌────────────────────────────┐
│ ███████████░░░ 75% │ ●10%  │↺           │ ███████│██▲░░ 75% │ ●10%(+2%) │↺   ← ▲=閾値マーカー
│ セッション 1時間13分 │週間 5日2時間│      │ セッション 16:00 リセット│週間 …│   ← 絶対表示モード時
│                            │            │ ペース: 予定比 +8%          │   ← F-06（新規行）
│ ──────────────────────     │            │ ──────────────────────     │
│ (Copilot / Codex 各行)      │            │ (Copilot / Codex 各行+ペース)│
│                            │            │ Claude Code 今日 1.2M/$3.4 …│   ← F-09（新規行）
│                  API: 14:32│            │                  API: 14:32│
└────────────────────────────┘            └────────────────────────────┘
※ 取得失敗中はセクション全体が 55% 不透明度（F-02）
※ 障害検知時はセクションラベル左に ▲（F-12、既定OFF）
```

### 7.2 設定画面（SettingsWindow）の再構成

現行の縦一列 StackPanel（幅 400×高さ 520）に追加項目を継ぎ足すと破綻するため、`TabControl` による 4 タブ構成へ再編する。**新規表示要素（ペース行・コスト行・ステータスバッジ等）はすべて個別に表示/非表示を選択でき、「表示項目」タブに集約する**。機能スイッチと表示スイッチは分離せず 1 つに統一する（OFF = 表示しない かつ 関連処理・通信も行わない。無駄な二重設定を作らない）。

| タブ | 項目 | 対応設定 |
|---|---|---|
| **全般** | 更新間隔（秒） | `RefreshIntervalSeconds`（既存） |
| | 適応更新間隔（放置時に自動延長）ON-OFF | `AdaptiveRefreshEnabled`（F-10） |
| | Windows スタートアップ登録 | （既存） |
| **表示項目** | GitHub Copilot セクション | `GitHubCopilotEnabled`（既存） |
| | Codex セクション | `CodexEnabled`（既存） |
| | ペース行（Claude / Codex。OFF で計算もスキップ） | `PaceEnabled`（F-05/06） |
| | コスト行（Claude Code / Codex CLI のローカル集計。OFF でスキャンも停止） | `CostScanEnabled`（F-08/09） |
| | 稼働ステータスバッジ（ON で各社公式ステータスページへ 10 分毎に読み取りアクセス。通信先 URL を画面に明記） | `StatusMonitorEnabled`（F-12、既定 OFF） |
| | リセット時刻の表示形式（相対「残り1時間13分」/ 絶対「16:00 リセット」） | `ResetDisplayMode`（F-04） |
| **外観** | トレイアイコン形式（2段バー / ドーナツ） | `TrayIconStyle`（F-01） |
| | 色の閾値（注意% / 警告%） | `CautionThresholdPercent` / `WarningThresholdPercent`（F-03） |
| | バー上の閾値マーカー表示 | `ShowThresholdMarkers`（F-03） |
| | オーバーレイ不透明度スライダー | `WindowOpacity`（既存。現在 settings.json 直編集のみのため UI 化） |
| **通知** | 通知 ON-OFF | `NotificationsEnabled`（F-07） |
| | 通知閾値（カンマ区切り、例: `70, 90`） | `NotificationThresholds`（F-07） |
| | リセット完了通知 ON-OFF | `NotifyOnReset`（F-07） |
| | 100% 到達通知 ON-OFF | `NotifyOnExhausted`（F-07） |
| | 注記: Windows のフォーカスアシスト設定により表示が抑制される場合がある | — |

実装メモ:

- `SettingsWindow.xaml` を `TabControl` ベースに書き換え。ウィンドウサイズは 480×560 程度に拡大し、各タブ内は `ScrollViewer` で将来の項目増に耐える構成にする。
- 表示項目タブのチェック変更は保存時に `MainViewModel` へ反映され、対応する `Visibility` プロパティ（F-06 の `SessionPaceVisibility` / `CodexPaceVisibility`、F-09 の `CostRowVisibility`。いずれも既存 `GitHubSectionVisibility` と同形式）が切り替わる。オーバーレイは `SizeToContent="Height"` のため行の増減に自動追従する（既存挙動）。
- コスト行 OFF 時は `CostScanService` のタイマーを停止（次回スキャンを予約しない）。ステータスバッジ OFF 時は `ProviderStatusService` の `HttpClient` ポーリングを停止。「非表示なのに裏で動き続ける」状態を作らない。

### 7.3 トレイ

- アイコン: 2段バー（F-01）。ツールチップ: `セッション: 75% (+8%)  週間: 10%`
- 右クリックメニュー: 既存項目 + `更新を一時停止 ▸ (30分/1時間/3時間/再開)`（F-11）

## 8. 実装フェーズ計画と依存関係

```text
P1  F-01 トレイ2段バー ──┐
    F-02 stale 表示 ──────┤（相互独立。F-01 と F-02 はトレイ描画引数で接点あり）
    F-03 閾値・マーカー ──┤
    F-04 リセット表示 ────┴─ ScrapedUsageData.SessionResetAt/WeeklyResetAt 追加（基盤）
                                │
P2  F-05 ペース計算 ←──────────┘（remainingMinutes を利用。F-04 の resets_at 保持が前提）
    F-06 ペース表示 ← F-05
    F-07 通知      ← F-04（リセット検知に ResetAt を使用）
P3  F-08 コストスキャン（独立。どのフェーズとも依存なし）
    F-09 コスト表示 ← F-08
P4  F-10 適応更新 / F-11 スヌーズ / F-12 ステータス監視（相互独立）
```

- 1 フェーズ = 1 リリース（`v*` タグ → GitHub Release）を推奨。各フェーズ完了時に手動確認（§9）を実施。
- コミットは Conventional Commits 準拠で機能単位に分割（例: `feat(tray): 2段バーアイコンを追加`、`feat(pace): UsagePaceCalculator を追加`）。

## 9. テスト観点（手動確認項目）

自動テストプロジェクトが無いため、以下を各フェーズのリリース前に exe 起動で確認する。F-05（純関数）は将来のテスト導入時の最優先対象。

| 対象 | 確認項目 |
|---|---|
| F-01 | 100%/125%/150% DPI でバーが視認できるか。0% / 100% / stale の描画。ドーナツへの切替反映 |
| F-02 | ネットワーク切断 → 前回値のまま 55% 減光になるか。復帰 → 通常表示に戻るか |
| F-03 | 閾値変更がトレイ色・ドット色・マーカー位置すべてに反映されるか（一本化の確認） |
| F-04 | 相対/絶対の切替。`resets_at` null（未使用アカウント）時に相対へフォールバックするか |
| F-05/06 | 窓開始直後（3%未満）に非表示か。使用率 0% で「リセットまで余裕」になるか。ETA 表記の日跨ぎ |
| F-07 | 閾値 70 を跨いだ最初の取得でのみ通知されるか（再通知なし）。リセット後に閾値状態がクリアされるか。stale 中に誤発火しないか |
| F-08/09 | `.claude\projects` 無し環境で行が非表示か。大量ログ（数百MB）でも UI が固まらないか。2 回目スキャンがキャッシュで高速か。`costUSD` 無しログで `$` 非表示か |
| F-10 | 放置 1 時間後に間隔が 2 分に伸びているか（StatusText 等にデバッグ表示を仮置きして確認）。手動更新で即 30 秒に戻るか |
| F-11 | スヌーズ中に WebView2 巡回が止まるか（タスクマネージャで msedgewebview2 の活動確認）。手動更新で解除されるか |
| F-12 | OFF（既定）で一切通信しないか。ON かつ正常時（indicator=none）は何も表示しないか |
| 共通 | 旧 settings.json のまま起動して既定値が補完されるか。長時間稼働（1 日）でメモリが漸増しないか |

## 10. リスク・注意事項

| # | リスク | 対応 |
|---|---|---|
| 1 | Claude Code ログの `costUSD` フィールドは新しいバージョンで出力されない場合がある | コストは「取れた分のみ合算」とし、無ければトークンのみ表示（自前単価推計をしない方針を仕様に明記済み） |
| 2 | Codex セッションログの形式変動 | Codex 集計は努力目標。パース失敗はスキップし、Claude Code 集計へ影響させない |
| 3 | ペースは線形外挿であり、実際の消費は突発的 | 表示文言を「予定比」とし断定表現を避ける。ETA は「〜頃」表記 |
| 4 | `ShowBalloonTip` は OS 設定（フォーカスアシスト・通知OFF）で抑制される | 設定画面に注記。将来トースト API へ移行できるよう `NotificationService` を分離 |
| 5 | ステータス監視は外部通信先が増える | 既定 OFF。取得のみ・送信なし・認証なしを仕様に明記（CLAUDE.md 整合） |
| 6 | コストスキャンのディスク I/O（初回フルスキャン） | 増分キャッシュ + 直列実行 + 15 分間隔。初回は起動 60 秒後に遅延実行 |
| 7 | トレイアイコン 32px での 2 段バー視認性 | ドーナツ型を設定で残す。実機確認で下段高さを調整 |
| 8 | 週間ペースの精度（Claude の週間リセットは口座ごとに異なる曜日・時刻） | API の `resets_at` を直接使うためローカル推定（月曜固定）より正確。ローカル計算フォールバック時はペース非表示 |
| 9 | 設定画面の項目増加による UI 破綻 | TabControl 化（§7.2）を P1 と同時に実施 |

## 11. ライセンス表記

CodexBar（steipete 氏）・Win-CodexBar（Finesssee 氏）はいずれも MIT License。本仕様はロジック（ペース計算式・閾値・描画パラメータ）の移植であり Swift / Rust コードの複製ではないが、移植元を明示するコメント（例: `// CodexBar (MIT, steipete) UsagePace.swift / Win-CodexBar (MIT, Finesssee) usage_pace.rs を参考に移植`）をファイルヘッダに付与し、README の謝辞に両リポジトリへのリンクを追加する。
