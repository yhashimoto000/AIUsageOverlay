using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeUsageOverlay.Services;
using ClaudeUsageOverlay.ViewModels;
// UseWindowsForms 追加による WinForms との名前衝突を解消するエイリアス
using Application      = System.Windows.Application;
using Color            = System.Windows.Media.Color;
using ColorConverter   = System.Windows.Media.ColorConverter;

namespace ClaudeUsageOverlay
{
    /// <summary>
    /// MainWindow のコードビハインド。
    /// 常時最前面表示のオーバーレイウィンドウとして動作し、
    /// ドラッグ移動・右クリックメニュー・週間インジケータ色変更を担当する。
    ///
    /// トレイ常駐対応:
    ///   × ボタンを押してもアプリは終了せず、ウィンドウを非表示にしてトレイに引っ込む。
    ///   App.IsExiting が true のとき（「終了」選択時）だけ実際に閉じる。
    /// </summary>
    public partial class MainWindow : Window
    {
        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>使用量追跡サービス（設定読み書き・使用量計算を担当）</summary>
        private readonly UsageService _usageService;

        /// <summary>データバインディング用 ViewModel</summary>
        private readonly MainViewModel _viewModel;

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// MainWindow を初期化する。
        /// UsageService と ViewModel を生成し、前回のウィンドウ位置を復元する。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // サービスと ViewModel を初期化する
            _usageService = new UsageService();
            _viewModel = new MainViewModel(_usageService);
            DataContext = _viewModel;

            // ViewModel のプロパティ変更をフックして週間インジケータ色を更新する
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.WeeklyPercent))
                    UpdateWeeklyIndicatorColor(_viewModel.WeeklyPercent);
            };

            // 保存されたウィンドウ位置を復元する
            RestoreWindowPosition();
        }

        // ────────────────────────────────────────────────────────────────
        // イベントハンドラ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ウィンドウのドラッグ移動を実装するハンドラ。
        /// 左ボタン押下でウィンドウをドラッグし、離した後に位置を保存する。
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // WPF 組み込みのドラッグ移動機能を呼び出す
            DragMove();

            // ドラッグ後の新しい位置を設定ファイルに保存する
            SaveWindowPosition();
        }

        /// <summary>
        /// 右クリックメニューの「設定」クリックハンドラ。
        /// SettingsWindow をモーダルダイアログとして表示し、
        /// 閉じた後にタイマー間隔とデータを更新する。
        /// </summary>
        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_usageService)
            {
                Owner = this
            };

            // モーダル表示: 設定が保存されるまでメインウィンドウを操作できない
            settingsWindow.ShowDialog();

            // 設定変更を ViewModel に反映し、即座にデータを再取得する
            _viewModel.UpdateRefreshInterval();
            await _viewModel.RefreshUsageAsync();
        }

        /// <summary>
        /// 右クリックメニューの「ログイン」クリックハンドラ。
        /// WebView2 ウィンドウを画面中央に表示して claude.ai のログインページを開く。
        /// ログイン完了後は ↺ ボタンで更新する。
        /// </summary>
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            await _usageService.ShowLoginWindowAsync();
        }

        /// <summary>
        /// 右クリックメニューの「セッションリセット」クリックハンドラ。
        /// セッション開始時刻をリセットし、表示を即時更新する。
        /// </summary>
        private void ResetSession_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ResetSession();
        }

        /// <summary>
        /// ↺ 更新ボタンのクリックハンドラ。
        /// claude.ai から最新の使用量データを即時取得して表示を更新する。
        /// ボタンを一時的に無効化してスパムクリックを防ぐ。
        /// </summary>
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            // 更新中はボタンを無効化してアイコンを変更する
            RefreshButton.IsEnabled = false;
            var textBlock = (TextBlock)RefreshButton.Content;
            textBlock.Text = "⟳";
            textBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#FF8C00"));

            await _viewModel.RefreshUsageAsync();

            // 更新完了後にボタンを元に戻す
            textBlock.Text = "↺";
            textBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#888888"));
            RefreshButton.IsEnabled = true;
        }

        /// <summary>
        /// 右クリックメニューの「非表示にする」クリックハンドラ。
        /// × ボタンと同じく、ウィンドウを非表示にしてトレイに引っ込める。
        /// </summary>
        private void Hide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        /// <summary>
        /// 右クリックメニューの「終了」クリックハンドラ。
        /// App.ExitApplication() 経由で NotifyIcon を破棄してアプリを終了する。
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Dispose();
            // App.ExitApplication() が IsExiting フラグを立ててから Shutdown() を呼ぶ
            ((App)Application.Current).ExitApplication();
        }

        // ────────────────────────────────────────────────────────────────
        // ウィンドウライフサイクル
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ウィンドウが閉じようとするときに呼ばれるオーバーライド。
        ///
        /// App.IsExiting が false（通常時）: キャンセルしてウィンドウを非表示にする。
        ///   → × ボタンや Alt+F4 でもアプリは終了せず、トレイに引っ込む。
        /// App.IsExiting が true（終了操作時）: そのまま閉じる。
        ///   → 「終了」メニューや ExitApplication() 経由でのみ本当に閉じる。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!App.IsExiting)
            {
                // キャンセルして非表示にする（アプリは終了しない）
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        // ────────────────────────────────────────────────────────────────
        // 内部ヘルパーメソッド
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 設定ファイルから前回のウィンドウ位置を復元する。
        /// 設定が未保存（WindowLeft = -1）の場合は画面水平中央に配置する。
        /// </summary>
        private void RestoreWindowPosition()
        {
            var settings = _usageService.GetSettings();

            if (settings.WindowLeft >= 0)
            {
                // 保存された位置に配置する（画面外に出ないようにクランプ）
                Left = Math.Max(0, Math.Min(settings.WindowLeft,
                    SystemParameters.PrimaryScreenWidth - Width));
                Top = Math.Max(0, Math.Min(settings.WindowTop,
                    SystemParameters.PrimaryScreenHeight - Height));
            }
            else
            {
                // 初回起動: 画面上部中央に配置する（Loaded イベント後に実行）
                Loaded += (_, _) =>
                {
                    Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                    Top = 10;
                    SaveWindowPosition();
                };
            }
        }

        /// <summary>
        /// 現在のウィンドウ位置を設定ファイルに保存する。
        /// ドラッグ移動後と初回配置後に呼び出される。
        /// </summary>
        private void SaveWindowPosition()
        {
            var settings = _usageService.GetSettings();
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            _usageService.SaveSettings(settings);
        }

        /// <summary>
        /// 週間使用率に応じてインジケータドットの色を変更する。
        /// 使用率が低い: グリーン (#4CAF50)
        /// 使用率が中程度: 黄色 (#FFC107)
        /// 使用率が高い: 赤 (#F44336)
        /// </summary>
        /// <param name="weeklyPercent">週間使用率（0 ～ 100）</param>
        private void UpdateWeeklyIndicatorColor(double weeklyPercent)
        {
            string colorHex;
            string textColorHex;

            if (weeklyPercent >= 80)
            {
                // 80% 以上: 警告色（赤）
                colorHex = "#F44336";
                textColorHex = "#F44336";
            }
            else if (weeklyPercent >= 50)
            {
                // 50〜79%: 注意色（黄色）
                colorHex = "#FFC107";
                textColorHex = "#FFC107";
            }
            else
            {
                // 50% 未満: 通常色（グリーン）
                colorHex = "#4CAF50";
                textColorHex = "#4CAF50";
            }

            // ドット色を更新する
            WeeklyIndicatorDot.Fill = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(colorHex));

            // パーセンテージテキスト色を更新する
            WeeklyPercentBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(textColorHex));
        }
    }
}
