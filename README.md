# AI Usage Overlay

Claude.ai の使用量（セッション・週間）を Windows 画面上に常時表示するオーバーレイアプリです。  
タスクトレイに常駐し、使用率に応じてアイコンの色がリアルタイムで変化します。

![overlay](docs/screenshot.png)

---

## 機能

- **セッション使用率**をオレンジのプログレスバーで表示
- **週間使用率**をカラーインジケーター（緑 / 黄 / 赤）で表示
- **残り時間**をリアルタイムで表示（例: "2時間13分" / "4日8時間"）
- 画面の好きな場所にドラッグして配置可能（位置は次回起動時も維持）
- Windows 起動時に自動起動（設定から ON/OFF）
- 定期自動更新（デフォルト 60 秒）＋手動更新ボタン（↺）
- **タスクトレイ常駐**（タスクバーには表示されない）
- **トレイアイコンが使用率に応じて色変化**（緑 / オレンジ / 赤）
- **トレイアイコンにカーソルを合わせると使用率を表示**（例: "セッション: 75%  週間: 10%"）

---

## 動作要件

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 (64-bit) |
| WebView2 Runtime | Windows 11 および Edge インストール済みの Windows 10 には標準搭載 |
| Claude.ai アカウント | Pro プラン推奨（Free プランでも動作します） |

> **WebView2 が未インストールの場合**  
> [Microsoft の公式ページ](https://developer.microsoft.com/ja-jp/microsoft-edge/webview2/) からインストールしてください。

---

## インストール（ビルド不要）

1. [Releases](../../releases) ページを開く
2. 最新バージョンの `AIUsageOverlay.exe` をダウンロード
3. ダウンロードしたフォルダで `AIUsageOverlay.exe` をダブルクリック

インストーラー不要・単一 exe ファイルです。

---

## 初回セットアップ

アプリを起動するとタスクバーには表示されず、**タスクトレイ**（画面右下の通知領域）にアイコンが表示されます。  
オーバーレイを右クリック、またはトレイアイコンを右クリックしてメニューを開きます。

### 1. ログイン

```
右クリック → ログイン
```

表示されたブラウザウィンドウで **claude.ai にログイン**します。  
ログイン完了後、ウィンドウを閉じて ↺ ボタンを押すと使用量が反映されます。

> ログイン情報は `%TEMP%\AIUsageOverlay_WebView2` に保存され、  
> **次回以降は自動ログイン**されます（Cookie の手動入力は不要です）。

### 2. 設定（任意）

```
右クリック → 設定
```

| 設定項目 | 説明 |
|----------|------|
| 更新間隔（秒） | データ取得の間隔（最小 5 秒） |
| Windows 起動時に自動起動 | チェックで HKCU\...\Run に登録 |

---

## 使い方

### オーバーレイの操作

| 操作 | 動作 |
|------|------|
| ドラッグ | オーバーレイを好きな場所へ移動（位置は自動保存） |
| ↺ ボタン | 今すぐ更新 |
| 右クリック → 設定 | 設定画面を開く |
| 右クリック → ログイン | WebView2 ブラウザでログイン |
| 右クリック → セッションリセット | セッションタイマーをリセット |
| 右クリック → 非表示にする | オーバーレイを隠してトレイに引っ込む |
| 右クリック → 終了 | アプリを終了 |
| × ボタン / Alt+F4 | トレイに引っ込む（アプリは終了しない） |

### トレイアイコンの操作

| 操作 | 動作 |
|------|------|
| ダブルクリック | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 表示 / 非表示 | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 終了 | アプリを終了 |
| カーソルを合わせる | セッションと週間の使用率を表示 |

### トレイアイコンの色の意味

| 色 | セッション使用率 |
|----|----------------|
| 🟢 緑 | 0 〜 49%（通常） |
| 🟠 オレンジ | 50 〜 79%（注意） |
| 🔴 赤 | 80 〜 100%（警告） |

### ステータス表示の見方

| 表示 | 意味 |
|------|------|
| `API: HH:mm` | claude.ai からリアルタイムデータを取得中 |
| `エラー: 未ログイン` | ログインが必要（右クリック → ログイン） |
| `更新: HH:mm` | ローカル計測モード（ログイン前） |

---

## 自分でビルドする場合

### 必要なもの

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 以降（WPF ワークロード）

### ビルド手順

```bash
git clone https://github.com/<your-name>/Claude-UsageTool.git
cd Claude-UsageTool/AIUsageOverlay
dotnet restore
dotnet build -c Release
```

### 単一 exe として発行する

```bash
dotnet publish AIUsageOverlay/AIUsageOverlay.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -o publish/
```

`publish/AIUsageOverlay.exe` が生成されます。

---

## アーキテクチャ

```
AIUsageOverlay/
├── Resources/
│   └── app.ico              # トレイアイコン（カスタム）
├── Models/
│   ├── AppSettings.cs       # 設定（更新間隔・ウィンドウ位置）
│   ├── ScrapedUsageData.cs  # API レスポンスの中間モデル
│   └── UsageRecord.cs       # ローカル時間計測レコード
├── Services/
│   ├── ClaudeApiClient.cs   # WebView2 で claude.ai API を呼び出す
│   ├── ClaudeWebScraper.cs  # （旧 HTTP 方式・予備）
│   └── UsageService.cs      # 設定・API・フォールバックの統合
├── ViewModels/
│   └── MainViewModel.cs     # INotifyPropertyChanged / DispatcherTimer
├── App.xaml(.cs)            # 起動・トレイアイコン管理・動的アイコン生成
├── MainWindow.xaml(.cs)     # 常時最前面オーバーレイ
├── SettingsWindow.xaml(.cs) # 設定ダイアログ
└── LoginWindow.xaml(.cs)    # WebView2 ログインウィンドウ
```

**データ取得フロー:**
1. WebView2 が `https://claude.ai/settings/usage` に自動アクセス
2. ページ内の `fetch()` 呼び出しを JavaScript で傍受
3. `/api/organizations/{id}/usage` のレスポンス JSON をパース
4. `five_hour.utilization` → セッション %、`seven_day.utilization` → 週間 %

**トレイアイコン更新フロー:**
1. `DispatcherTimer` が定期的に `MainViewModel.RefreshUsageAsync()` を呼び出す
2. `SessionPercent` プロパティが変化すると `PropertyChanged` イベントが発火
3. `App` がイベントを受け取り `CreateSessionBitmap()` で 32×32 ビットマップを生成
4. `NotifyIcon.Icon` を差し替えてトレイアイコンを更新、`Text` にツールチップを設定

---

## プライバシー・セキュリティ

- **外部サーバーへの送信なし** — データは claude.ai との直接通信のみ
- **認証情報の保存場所** — `%TEMP%\AIUsageOverlay_WebView2`（WebView2 の標準プロファイル）
- **設定ファイル** — `%AppData%\AIUsageOverlay\settings.json`

---

## 謝辞

トレイアイコンの2段バーデザイン・stale 減光表現・使用率レベルの閾値色など、一部の UI ロジックは
以下のオープンソース実装（いずれも MIT License）を参考に移植しています。

- [CodexBar](https://github.com/steipete/CodexBar)（steipete 氏、macOS / Swift）
- [Win-CodexBar](https://github.com/Finesssee/Win-CodexBar)（Finesssee 氏、Windows / Rust + Tauri）

Swift / Rust コードの複製ではなくロジック（描画パラメータ・閾値）の移植であり、
該当ファイルのヘッダに移植元を明記しています。

## ライセンス

MIT License
