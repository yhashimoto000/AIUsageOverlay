# P4（CodexBar 機能取込）自己コードレビュー結果書

| 項目 | 内容 |
|---|---|
| 対象 | AIUsageOverlay P4: F-10 適応更新間隔 / F-11 スヌーズ（F-12 稼働ステータス監視はスキップ） |
| ブランチ | `feat/p4-ops`（想定。master から分岐） |
| 実施日 | 2026-07-06 |
| ビルド検証 | **未実施**。`net9.0-windows` WPF のため Linux 環境ではビルド不可。Windows で `dotnet build -c Release` 必須 |
| 参照 | `docs/SPEC_CodexBar_Feature_Adoption.md` §F-10/F-11 |

## 結論

F-10・F-11 を実装した。F-12（稼働ステータス監視）はユーザー決定によりスコープ外（外部通信を増やさない）。適応間隔の純関数は境界値検証で期待どおり（`TEST_P4_CodexBar.md`）。**残作業は Windows でのビルドと実機項目の消化。**

## 1. 変更ファイル一覧

### 新規

| ファイル | 役割 |
|---|---|
| `Services/AdaptiveRefreshPolicy.cs` | 操作経過・表示状態・電源制約から次回更新間隔を決める純関数 `Compute` |

### 変更

| ファイル | 変更概要 |
|---|---|
| `Models/AppSettings.cs` | `AdaptiveRefreshEnabled`（既定 true）を追加 |
| `ViewModels/MainViewModel.cs` | 適応間隔（`_lastInteractionAt` / `IsOverlayVisible` / `NotifyUserInteraction` / `UpdateAdaptiveInterval` / `IsPowerConstrained`）、タイマー Tick 先頭で間隔再計算。スヌーズ（`SnoozeUntil` / `IsSnoozing` / `SnoozeFor` / `ClearSnooze`）、`RefreshUsageAsync` 冒頭でスヌーズ中は即 return |
| `App.xaml.cs` | トレイ右クリックに「更新を一時停止（30分/1時間/3時間/再開）」、スヌーズ中はトレイ減光、`IsSnoozing` 変化でトレイ再描画 |
| `MainWindow.xaml.cs` | 操作フック（ドラッグ・手動更新・設定）で `NotifyUserInteraction`、`IsVisibleChanged` で `IsOverlayVisible` 更新、手動更新で `ClearSnooze` |
| `SettingsWindow.xaml(.cs)` | 「全般」タブに適応更新トグル（読込/保存） |

## 2. レビューで対応した事項

| # | 重大度 | 事象 | 対応 |
|---|---|---|---|
| 1 | 中（負荷防止） | 固定 30 秒間隔は WebView2 取得としては高頻度で放置時に無駄 | 操作直後は基準間隔・放置/非表示/電源制約時は延長。Tick 先頭で毎回再計算（タイマー再生成しない） |
| 2 | 低（安全側） | `RefreshIntervalSeconds` が極小（5 未満）だと過負荷 | 適応計算で下限 5 秒にクランプ |
| 3 | 低（API 制約） | Windows の「バッテリー節約機能」自体を直接判定する簡易 API が無い | バッテリー駆動かつ残量 20% 未満で電源制約とみなす近似。省電力モード検出はスコープ外と明記 |
| 4 | 低（誤停止防止） | スヌーズ中に手動更新できないと不便 | 手動更新（↺）は `ClearSnooze` 後に実行。トレイ「再開」でも解除 |

## 3. CLAUDE.md 準拠チェック

| ルール | 判定 | 根拠 |
|---|---|---|
| 外部サーバー送信の禁止 | OK | P4（F-10/F-11）は送信なし。F-12（外部 GET）はスキップ |
| プロパティ更新は `SetProperty<T>` 経由 | OK | `IsSnoozing` は `SetProperty` 経由。`IsOverlayVisible` は UI からの単純フラグ（バインド対象外）で自動プロパティ |
| WebView2 ライフサイクル非変更 | OK | 取得層は無変更。間隔制御・スヌーズは取得の起動可否のみ |
| 前回値保持（ちらつき防止）を壊さない | OK | スヌーズ中は取得自体を行わず前回表示を維持 |
| 純関数はテスト容易に | OK | `AdaptiveRefreshPolicy.Compute` は副作用なし。境界値を机上検証 |
| 関数・定義に詳細コメント | OK | 新規/変更箇所に日本語コメント付与 |

## 4. 静的検証（コンパイル前チェック）

- **WinForms 型の完全修飾**: `System.Windows.Forms.SystemInformation` / `PowerLineStatus` を完全修飾し VM に using を持ち込まない。`Timer` は使用せず既存の `DispatcherTimer` を流用（間隔差し替えのみ）。
- **タイマー再生成なし**: Tick 先頭で `_refreshTimer.Interval` を更新する方式（`DispatcherTimer` は Interval 変更が即反映）。
- **スヌーズと排他**: `RefreshUsageAsync` はゲート取得後にスヌーズ判定し早期 return（`finally` でゲート解放）。手動更新は `ClearSnooze` 先行で確実に実行。
- **トレイ連動**: `IsSnoozing` を `OnViewModelPropertyChanged` のトリガーへ追加し、減光を即反映。
- **設定 UI**: 全般タブに適応トグル。読込/保存を確認（Grep）。
- **NUL/末尾整合**: 全変更ファイル NUL なしを確認。

> 静的目視でありコンパイル成功を保証しない。Windows でのビルドを必須とする。

## 5. 既知の限界

| # | 内容 | 影響 |
|---|---|---|
| 1 | 省電力（バッテリー節約）モードの直接判定は未実装 | 残量 20% 未満で近似。実害は小 |
| 2 | スヌーズは非永続 | 再起動で解除（仕様どおり） |
| 3 | `IsOverlayVisible` は MainWindow の可視性のみ | 最小化等の細かな状態は考慮しない（表示/非表示の二値） |

## 6. 総合評価

P4（F-10/F-11）は仕様を満たし、適応間隔の純ロジックは机上検証で妥当。CLAUDE.md の外部送信禁止・SetProperty・取得層非変更に適合。**リリース前提条件は Windows ビルド成功と、テスト結果書の実機項目（放置での間隔延長・手動での復帰・スヌーズ中の巡回停止と解除）の消化。**
