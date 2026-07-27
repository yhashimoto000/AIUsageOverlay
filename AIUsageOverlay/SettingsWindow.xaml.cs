using System.Windows;
using Microsoft.Win32;
using AIUsageOverlay.Services;
using MessageBox = System.Windows.MessageBox;

namespace AIUsageOverlay
{
    /// <summary>
    /// SettingsWindow のコードビハインド。
    /// 左サイドバーで全般・表示項目・外観・通知を切り替え、設定を編集する。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private const string StartupRegistryKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "AIUsageOverlay";

        private readonly UsageService _usageService;
        private readonly Func<Task<string>> _checkForUpdatesAsync;

        /// <summary>
        /// 設定画面を初期化し、現在設定と自バージョンを各コントロールへ反映する。
        /// </summary>
        /// <param name="usageService">設定の取得・保存を担うサービス</param>
        /// <param name="checkForUpdatesAsync">24時間ゲートを無視して更新確認するコールバック</param>
        public SettingsWindow(
            UsageService usageService,
            Func<Task<string>> checkForUpdatesAsync)
        {
            InitializeComponent();
            _usageService = usageService;
            _checkForUpdatesAsync = checkForUpdatesAsync;

            var settings = _usageService.GetSettings();

            // ── 全般 ──
            RefreshIntervalTextBox.Text     = settings.RefreshIntervalSeconds.ToString();
            AdaptiveRefreshCheckBox.IsChecked = settings.AdaptiveRefreshEnabled;
            StartupCheckBox.IsChecked       = IsStartupRegistered();
            AutoUpdateCheckBox.IsChecked    = settings.AutoUpdateCheckEnabled;
            CurrentVersionTextBlock.Text    =
                $"現在のバージョン: v{UpdateCheckService.GetCurrentVersion()}";
            VersionLabel.Text = $"AI Usage Overlay v{UpdateCheckService.GetCurrentVersion()}";

            // ── 表示項目 ──
            GitHubCopilotCheckBox.IsChecked = settings.GitHubCopilotEnabled;
            CodexCheckBox.IsChecked         = settings.CodexEnabled;
            // リセット表示形式（セグメント）: relative / absolute
            if (settings.ResetDisplayMode == "absolute") ResetAbsolute.IsChecked = true;
            else                                          ResetRelative.IsChecked = true;

            // 表示項目（ペース・F-06 / スパークライン・刷新 2b）
            PaceEnabledCheckBox.IsChecked   = settings.PaceEnabled;
            ShowSparklineCheckBox.IsChecked = settings.ShowSparkline;

            // ── 外観 ──
            // オーバーレイ表示形式（セグメント）: list（縦積み・1a）/ compact（コンパクト⇔詳細・1b）
            if (settings.OverlayLayout == "compact") OverlayLayoutCompact.IsChecked = true;
            else                                      OverlayLayoutList.IsChecked   = true;

            // トレイアイコン形式（セグメント）: ring（既定）/ dualBar / donut / numeric
            switch (settings.TrayIconStyle)
            {
                case "donut":   TrayDonut.IsChecked   = true; break;
                case "dualBar": TrayDualBar.IsChecked = true; break;
                case "numeric": TrayNumeric.IsChecked = true; break;
                default:        TrayRing.IsChecked    = true; break; // "ring"
            }
            CautionThresholdTextBox.Text        = settings.CautionThresholdPercent.ToString();
            WarningThresholdTextBox.Text        = settings.WarningThresholdPercent.ToString();
            ShowThresholdMarkersCheckBox.IsChecked = settings.ShowThresholdMarkers;
            OpacitySlider.Value                 = settings.WindowOpacity;
            UpdateOpacityLabel(settings.WindowOpacity);

            // ── 通知（F-07）──
            NotificationsEnabledCheckBox.IsChecked = settings.NotificationsEnabled;
            NotificationThresholdsTextBox.Text     = string.Join(", ", settings.NotificationThresholds);
            NotifyOnResetCheckBox.IsChecked        = settings.NotifyOnReset;
            NotifyOnExhaustedCheckBox.IsChecked    = settings.NotifyOnExhausted;
        }

        /// <summary>不透明度スライダーの値変更でラベルを更新する（保存は「保存」ボタンで確定）。</summary>
        private void OpacitySlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e) => UpdateOpacityLabel(e.NewValue);

        /// <summary>不透明度ラベルを "オーバーレイ不透明度: NN%" 形式で更新する。</summary>
        private void UpdateOpacityLabel(double value)
        {
            // InitializeComponent 中の初回発火に備えて null ガードする
            if (OpacityLabel != null)
                OpacityLabel.Text = $"オーバーレイ不透明度: {(int)(value * 100)}%";
        }

        /// <summary>
        /// 24時間ゲートとスキップ設定を無視して更新確認を即時実行し、結果を画面に表示する。F-23。
        /// </summary>
        private async void CheckUpdatesNow_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateCheckStatusTextBlock.Text = "更新を確認しています...";

            try
            {
                UpdateCheckStatusTextBlock.Text = await _checkForUpdatesAsync();
            }
            catch (Exception ex)
            {
                UpdateCheckStatusTextBlock.Text = $"更新確認に失敗しました: {ex.Message}";
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // ── 更新間隔の検証 ──
            if (!int.TryParse(RefreshIntervalTextBox.Text, out var refreshInterval)
                || refreshInterval < 5)
            {
                MessageBox.Show("更新間隔は 5 以上の整数（秒）を入力してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshIntervalTextBox.Focus();
                return;
            }

            // ── 閾値の検証（0〜100、注意 < 警告）──
            if (!int.TryParse(CautionThresholdTextBox.Text, out var caution)
                || caution < 0 || caution > 100)
            {
                MessageBox.Show("注意の閾値は 0〜100 の整数で入力してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                CautionThresholdTextBox.Focus();
                return;
            }
            if (!int.TryParse(WarningThresholdTextBox.Text, out var warning)
                || warning < 0 || warning > 100)
            {
                MessageBox.Show("警告の閾値は 0〜100 の整数で入力してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                WarningThresholdTextBox.Focus();
                return;
            }
            if (caution >= warning)
            {
                MessageBox.Show("注意の閾値は警告の閾値より小さく指定してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                CautionThresholdTextBox.Focus();
                return;
            }

            // ── 通知閾値のパース（カンマ区切り・0〜100 の整数。空欄は「閾値通知なし」）──
            int[] notificationThresholds;
            var thresholdsRaw = NotificationThresholdsTextBox.Text.Trim();
            if (thresholdsRaw.Length == 0)
            {
                notificationThresholds = Array.Empty<int>();
            }
            else
            {
                var parts = thresholdsRaw.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var parsed = new List<int>();
                foreach (var part in parts)
                {
                    if (!int.TryParse(part, out var t) || t < 0 || t > 100)
                    {
                        MessageBox.Show("通知閾値は 0〜100 の整数をカンマ区切りで入力してください（例: 70, 90）。",
                            "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                        NotificationThresholdsTextBox.Focus();
                        return;
                    }
                    parsed.Add(t);
                }
                notificationThresholds = parsed.ToArray();
            }

            var settings = _usageService.GetSettings();

            // 全般
            settings.RefreshIntervalSeconds = refreshInterval;
            settings.AdaptiveRefreshEnabled = AdaptiveRefreshCheckBox.IsChecked == true;
            settings.AutoUpdateCheckEnabled = AutoUpdateCheckBox.IsChecked == true;

            // 表示項目
            settings.GitHubCopilotEnabled   = GitHubCopilotCheckBox.IsChecked == true;
            settings.CodexEnabled           = CodexCheckBox.IsChecked == true;
            settings.ResetDisplayMode       = ResetAbsolute.IsChecked == true ? "absolute" : "relative";

            // 外観（オーバーレイ表示形式・トレイ形式はセグメント選択から決定）
            settings.OverlayLayout          = OverlayLayoutCompact.IsChecked == true ? "compact" : "list";
            settings.TrayIconStyle          =
                  TrayDonut.IsChecked   == true ? "donut"
                : TrayDualBar.IsChecked == true ? "dualBar"
                : TrayNumeric.IsChecked == true ? "numeric"
                :                                 "ring";
            settings.CautionThresholdPercent = caution;
            settings.WarningThresholdPercent = warning;
            settings.ShowThresholdMarkers   = ShowThresholdMarkersCheckBox.IsChecked == true;
            settings.WindowOpacity          = OpacitySlider.Value;

            // ペース（F-06）・スパークライン（刷新 2b）
            settings.PaceEnabled            = PaceEnabledCheckBox.IsChecked == true;
            settings.ShowSparkline          = ShowSparklineCheckBox.IsChecked == true;

            // 通知（F-07）
            settings.NotificationsEnabled   = NotificationsEnabledCheckBox.IsChecked == true;
            settings.NotificationThresholds = notificationThresholds;
            settings.NotifyOnReset          = NotifyOnResetCheckBox.IsChecked == true;
            settings.NotifyOnExhausted      = NotifyOnExhaustedCheckBox.IsChecked == true;

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
