using System.Drawing;
using System.Windows;
using System.Windows.Forms;
// WinForms との名前衝突を解消するエイリアス
using Application  = System.Windows.Application;
using MessageBox   = System.Windows.MessageBox;

namespace ClaudeUsageOverlay
{
    /// <summary>
    /// App のエントリーポイント。
    ///
    /// このクラスが担当する責務:
    ///   1. ShutdownMode を OnExplicitShutdown に設定してトレイ常駐アプリとして動作させる
    ///   2. System.Windows.Forms.NotifyIcon を初期化してタスクトレイにアイコンを表示する
    ///   3. MainWindow の生成・表示を管理する
    ///   4. トレイアイコンのダブルクリック / 右クリックメニューで表示切替・終了を提供する
    ///   5. グローバル例外ハンドラで未処理例外をユーザーに通知する
    /// </summary>
    public partial class App : Application
    {
        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>タスクトレイに表示するアイコンオブジェクト</summary>
        private NotifyIcon? _notifyIcon;

        /// <summary>アプリのメインウィンドウ（トレイから表示/非表示を制御する）</summary>
        private MainWindow? _mainWindow;

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
        ///   3. NotifyIcon を初期化する
        ///   4. MainWindow を生成して表示する
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
                    "ClaudeUsageOverlay - エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };

            // トレイアイコンをセットアップする
            InitializeNotifyIcon();

            // メインウィンドウを生成して表示する
            _mainWindow = new MainWindow();
            _mainWindow.Show();
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
        /// アプリケーション終了時に NotifyIcon を確実に破棄する。
        /// ExitApplication() 経由で終了した場合はすでに破棄済みのため二重破棄にならない。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
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
        ///   - アイコン: WPF リソース (Resources/app.ico) から読み込む
        ///   - ツールチップ: "Claude Usage Overlay"
        ///   - ダブルクリック: 表示 / 非表示トグル
        ///   - 右クリックメニュー: 「表示 / 非表示」「終了」
        /// </summary>
        private void InitializeNotifyIcon()
        {
            // pack:// URI で WPF リソースとして埋め込んだ ICO ファイルを読み込む
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
        /// <returns>構築済みの ContextMenuStrip</returns>
        private ContextMenuStrip BuildTrayContextMenu()
        {
            var menu = new ContextMenuStrip();

            // 「表示 / 非表示」メニュー項目
            var showHideItem = new ToolStripMenuItem("表示 / 非表示");
            showHideItem.Click += (_, _) => ToggleMainWindow();

            // 「終了」メニュー項目
            var exitItem = new ToolStripMenuItem("終了");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(showHideItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }
    }
}
