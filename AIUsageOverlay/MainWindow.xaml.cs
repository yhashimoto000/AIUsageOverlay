using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIUsageOverlay.Services;
using AIUsageOverlay.ViewModels;
using Application    = System.Windows.Application;
using Color          = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace AIUsageOverlay
{
    /// <summary>
    /// MainWindow のコードビハインド。
    /// 常時最前面オーバーレイとして動作し、ドラッグ移動・週間インジケータ色変更を担当する。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly UsageService _usageService;
        private readonly MainViewModel _viewModel;

        internal MainViewModel ViewModel => _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _usageService = new UsageService();
            _viewModel    = new MainViewModel(_usageService);
            DataContext   = _viewModel;

            _viewModel.PropertyChanged += (_, e) =>
            {
                // F-03: 使用率変化に合わせてバー色を閾値色へ更新する（色ロジックは UsageLevelHelper に一本化）
                if (e.PropertyName == nameof(MainViewModel.WeeklyPercent))
                    UpdateWeeklyIndicatorColor(_viewModel.WeeklyPercent);
                else if (e.PropertyName == nameof(MainViewModel.SessionPercent))
                    UpdateSessionColor(_viewModel.SessionPercent);
            };

            // F-03: 閾値マーカーを設定値で初期化する
            ApplyThresholdMarkers();

            // 設定のオーバーレイ不透明度を適用する（設定画面でスライダー UI 化した項目）
            Opacity = _usageService.GetSettings().WindowOpacity;

            // F-10: 表示/非表示の変化を ViewModel へ伝える（適応間隔の判定に使う）
            _viewModel.IsOverlayVisible = IsVisible;
            IsVisibleChanged += (_, _) => _viewModel.IsOverlayVisible = IsVisible;

            RestoreWindowPosition();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
            SaveWindowPosition();
            _viewModel.NotifyUserInteraction();   // F-10: ドラッグは操作扱い
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_usageService) { Owner = this };
            settingsWindow.ShowDialog();
            _viewModel.NotifyUserInteraction();   // F-10: 設定操作は操作扱い
            _viewModel.UpdateRefreshInterval();

            // F-03: 閾値・マーカー表示設定の変更をマーカーと現在色へ即時反映する
            // （使用率が変わらない場合は PropertyChanged が走らないため明示的に色を再適用する）
            ApplyThresholdMarkers();
            UpdateSessionColor(_viewModel.SessionPercent);
            UpdateWeeklyIndicatorColor(_viewModel.WeeklyPercent);

            // オーバーレイ不透明度の変更を即時反映する
            Opacity = _usageService.GetSettings().WindowOpacity;

            // F-01/F-03: トレイ形式・閾値色の変更を即座にトレイアイコンへ反映する
            // （使用率が変わらないと App 側の PropertyChanged 監視が走らないため明示的に更新）
            ((App)Application.Current).RefreshTrayIcon();

            await _viewModel.RefreshUsageAsync();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
            => await _usageService.ShowLoginWindowAsync();

        private async void GitHubLogin_Click(object sender, RoutedEventArgs e)
            => await _usageService.ShowGitHubLoginWindowAsync();

        private async void CodexLogin_Click(object sender, RoutedEventArgs e)
            => await _usageService.ShowCodexLoginWindowAsync();

        private void ResetSession_Click(object sender, RoutedEventArgs e)
            => _viewModel.ResetSession();

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            var tb = (TextBlock)RefreshButton.Content;
            tb.Text       = "⟳";
            tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8C00"));

            // F-11: 手動更新はスヌーズを解除して実行する。F-10: 操作として記録
            _viewModel.ClearSnooze();
            _viewModel.NotifyUserInteraction();
            await _viewModel.RefreshUsageAsync();

            tb.Text       = "↺";
            tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));
            RefreshButton.IsEnabled = true;
        }

        private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Dispose();
            ((App)Application.Current).ExitApplication();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!App.IsExiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        private void RestoreWindowPosition()
        {
            var settings = _usageService.GetSettings();
            if (settings.WindowLeft >= 0)
            {
                Left = Math.Max(0, Math.Min(settings.WindowLeft,
                    SystemParameters.PrimaryScreenWidth - Width));
                Top  = Math.Max(0, Math.Min(settings.WindowTop,
                    SystemParameters.PrimaryScreenHeight - Height));
            }
            else
            {
                Loaded += (_, _) =>
                {
                    Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                    Top  = 10;
                    SaveWindowPosition();
                };
            }
        }

        private void SaveWindowPosition()
        {
            var settings = _usageService.GetSettings();
            settings.WindowLeft = Left;
            settings.WindowTop  = Top;
            _usageService.SaveSettings(settings);
        }

        /// <summary>
        /// 週間使用率に応じて週間ドット・%テキストの色を更新する（F-03）。
        /// 色判定は <see cref="UsageLevelHelper"/> に一本化した。
        /// 従来は注意色が #FFC107 でトレイ（#FF8C00）と食い違っていたが、統一色 #FF8C00 になる。
        /// </summary>
        private void UpdateWeeklyIndicatorColor(double weeklyPercent)
        {
            var brush = LevelBrush(weeklyPercent);
            WeeklyIndicatorDot.Fill       = brush;
            WeeklyPercentBlock.Foreground = brush;
        }

        /// <summary>
        /// セッション使用率に応じてセッションバー・%テキストの色を更新する（F-03）。
        /// トレイアイコン（App.xaml.cs）と同じ閾値色になるよう一本化する。
        /// </summary>
        private void UpdateSessionColor(double sessionPercent)
        {
            var brush = LevelBrush(sessionPercent);
            SessionProgressBar.Foreground = brush;
            SessionPercentBlock.Foreground = brush;
        }

        /// <summary>使用率から閾値色のブラシを生成する共通ヘルパー（F-03）。</summary>
        private SolidColorBrush LevelBrush(double percent)
        {
            var settings = _usageService.GetSettings();
            var hex      = UsageLevelHelper.GetHex(percent, settings);
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        /// <summary>
        /// 閾値マーカー（F-03）の閾値と表示可否を設定値から適用する。
        /// ShowThresholdMarkers が false のときは空配列を渡して何も描画させない
        /// （バーの Visibility バインドを壊さずにマーカーだけ無効化するため）。
        /// </summary>
        private void ApplyThresholdMarkers()
        {
            var s = _usageService.GetSettings();
            var thresholds = s.ShowThresholdMarkers
                ? new double[] { s.CautionThresholdPercent, s.WarningThresholdPercent }
                : Array.Empty<double>();

            SessionMarkers.Thresholds = thresholds;
            GitHubMarkers.Thresholds  = thresholds;
            CodexMarkers.Thresholds   = thresholds;
        }
    }
}
