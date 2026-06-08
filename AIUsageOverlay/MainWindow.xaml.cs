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
                if (e.PropertyName == nameof(MainViewModel.WeeklyPercent))
                    UpdateWeeklyIndicatorColor(_viewModel.WeeklyPercent);
            };

            RestoreWindowPosition();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
            SaveWindowPosition();
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_usageService) { Owner = this };
            settingsWindow.ShowDialog();
            _viewModel.UpdateRefreshInterval();
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

        private void UpdateWeeklyIndicatorColor(double weeklyPercent)
        {
            string colorHex;
            if (weeklyPercent >= 80)
                colorHex = "#F44336";
            else if (weeklyPercent >= 50)
                colorHex = "#FFC107";
            else
                colorHex = "#4CAF50";

            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var brush = new SolidColorBrush(color);
            WeeklyIndicatorDot.Fill        = brush;
            WeeklyPercentBlock.Foreground  = brush;
        }
    }
}
