using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using AIUsageOverlay.Services;
// WinForms / System.Drawing との名前衝突を解消するエイリアス
using Application  = System.Windows.Application;
using MessageBox   = System.Windows.MessageBox;

namespace AIUsageOverlay
{
    /// <summary>
    /// App のエントリーポイント。
    ///
    /// このクラスが担当する責務:
    ///   1. ShutdownMode を OnExplicitShutdown に設定してトレイ常駐アプリとして動作させる
    ///   2. System.Windows.Forms.NotifyIcon を初期化してタスクトレイにアイコンを表示する
    ///   3. MainWindow の生成・表示を管理する
    ///   4. トレイアイコンのダブルクリック / 右クリックメニューで表示切替・終了を提供する
    ///   5. セッション使用率の変化を検知してトレイアイコン色とツールチップを動的更新する
    ///   6. グローバル例外ハンドラで未処理例外をユーザーに通知する
    /// </summary>
    public partial class App : Application
    {
        // ────────────────────────────────────────────────────────────────
        // Win32 P/Invoke
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// GDI アイコンハンドルを解放する Win32 API。
        /// Bitmap.GetHicon() で作成したハンドルは Icon.Dispose() では解放されないため
        /// 手動でこの関数を呼ぶ必要がある。
        /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>タスクトレイに表示するアイコンオブジェクト</summary>
        private NotifyIcon? _notifyIcon;

        /// <summary>アプリのメインウィンドウ（トレイから表示/非表示を制御する）</summary>
        private MainWindow? _mainWindow;

        /// <summary>
        /// 現在トレイに表示中のアイコンの GDI ハンドル。
        /// 次回更新時に DestroyIcon() で解放してメモリリークを防ぐ。
        /// </summary>
        private IntPtr _currentIconHandle = IntPtr.Zero;

        /// <summary>
        /// 直前にトレイへ描画した状態のキー（"session|weekly|stale|style"）。F-01。
        /// 同一状態では再描画をスキップして GDI 負荷とちらつきを抑える
        /// （トレイは1アイコンのため CodexBar の LRU キャッシュはキー1件で足りる）。
        /// </summary>
        private string? _lastTrayKey;

        // ────────────────────────────────────────────────────────────────
        // 公開プロパティ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 終了フラグ。
        /// true のとき MainWindow.OnClosing でウィンドウを隠さずにそのまま閉じる。
        /// ExitApplication() を呼ぶ前に true にセットする。
        /// </summary>
        internal static bool IsExiting { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 起動 / 終了
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// アプリケーション起動時に呼ばれるハンドラ（App.xaml の Startup="App_Startup" に対応）。
        ///
        /// 処理順序:
        ///   1. ShutdownMode を OnExplicitShutdown に変更する
        ///   2. グローバル例外ハンドラを登録する
        ///   3. NotifyIcon を初期化する（初期アイコンは静的アイコン）
        ///   4. MainWindow を生成して表示する
        ///   5. MainViewModel の PropertyChanged を購読して動的更新を開始する
        /// </summary>
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // ウィンドウを全て閉じてもアプリを終了しない（トレイ常駐のため）
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // UI スレッドの未処理例外をキャッチしてダイアログ表示する
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    $"予期しないエラーが発生しました:\n{ex.Exception.Message}",
                    "AIUsageOverlay - エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };

            // トレイアイコンをセットアップする（起動時は静的アイコンで表示）
            InitializeNotifyIcon();

            // メインウィンドウを生成して表示する
            _mainWindow = new MainWindow();
            _mainWindow.Show();

            // ViewModel の使用率変化を購読してトレイアイコンを動的更新する
            _mainWindow.ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // F-07: 通知の送出先として NotifyIcon を ViewModel（NotificationService）へ注入する
            if (_notifyIcon != null)
                _mainWindow.ViewModel.AttachNotifier(_notifyIcon);
        }

        /// <summary>
        /// アプリケーションを安全に終了する。
        /// NotifyIcon を破棄してから Shutdown() を呼ぶ。
        /// MainWindow.Exit_Click や トレイメニューの「終了」から呼ばれる。
        /// </summary>
        internal void ExitApplication()
        {
            // OnClosing でウィンドウを隠さないようにフラグを立てる
            IsExiting = true;

            // トレイアイコンを解放する（破棄しないとアイコンがゴーストとして残る）
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            // 残存するアイコンハンドルを解放する
            if (_currentIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }

            // アプリケーションを終了する
            Shutdown();
        }

        /// <summary>
        /// メインウィンドウの表示 / 非表示をトグルする。
        /// トレイアイコンのダブルクリックおよび右クリックメニューから呼ばれる。
        /// </summary>
        internal void ToggleMainWindow()
        {
            if (_mainWindow == null) return;

            // WinForms のコールバックから呼ばれることがあるため Dispatcher 経由で実行する
            Dispatcher.Invoke(() =>
            {
                if (_mainWindow.IsVisible)
                {
                    // 表示中 → 非表示にする
                    _mainWindow.Hide();
                }
                else
                {
                    // 非表示 → 表示して最前面に持ってくる
                    _mainWindow.Show();
                    _mainWindow.Activate();
                }
            });
        }

        /// <summary>
        /// アプリケーション終了時に NotifyIcon と GDI リソースを確実に破棄する。
        /// ExitApplication() 経由の場合はすでに破棄済みのため二重破棄にはならない。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            if (_currentIconHandle != IntPtr.Zero)
                DestroyIcon(_currentIconHandle);

            _notifyIcon?.Dispose();
            base.OnExit(e);
        }

        // ────────────────────────────────────────────────────────────────
        // NotifyIcon 初期化
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// System.Windows.Forms.NotifyIcon を初期化する。
        ///
        /// 設定内容:
        ///   - アイコン: 起動時は静的リソース (Resources/app.ico) を使用
        ///               使用率取得後は動的生成アイコンに切り替わる
        ///   - ツールチップ: 起動直後は "Claude Usage Overlay"、データ取得後は使用率を表示
        ///   - ダブルクリック: 表示 / 非表示トグル
        ///   - 右クリックメニュー: 「表示 / 非表示」「終了」
        /// </summary>
        private void InitializeNotifyIcon()
        {
            // pack:// URI で WPF リソースとして埋め込んだ ICO ファイルを読み込む（初期表示用）
            var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
            var iconStream = GetResourceStream(iconUri).Stream;
            var icon = new Icon(iconStream);

            _notifyIcon = new NotifyIcon
            {
                Icon    = icon,
                Text    = "Claude Usage Overlay",
                Visible = true   // 起動直後からトレイに表示する
            };

            // ダブルクリックで表示 / 非表示をトグルする
            _notifyIcon.DoubleClick += (_, _) => ToggleMainWindow();

            // 右クリックメニューを構築する
            _notifyIcon.ContextMenuStrip = BuildTrayContextMenu();
        }

        /// <summary>
        /// トレイアイコンの右クリックメニューを構築して返す。
        ///
        /// メニュー構成:
        ///   ● 表示 / 非表示  ← クリックでオーバーレイの表示切替
        ///   ─ セパレータ ─
        ///   ● 終了           ← クリックでアプリ完全終了
        /// </summary>
        private ContextMenuStrip BuildTrayContextMenu()
        {
            var menu = new ContextMenuStrip();

            var showHideItem = new ToolStripMenuItem("表示 / 非表示");
            showHideItem.Click += (_, _) => ToggleMainWindow();

            // F-11: 更新を一時停止（スヌーズ）サブメニュー
            var snoozeItem = new ToolStripMenuItem("更新を一時停止");
            AddSnoozeMenuItem(snoozeItem, "30分", TimeSpan.FromMinutes(30));
            AddSnoozeMenuItem(snoozeItem, "1時間", TimeSpan.FromHours(1));
            AddSnoozeMenuItem(snoozeItem, "3時間", TimeSpan.FromHours(3));
            snoozeItem.DropDownItems.Add(new ToolStripSeparator());
            var resumeItem = new ToolStripMenuItem("再開");
            resumeItem.Click += (_, _) => _mainWindow?.ViewModel.ClearSnooze();
            snoozeItem.DropDownItems.Add(resumeItem);

            var exitItem = new ToolStripMenuItem("終了");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(showHideItem);
            menu.Items.Add(snoozeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        /// <summary>スヌーズのサブメニュー項目を 1 つ追加する（F-11）。</summary>
        private void AddSnoozeMenuItem(ToolStripMenuItem parent, string label, TimeSpan duration)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => _mainWindow?.ViewModel.SnoozeFor(duration);
            parent.DropDownItems.Add(item);
        }

        // ────────────────────────────────────────────────────────────────
        // 動的アイコン更新
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ViewModel のプロパティ変更を検知するハンドラ。
        /// SessionPercent / WeeklyPercent / IsClaudeStale が変化したときにトレイアイコンを更新する。
        /// （F-02 で stale の変化もトリガーに追加した）
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(ViewModels.MainViewModel.SessionPercent)
                                    or nameof(ViewModels.MainViewModel.WeeklyPercent)
                                    or nameof(ViewModels.MainViewModel.IsClaudeStale)
                                    or nameof(ViewModels.MainViewModel.IsSnoozing)))
                return;

            if (_mainWindow == null || _notifyIcon == null) return;

            // NotifyIcon の操作は UI スレッドから行う
            Dispatcher.Invoke(UpdateTrayIcon);
        }

        /// <summary>
        /// 設定変更（トレイ形式・閾値色など使用率に依存しない項目）を即座にトレイへ反映する（F-01/F-03）。
        /// 使用率が変わらないと <see cref="OnViewModelPropertyChanged"/> が走らないため、
        /// 再描画スキップ用のキーを無効化してから強制的に再描画する。
        /// SettingsWindow 保存後に MainWindow から呼ばれる。
        /// </summary>
        internal void RefreshTrayIcon()
        {
            _lastTrayKey = null;                 // 同一状態スキップを解除して確実に再描画させる
            Dispatcher.Invoke(UpdateTrayIcon);
        }

        /// <summary>
        /// セッション/週間使用率・stale 状態に応じてトレイアイコンとツールチップを更新する（F-01/F-02）。
        ///
        /// アイコン形式は <see cref="Models.AppSettings.TrayIconStyle"/> により切替（デザイン刷新 1e）:
        ///   "ring"（既定）→ ストローク弧 + 中央%テキスト
        ///   "dualBar"     → 上段=セッション/下段=週間の2段バー改
        ///   "donut"       → 従来のドーナツ + 中央%テキスト
        ///   "numeric"     → %数値 + 下部ミニバー
        /// 色は <see cref="UsageLevelHelper"/> の閾値で決定し、stale 時は減光する。
        ///
        /// ツールチップ（NotifyIcon.Text）: "セッション: 75%  週間: 10%"（最大 63 文字にクランプ）。
        /// 同一状態（session|weekly|stale|style）では再描画をスキップする。
        /// </summary>
        private void UpdateTrayIcon()
        {
            if (_notifyIcon == null || _mainWindow == null) return;

            var vm       = _mainWindow.ViewModel;
            int session  = (int)vm.SessionPercent;
            int weekly   = (int)vm.WeeklyPercent;
            // F-11: スヌーズ中も stale と同じ減光でトレイに表す
            bool stale   = vm.IsClaudeStale || vm.IsSnoozing;
            var settings = vm.GetSettings();
            string style = settings.TrayIconStyle;

            // ── 再描画スキップ判定（同一状態なら何もしない）──
            var key = $"{session}|{weekly}|{stale}|{style}";
            if (key == _lastTrayKey) return;
            _lastTrayKey = key;

            // ── ツールチップを更新する ──
            var tooltip = $"セッション: {session}%  週間: {weekly}%";
            // NotifyIcon.Text は最大 63 文字の制限があるためクランプする
            _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

            // ── アイコンを動的生成して更新する（デザイン刷新 1e: 既定はリング）──
            using var bitmap = style switch
            {
                "donut"   => TrayIconRenderer.RenderDonut(session, stale, settings),
                "dualBar" => TrayIconRenderer.RenderDualBar(session, weekly, stale, settings),
                "numeric" => TrayIconRenderer.RenderNumeric(session, stale, settings),
                _         => TrayIconRenderer.RenderRing(session, stale, settings), // "ring"（既定）
            };
            var newHandle = bitmap.GetHicon();

            // 新しいアイコンをセットする
            _notifyIcon.Icon = Icon.FromHandle(newHandle);

            // 前回のアイコンハンドルを解放する（GDI リソースリーク防止）
            if (_currentIconHandle != IntPtr.Zero)
                DestroyIcon(_currentIconHandle);

            _currentIconHandle = newHandle;
        }
    }
}
