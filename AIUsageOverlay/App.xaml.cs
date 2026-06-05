using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
// WinForms / System.Drawing との名前衝突を解消するエイリアス
using Application  = System.Windows.Application;
using MessageBox   = System.Windows.MessageBox;
using FontStyle    = System.Drawing.FontStyle;

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

            var exitItem = new ToolStripMenuItem("終了");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(showHideItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        // ────────────────────────────────────────────────────────────────
        // 動的アイコン更新
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ViewModel のプロパティ変更を検知するハンドラ。
        /// SessionPercent または WeeklyPercent が変化したときにトレイアイコンを更新する。
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // SessionPercent か WeeklyPercent の変化のみ処理する（他プロパティは無視）
            if (e.PropertyName is not (nameof(ViewModels.MainViewModel.SessionPercent)
                                    or nameof(ViewModels.MainViewModel.WeeklyPercent)))
                return;

            if (_mainWindow == null || _notifyIcon == null) return;

            var sessionPercent = (int)_mainWindow.ViewModel.SessionPercent;
            var weeklyPercent  = (int)_mainWindow.ViewModel.WeeklyPercent;

            // NotifyIcon の操作は UI スレッドから行う
            Dispatcher.Invoke(() => UpdateTrayIcon(sessionPercent, weeklyPercent));
        }

        /// <summary>
        /// セッション使用率に応じてトレイアイコンとツールチップを更新する。
        ///
        /// アイコン色の基準（セッション使用率）:
        ///   0 〜 49%  : 緑  (#4CAF50) ← 通常
        ///   50 〜 79% : オレンジ (#FF8C00) ← 注意
        ///   80 〜100% : 赤  (#F44336) ← 警告
        ///
        /// ツールチップ（NotifyIcon.Text）: "セッション: 75%  週間: 10%"
        /// ※ Windows の制限により最大 63 文字
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <param name="weeklyPercent">週間使用率（0〜100）</param>
        private void UpdateTrayIcon(int sessionPercent, int weeklyPercent)
        {
            if (_notifyIcon == null) return;

            // ── ツールチップを更新する ──
            var tooltip = $"セッション: {sessionPercent}%  週間: {weeklyPercent}%";
            // NotifyIcon.Text は最大 63 文字の制限があるためクランプする
            _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];

            // ── アイコンを動的生成して更新する ──
            using var bitmap = CreateSessionBitmap(sessionPercent);
            var newHandle = bitmap.GetHicon();

            // 新しいアイコンをセットする
            _notifyIcon.Icon = Icon.FromHandle(newHandle);

            // 前回のアイコンハンドルを解放する（GDI リソースリーク防止）
            if (_currentIconHandle != IntPtr.Zero)
                DestroyIcon(_currentIconHandle);

            _currentIconHandle = newHandle;
        }

        /// <summary>
        /// セッション使用率を視覚化した 32×32 ビットマップを生成する。
        ///
        /// デザイン:
        ///   - 外枠: ダーク円（#1C1C1C）
        ///   - 進捗弧: 使用率に応じた色で -90° から時計回りに描画
        ///   - 内枠: ダーク円で中抜き（ドーナツ形状）
        ///   - 中央テキスト: 使用率（%）を白で表示
        /// </summary>
        /// <param name="sessionPercent">セッション使用率（0〜100）</param>
        /// <returns>32×32 の ARGB ビットマップ（呼び出し元が Dispose する）</returns>
        private static Bitmap CreateSessionBitmap(int sessionPercent)
        {
            const int size = 32;
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // 使用率に応じた色を決定する
            Color progressColor = sessionPercent >= 80
                ? Color.FromArgb(255, 244,  67,  54)   // 赤: 80% 以上（警告）
                : sessionPercent >= 50
                ? Color.FromArgb(255, 255, 140,   0)   // オレンジ: 50〜79%（注意）
                : Color.FromArgb(255,  76, 175,  80);  // 緑: 0〜49%（通常）

            // 外側のダーク円（背景）
            using (var bgBrush = new SolidBrush(Color.FromArgb(255, 28, 28, 28)))
                g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);

            // 進捗弧（使用率分だけ色付き弧を描画）
            // -90° から開始して時計回りに sweepAngle 度描く（0% = 弧なし、100% = 全周）
            if (sessionPercent > 0)
            {
                float sweepAngle = Math.Min(sessionPercent, 100) * 3.6f;
                using var progressBrush = new SolidBrush(progressColor);
                g.FillPie(progressBrush, 2, 2, size - 4, size - 4, -90f, sweepAngle);
            }

            // 内側のダーク円（ドーナツの穴）
            int holeSize   = size - 14;
            int holeOffset = (size - holeSize) / 2;
            using (var holeBrush = new SolidBrush(Color.FromArgb(255, 28, 28, 28)))
                g.FillEllipse(holeBrush, holeOffset, holeOffset, holeSize, holeSize);

            // 中央にパーセンテージテキストを描画する
            string text      = $"{sessionPercent}%";
            float  fontSize  = sessionPercent >= 100 ? 6.5f : 7.5f;  // 3桁のとき少し小さく
            using var font   = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Point);
            using var brush  = new SolidBrush(Color.White);
            var textSize     = g.MeasureString(text, font);
            float tx         = (size - textSize.Width)  / 2f;
            float ty         = (size - textSize.Height) / 2f;
            g.DrawString(text, font, brush, tx, ty);

            return bmp;
        }
    }
}
