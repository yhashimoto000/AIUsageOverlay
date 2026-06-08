using System.Windows;
using Microsoft.Win32;
using AIUsageOverlay.Services;
using MessageBox = System.Windows.MessageBox;

namespace AIUsageOverlay
{
    /// <summary>
    /// SettingsWindow のコードビハインド。
    /// 更新間隔・各ツール有効化・Windows スタートアップ登録を設定する。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private const string StartupRegistryKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AIUsageOverlay";

        private readonly UsageService _usageService;

        public SettingsWindow(UsageService usageService)
        {
            InitializeComponent();
            _usageService = usageService;

            var settings = _usageService.GetSettings();
            RefreshIntervalTextBox.Text = settings.RefreshIntervalSeconds.ToString();
            GitHubCopilotCheckBox.IsChecked = settings.GitHubCopilotEnabled;
            CodexCheckBox.IsChecked         = settings.CodexEnabled;
            StartupCheckBox.IsChecked       = IsStartupRegistered();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(RefreshIntervalTextBox.Text, out var refreshInterval)
                || refreshInterval < 5)
            {
                MessageBox.Show("更新間隔は 5 以上の整数（秒）を入力してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshIntervalTextBox.Focus();
                return;
            }

            var settings = _usageService.GetSettings();
            settings.RefreshIntervalSeconds = refreshInterval;
            settings.GitHubCopilotEnabled   = GitHubCopilotCheckBox.IsChecked == true;
            settings.CodexEnabled           = CodexCheckBox.IsChecked == true;
            _usageService.SaveSettings(settings);

            SetStartupRegistration(StartupCheckBox.IsChecked == true);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private static bool IsStartupRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKeyPath, false);
                return key?.GetValue(StartupValueName) != null;
            }
            catch { return false; }
        }

        private static void SetStartupRegistration(bool register)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    StartupRegistryKeyPath, writable: true);
                if (key == null) return;

                if (register)
                {
                    // Environment.ProcessPath は .NET 6+ で常に有効。
                    // Assembly.Location はシングルファイルアプリで空になるため使用しない（IL3000）。
                    var exePath = Environment.ProcessPath
                               ?? System.IO.Path.Combine(AppContext.BaseDirectory,
                                      AppDomain.CurrentDomain.FriendlyName + ".exe");
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
                }
                else
                {
                    if (key.GetValue(StartupValueName) != null)
                        key.DeleteValue(StartupValueName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"スタートアップ設定の変更に失敗しました。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
