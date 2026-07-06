# P2（CodexBar 機能取込）自己コードレビュー結果書

| 項目 | 内容 |
|---|---|
| 対象 | AIUsageOverlay P2: F-05 ペース計算 / F-06 ペース表示 / F-07 通知 |
| ブランチ | `feat/p2-pace-notify`（想定。master から分岐） |
| 実施日 | 2026-07-06 |
| ビルド検証 | **未実施**。`net9.0-windows` WPF のため Linux 環境ではビルド不可。Windows で `dotnet build -c Release` 必須 |
| スコープ | P2 の 3 機能。F-12（ステータス監視）は別途スキップ決定済み |
| 参照 | `docs/SPEC_CodexBar_Feature_Adoption.md` §F-05/06/07 |

## 結論

F-05〜F-07 を実装した。純ロジック（ペース計算・通知状態機械）は境界値・状態遷移とも机上テストで期待どおり（`TEST_P2_CodexBar.md`）。P1/P3 で学習した型の曖昧参照（CS0104）を予防的に回避済み。**残作業は Windows でのビルドと実機項目の消化。**

## 1. 変更ファイル一覧

### 新規

| ファイル | 役割 |
|---|---|
| `Models/UsagePace.cs` | ペース段階 enum（`PaceStage`）と結果 record（`UsagePace`） |
| `Services/UsagePaceCalculator.cs` | ペース計算の純関数 `Compute`（CodexBar UsagePace.swift 移植） |
| `Services/NotificationService.cs` | 閾値超過・リセット・上限到達の通知（`ShowBalloonTip`）。窓ごとの判定状態はメモリ保持 |

### 変更

| ファイル | 変更概要 |
|---|---|
| `Models/AppSettings.cs` | `PaceEnabled` / `NotificationsEnabled` / `NotificationThresholds([70,90])` / `NotifyOnReset` / `NotifyOnExhausted` を追加 |
| `ViewModels/MainViewModel.cs` | Session/Codex の Pace（Text/Brush/Visibility）、Claude 週間は残り時間末尾へ予定比付加、通知 Evaluate 呼び出し、`AttachNotifier` |
| `MainWindow.xaml` | Claude/Codex セクションをフル幅ペース行付きへ（各セクションを 2 行化し ColumnSpan で下段追加） |
| `App.xaml.cs` | 起動時に NotifyIcon を `NotificationService` へ注入（`AttachNotifier`） |
| `SettingsWindow.xaml(.cs)` | 「表示項目」タブにペース行トグル、「通知」タブ新設（有効・閾値・リセット・上限）と読込/保存・入力検証 |

## 2. レビューで予防・対応した事項

| # | 重大度 | 事象 | 対応 |
|---|---|---|---|
| 1 | 中（ビルドエラー予防） | ペース色に `Brush` を使うと `System.Drawing.Brush`（WinForms 暗黙 using）と CS0104 衝突 | `using MediaBrush = System.Windows.Media.Brush;` エイリアスで固定。ブラシ生成は WPF 型を完全修飾（P1/P3 の教訓を先回り適用） |
| 2 | 低（誤発火防止） | stale（取得失敗）時にペース・通知が誤って出る | ペースは `isFromApi`／Codex は `data!=null` のときのみ算出。通知は `isFromApi` のときのみ Evaluate |
| 3 | 低（起動時ノイズ） | 起動直後に既に高使用率だと閾値通知が連発し得る | 各窓の初回 Evaluate はベースライン記録のみで通知しない設計 |
| 4 | 低（Codex 制約） | Codex はリセット日時（DateTime）を保持しない | Codex の通知は resetsAt=null とし、リセット検知は「30pt 以上の急落」で代替 |

## 3. CLAUDE.md 準拠チェック

| ルール | 判定 | 根拠 |
|---|---|---|
| 外部サーバー送信の禁止 | OK | P2 は送信なし。通知は OS ローカル、ペースは計算のみ |
| プロパティ更新は `SetProperty<T>` 経由 | OK | 追加プロパティ（Pace 6 点）すべて `SetProperty` 経由 |
| WebView2 ライフサイクル非変更 | OK | 取得層は無変更。P2 は取得結果の加工・表示・通知のみ |
| 前回値保持（ちらつき防止）を壊さない | OK | stale 分岐は既存の値保持ルートを維持し、その上でペース/通知を抑制 |
| 純関数はテスト容易に | OK | `UsagePaceCalculator.Compute` は副作用なし。机上テストで境界検証 |
| 関数・定義に詳細コメント | OK | 新規/変更箇所に日本語コメント付与 |
| セキュリティ／トレードオフ明示 | OK | 通知手段の選定トレードオフ（ShowBalloonTip vs WinRT Toast）は仕様書に記載済み。ペースは線形外挿の限界を明示 |

## 4. 静的検証（コンパイル前チェック）

- **型の曖昧回避**: `Brush` は `MediaBrush` エイリアス。`NotifyIcon` は `System.Windows.Forms.NotifyIcon` を完全修飾（VM に WinForms の using を持ち込まない）。
- **静的初期化順**: `_sessionPaceBrush = PaceGray`（インスタンス初期化子が静的フィールド参照）は、型初期化が先行するため安全。
- **列挙・型参照**: `Models.PaceStage` / `Models.UsagePace` / `UsageWindowKey`（Services）/ `UsagePaceCalculator`（Services）の参照整合を確認。
- **XAML バインド**: `SessionPaceText/Brush/Visibility`・`CodexPaceText/Brush/Visibility` が VM に存在（Grep 確認）。セクションを 2 行化し、ペースは `Grid.Row=1` + `ColumnSpan` でフル幅。
- **設定 UI**: 通知タブ 5 コントロール・ペーストグルの読込/保存を確認。閾値はカンマ区切りパース（0〜100 検証、空欄可）。
- **NUL/末尾整合**: 全変更ファイル NUL なしを確認。

> 静的目視でありコンパイル成功を保証しない。Windows でのビルドを必須とする。

## 5. 既知の限界

| # | 内容 | 影響 |
|---|---|---|
| 1 | ペースは線形外挿。実消費は突発的 | 文言を「予定比」「〜頃」に留め断定を避けた |
| 2 | Codex のリセット検知は急落（30pt）代替 | DateTime 未保持のため。誤検知は低確率だが皆無ではない |
| 3 | `ShowBalloonTip` は OS のフォーカスアシスト等で抑制され得る | 設定「通知」タブに注記済み |
| 4 | Claude 週間ペースは残り時間末尾へ予定比のみ付加 | 行の増加を避ける仕様。ETA は表示しない |
| 5 | 通知状態は非永続 | 再起動で再通知を許容（仕様どおり） |

## 6. 総合評価

P2 は仕様を満たし、純ロジックは机上検証で妥当。CLAUDE.md の外部送信禁止・SetProperty・取得層非変更に適合。**リリース前提条件は Windows ビルド成功と、テスト結果書の実機項目（ペース表示の各状態・通知の跨ぎ/リセット/上限・stale 時の非発火）の消化。**
