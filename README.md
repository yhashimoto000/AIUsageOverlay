# AI Usage Overlay

Claude.ai、GitHub Copilot、Codex の使用量を Windows 画面上に常時表示するオーバーレイアプリです。
タスクトレイに常駐し、使用率・取得状態に応じてアイコン表示が変化します。

---

## 機能

- Claude.ai の**セッション・週間使用率**、GitHub Copilot の**AI credits**、
  Codex の**週間使用率**を表示
- **残り時間・リセット時刻・予定比・枯渇予測**を表示
- 縦積み、コンパクト、詳細のオーバーレイ表示を切り替え可能
- 使用率推移のスパークライン、stale（取得失敗中）の減光表示
- 画面の好きな場所にドラッグして配置可能（位置は次回起動時も維持）
- Windows 起動時に自動起動（設定から ON/OFF）
- 使用量の定期自動更新（デフォルト 30 秒、適応更新間隔あり）＋手動更新ボタン（↺）
- 使用量更新の30分・1時間・3時間スヌーズ
- 使用率の閾値超過、上限到達、リセット完了をトレイ通知
- **タスクトレイ常駐**（タスクバーには表示されない）
- **トレイアイコンを4形式から選択**（リング / 2段バー / ドーナツ / 数字）
- **トレイアイコンにカーソルを合わせると使用率を表示**（例: "セッション: 75%  週間: 10%"）
- GitHub Releases を利用した**新バージョン確認**（通知とReleaseページへの案内）

---

## 動作要件

| 項目 | 内容 |
|------|------|
| OS | Windows 10 / 11 (64-bit) |
| WebView2 Runtime | Windows 11 および Edge インストール済みの Windows 10 には標準搭載 |
| アカウント | 表示するサービスの Claude.ai / GitHub / ChatGPT アカウント |

> **WebView2 が未インストールの場合**  
> [Microsoft の公式ページ](https://developer.microsoft.com/ja-jp/microsoft-edge/webview2/) からインストールしてください。

---

## インストール（ビルド不要）

1. [Releases](https://github.com/yhashimoto000/AIUsageOverlay/releases) ページを開く
2. 最新バージョンの `AIUsageOverlay_vX.Y.Z.zip` をダウンロード
3. zip を任意のフォルダへ展開
4. 展開先の `AIUsageOverlay.exe` をダブルクリック

インストーラーは不要です。`AIUsageOverlay.exe` と同じフォルダにある DLL も実行に必要なため、
exe だけを移動せず、展開したフォルダごと使用してください。

Release の `checksums.txt` には zip の SHA256 が記載されています。必要に応じて
`Get-FileHash AIUsageOverlay_vX.Y.Z.zip -Algorithm SHA256` の結果と照合できます。

---

## 初回セットアップ

アプリを起動するとタスクバーには表示されず、**タスクトレイ**（画面右下の通知領域）にアイコンが表示されます。  
オーバーレイを右クリック、またはトレイアイコンを右クリックしてメニューを開きます。

### 1. ログイン

```
右クリック → ログイン - <サービス>
```

表示するサービスに応じて、右クリックメニューからログイン画面を開きます。

| メニュー | ログイン先 |
|----------|------------|
| ログイン - Claude | claude.ai |
| ログイン - GitHub | GitHub Copilot |
| ログイン - Codex | ChatGPT Codex |

ログイン完了後、ウィンドウを閉じて ↺ ボタンを押すと使用量が反映されます。

> ログイン情報は `%TEMP%\AIUsageOverlay_WebView2` に保存され、  
> **次回以降は自動ログイン**されます（Cookie の手動入力は不要です）。

### 2. 設定（任意）

```
右クリック → 設定
```

| 設定項目 | 説明 |
|----------|------|
| 更新間隔（秒） | 使用量データ取得の基本間隔（最小 5 秒） |
| 適応更新間隔 | 操作状況・表示状態・電源状態に応じて取得間隔を延長 |
| Windows 起動時に自動起動 | チェックで HKCU\...\Run に登録 |
| 自動で更新を確認する | GitHub Releases を24時間ごとに確認 |
| 表示項目 | Copilot / Codex、リセット時刻、ペース、スパークラインの表示設定 |
| 外観 | レイアウト、トレイアイコン、色の閾値、マーカー、不透明度 |
| 通知 | 使用率閾値、リセット完了、100%到達の通知設定 |

### 3. アプリの更新確認

設定画面の「今すぐ確認」、またはトレイメニューの「更新を確認」から最新版を確認できます。
新しいバージョンがある場合はトレイ通知とメニューで案内され、クリックするとGitHub Releaseページが開きます。

現在のP5では、アプリが更新ファイルを自動ダウンロード・自動適用することはありません。
Releaseページからzipを取得し、終了した旧版のフォルダを新版で置き換えてください。
公式Release版はタグからバージョンが埋め込まれるため、更新後の同一バージョンを再通知しません。

---

## 使い方

### オーバーレイの操作

| 操作 | 動作 |
|------|------|
| ドラッグ | オーバーレイを好きな場所へ移動（位置は自動保存） |
| ↺ ボタン | 今すぐ更新 |
| 右クリック → 設定 | 設定画面を開く |
| 右クリック → ログイン - Claude / GitHub / Codex | WebView2ブラウザで各サービスへログイン |
| 右クリック → セッションリセット | セッションタイマーをリセット |
| 右クリック → 非表示にする | オーバーレイを隠してトレイに引っ込む |
| 右クリック → 終了 | アプリを終了 |
| × ボタン / Alt+F4 | トレイに引っ込む（アプリは終了しない） |

### トレイアイコンの操作

| 操作 | 動作 |
|------|------|
| ダブルクリック | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 表示 / 非表示 | オーバーレイの表示 / 非表示をトグル |
| 右クリック → 更新を一時停止 | 使用量更新を30分 / 1時間 / 3時間停止、または再開 |
| 右クリック → 更新を確認 | アプリの最新バージョンを確認。検知済みならReleaseページを開く |
| 右クリック → 終了 | アプリを終了 |
| カーソルを合わせる | セッションと週間の使用率を表示 |

### トレイアイコンの色の意味

| 色 | セッション使用率 |
|----|----------------|
| 緑 | 0 〜 49%（通常） |
| オレンジ | 50 〜 79%（注意） |
| 赤 | 80 〜 100%（警告） |

注意・警告の閾値は設定画面で変更できます。

### ステータス表示の見方

| 表示 | 意味 |
|------|------|
| `API: HH:mm` | Claude.aiから使用量データを取得済み |
| `エラー: 未ログイン` | ログインが必要（右クリック → ログイン） |
| `取得中...` | 使用量データを更新中 |
| `一時停止中（〜HH:mm）` | 指定時刻まで使用量更新をスヌーズ中 |

---

## 自分でビルドする場合

### 必要なもの

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 以降（WPF ワークロード）

### ビルド手順

```bash
git clone https://github.com/yhashimoto000/AIUsageOverlay.git
cd AIUsageOverlay/AIUsageOverlay
dotnet restore
dotnet build -c Release
```

### ローカル配布用に発行する

```bat
build-release.bat
```

`publish/AIUsageOverlay.exe` と必要なDLL、`AIUsageOverlay_release.zip` が生成されます。
引数を省略した場合はcsprojの既定バージョンが使われます。任意の版数を使う場合は
`build-release.bat X.Y.Z`の形式で3成分SemVerを指定してください。

GitHub公式Releaseは`v*`タグのpushで`.github/workflows/release.yml`が生成します。
タグを単一の真実源としてバージョンを注入し、self-contained zipと`checksums.txt`を添付します。
`build-release.bat`のframework-dependent zipはローカル確認用であり、GitHub Releaseへ手動添付しないでください。

---

## アーキテクチャ

```
AIUsageOverlay/
├── Resources/
│   └── app.ico              # トレイアイコン（カスタム）
├── Models/
│   ├── AppSettings.cs       # 表示・通知・更新確認などの設定
│   ├── *UsageData.cs        # Claude / Copilot / Codex の使用量モデル
│   ├── UsagePace.cs         # 予定比・枯渇予測
│   └── UpdateInfo.cs        # GitHub Release の更新情報
├── Services/
│   ├── ClaudeApiClient.cs   # WebView2 で claude.ai API を呼び出す
│   ├── GitHubWebScraper.cs  # GitHub Copilot 使用量取得
│   ├── CodexWebScraper.cs   # Codex 使用量取得
│   ├── Parsing/             # 各サービスJSON・Release・SemVerの純粋パーサ
│   ├── UpdateCheckService.cs # GitHub Release取得とバージョン比較
│   └── UsageService.cs      # 設定・取得・フォールバックの統合
├── ViewModels/
│   └── MainViewModel.cs     # INotifyPropertyChanged / DispatcherTimer
├── App.xaml(.cs)            # 起動・トレイアイコン管理・動的アイコン生成
├── MainWindow.xaml(.cs)     # 常時最前面オーバーレイ
├── SettingsWindow.xaml(.cs) # 設定ダイアログ
└── LoginWindow.xaml(.cs)    # WebView2 ログインウィンドウ
```

**使用量データ取得フロー:**
1. 各サービスの認証済みWebView2セッションで使用量ページへアクセス
2. ページ内の通信またはレスポンスを取得
3. `Services/Parsing/`のサービス別ParserでJSONを解析
4. `UsageService`と`MainViewModel`を経由して表示を更新

**アプリ更新確認フロー:**
1. 起動30秒後、以降は6時間タイマーと24時間ゲートで確認
2. `UpdateCheckService`がGitHub Releasesの公開メタデータをGET
3. 実行中アプリと最新版を厳密な3成分SemVerで比較
4. 新版がある場合だけトレイ通知し、GitHub Releaseページを案内

**トレイアイコン更新フロー:**
1. `DispatcherTimer` が定期的に `MainViewModel.RefreshUsageAsync()` を呼び出す
2. `SessionPercent` プロパティが変化すると `PropertyChanged` イベントが発火
3. `App`がイベントを受け取り、`TrayIconRenderer`で選択形式の32×32ビットマップを生成
4. `NotifyIcon.Icon` を差し替えてトレイアイコンを更新、`Text` にツールチップを設定

---

## プライバシー・セキュリティ

- **利用データの外部送信なし** — 使用量取得は各サービスとの直接通信に限定
- **更新確認は公開情報のGETのみ** — GitHub Releasesへ利用データ・認証情報・テレメトリを送信しない
- **認証情報の保存場所** — `%TEMP%\AIUsageOverlay_WebView2`（WebView2 の標準プロファイル）
- **設定・計測ファイル** — `%AppData%\AIUsageOverlay\`（`settings.json` / `usage.json` / `history.json`）

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
