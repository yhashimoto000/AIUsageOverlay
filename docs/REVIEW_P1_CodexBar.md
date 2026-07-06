# P1（CodexBar 機能取込）自己コードレビュー結果書

| 項目 | 内容 |
|---|---|
| 対象 | AIUsageOverlay P1: F-01 トレイ2段バー / F-02 stale 表示 / F-03 閾値・マーカー / F-04 リセット表示切替 |
| 実施日 | 2026-07-06 |
| 実施者 | 自己レビュー（実装者による静的レビュー） |
| ビルド検証 | **未実施（重要）**。本アプリは `net9.0-windows` WPF のため Linux 環境ではビルド不可。`dotnet build` / 実機起動は Windows で別途必須。 |
| 参照仕様 | `docs/SPEC_CodexBar_Feature_Adoption.md` |

## 結論

P1 の全機能（F-01〜F-04）を仕様どおり実装した。レビューで検出した不具合 3 件はすべて修正済み。CLAUDE.md の退行防止ルール（前回値保持・SetProperty 経由・WebView2 非変更・外部送信なし・後方互換）はすべて満たしている。**ただしコンパイルは未実施のため、Windows でのビルドと §テスト結果書の実機項目の消化が残作業。**

## 1. 変更ファイル一覧

### 新規

| ファイル | 役割 |
|---|---|
| `Services/UsageLevelHelper.cs` | 使用率レベル判定と閾値色の一元管理（F-03）。色は 16進文字列で返し GDI/WPF 双方から利用 |
| `Services/TrayIconRenderer.cs` | トレイアイコン描画（`RenderDualBar` / `RenderDonut`）。App.xaml.cs から分離（F-01） |
| `Controls/ThresholdMarkerOverlay.cs` | プログレスバー上の閾値マーカー描画（`FrameworkElement` 派生、F-03） |

### 変更

| ファイル | 変更概要 |
|---|---|
| `Models/AppSettings.cs` | `TrayIconStyle` / `CautionThresholdPercent` / `WarningThresholdPercent` / `ShowThresholdMarkers` / `ResetDisplayMode` を既定値付きで追加 |
| `Models/ScrapedUsageData.cs` | `SessionResetAt` / `WeeklyResetAt`（`DateTime?`）追加（F-04 基盤） |
| `Services/Parsing/ClaudeUsageParser.cs` | `resets_at` をローカル時刻の `DateTime?` として格納（従来の残り分数変換は維持） |
| `Services/UsageService.cs` | `UpdateAndGetUsageAsync` の戻り値に `sessionResetAt` / `weeklyResetAt` を追加（7要素タプル） |
| `ViewModels/MainViewModel.cs` | stale 状態・セクション不透明度・`IsClaudeStale`・リセット表示切替（`BuildResetText`）・`GetSettings` 公開 |
| `App.xaml.cs` | `CreateSessionBitmap` 撤去→`TrayIconRenderer` 呼び分け。stale/style/再描画スキップ・`RefreshTrayIcon` 追加 |
| `MainWindow.xaml` | `ctrl:` 名前空間、各セクションの `Opacity` バインド、閾値マーカー配置、名前付け |
| `MainWindow.xaml.cs` | 色を `UsageLevelHelper` に一本化、`UpdateSessionColor`/`ApplyThresholdMarkers`、不透明度適用、トレイ即時反映 |
| `SettingsWindow.xaml` | `TabControl`（全般 / 表示項目 / 外観）へ再構成。閾値・形式・不透明度 UI 追加 |
| `SettingsWindow.xaml.cs` | 新設定の読込・保存・入力検証（閾値 0〜100・注意<警告） |
| `README.md` | §11 準拠の謝辞（CodexBar / Win-CodexBar、MIT）追記 |

## 2. レビューで検出・修正した不具合

| # | 重大度 | 事象 | 対応 |
|---|---|---|---|
| 1 | **高（破損）** | 編集の副作用で `App.xaml.cs` 末尾に NUL バイトが 2278 個混入（最終 `}` の後ろ） | NUL のみ除去し CRLF/UTF-8 を維持。除去後にファイル末尾が最終 `}` で終わることを再確認 |
| 2 | 中（機能欠落） | 設定で「トレイ形式」「閾値」を変更しても、使用率が変わらないとトレイが再描画されず旧アイコンのまま残る | `App.RefreshTrayIcon()` を新設し `MainWindow.Settings_Click` から呼び出して強制再描画（再描画スキップキーを無効化） |
| 3 | 低（警告） | `_gitHubStale` / `_codexStale` が代入のみで未参照（CS0414 相当） | フィールドと代入を削除。stale 表現は不透明度で完結、bool は Claude（トレイ減光で参照）のみ保持 |
| 4 | **高（ビルドエラー）** | `ThresholdMarkerOverlay.cs` で `Brush` が `System.Drawing.Brush` と `System.Windows.Media.Brush` の曖昧参照（CS0104）。`UseWindowsForms=true` で `System.Drawing` が暗黙 using に入るため | `Brush`/`Color`/`Pen`/`Point`/`SolidColorBrush` を WPF 側へ using エイリアスで固定。`MainWindow.xaml.cs` は該当なしを確認（Windows 実機ビルドで検出→修正） |

## 3. CLAUDE.md 退行防止チェック

| ルール | 判定 | 根拠 |
|---|---|---|
| 前回値保持（`_gitHubEverLoaded` 等）を壊さない | OK | stale は「前回値保持ルートに入ったとき」に不透明度を下げるのみで、既存の値保持ロジックは置換していない |
| プロパティ更新は `SetProperty<T>` 経由 | OK | 追加プロパティ（`IsClaudeStale` / 各 `SectionOpacity`）はすべて `SetProperty` 経由。手動 `PropertyChanged` 発火なし |
| WebView2 ライフサイクル非変更 | OK | `ClaudeApiClient` / 各 Scraper は無変更。F-04 は `ClaudeUsageParser` の拡張のみ |
| パーサは `Services/Parsing/` に配置 | OK | 新規パーサなし。既存 `ClaudeUsageParser` の格納項目追加のみ |
| 外部サーバー送信の禁止 | OK | P1 の全機能はローカル処理のみ。ネットワーク通信の追加なし |
| 後方互換（旧 settings.json） | OK | 追加設定はすべて `[JsonPropertyName]` + 既定値。未知キーは既定値で補完される |
| 関数・定義に詳細コメント | OK | 新規/変更箇所に日本語 XML ドキュメントコメントと補足コメントを付与 |

## 4. 静的検証（コンパイル前チェック）

ビルドは未実施だが、コンパイルエラーになりやすい箇所を目視確認した。

- **タプル整合**: `UpdateAndGetUsageAsync` の生成側（7要素）と `MainViewModel` の分解側（7要素）が一致。
- **using / 名前空間**: `App.xaml.cs` に `using AIUsageOverlay.Services;` を追加、未使用の `System.Drawing.Drawing2D` と `FontStyle` エイリアスを除去。`System.Drawing`（`Icon`/`Bitmap`/`IntPtr`）は保持。
- **新規型参照**: `TrayIconRenderer` / `UsageLevelHelper`（Services）、`ThresholdMarkerOverlay`（XAML `ctrl:` 名前空間 `clr-namespace:AIUsageOverlay.Controls`）の参照が一致。
- **XAML 名前付き要素**: `SessionProgressBar` / `SessionPercentBlock` / `SessionMarkers` / `GitHubMarkers` / `CodexMarkers` を追加し、code-behind から参照。
- **ファイル末尾整合**: 全変更ファイルが最終 `}` または `</Window>` で終端（NUL・欠落なし）を Read で確認。

> 注意: 上記は静的な目視確認であり、コンパイル成功を保証するものではない。Windows での `dotnet build -c Release` を必ず実施すること。

## 5. 仕様からの逸脱・スコープ判断（記録）

| # | 事象 | 理由 |
|---|---|---|
| 1 | `TrayIconRenderer.RenderDualBar/RenderDonut` の引数に `AppSettings` を追加（仕様の署名は `(sessionPercent, weeklyPercent, stale)`） | F-03 で閾値が設定可能になったため、色判定に閾値が必要。描画クラスを自己完結させる目的で `settings` を受け取る形にした |
| 2 | 設定画面は 3 タブ（全般 / 表示項目 / 外観）。「通知」タブは未作成 | 通知（F-07）は P2 スコープで、対応する設定項目がまだ無いため。P2 で追加する |
| 3 | オーバーレイ不透明度スライダー（`WindowOpacity`）を UI 化し、`MainWindow.Opacity` へ適用 | §7.2 の外観タブ項目。従来 settings.json 直編集のみで未配線だったため配線した（小規模・低リスク） |

## 6. 既知の制限・残リスク

| # | 内容 | 影響 |
|---|---|---|
| 1 | ビルド未実施 | Windows でのコンパイル確認が必須。型・XAML の未検出エラーが残る可能性 |
| 2 | 起動直後にセッション 0% かつ取得成功の場合、トレイが静的 app.ico のまま（最初の値変化まで再描画されない） | 既存挙動と同一で退行ではない。0% は視覚的に問題が小さい。必要なら初回強制描画を P 追補で検討 |
| 3 | 32×32 の下段バー（週間, 高さ 6px）の視認性 | 高 DPI 実機での確認が必要（テスト結果書 F-01 参照）。下段高さは 5〜8px で調整余地あり |
| 4 | 絶対表示は Claude の `resets_at`、Codex の既存テキストに依存 | `resets_at` が null（未使用）の場合は相対表示にフォールバック（設計どおり） |

## 7. 総合評価

P1 の実装は仕様を満たし、退行防止ルールにも適合している。検出した不具合は修正済み。**リリース前提条件は Windows でのビルド成功と、テスト結果書の実機項目（特に F-01 の DPI 視認性、F-02 の減光、F-03 の色一本化、F-04 のフォールバック）の消化。**
