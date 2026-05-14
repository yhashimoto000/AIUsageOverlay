using System.Windows;
using Microsoft.Win32;
using ClaudeUsageOverlay.Services;
// UseWindowsForms 追加による WinForms との名前衝突を解消するエイリアス
using MessageBox = System.Windows.MessageBox;

namespace ClaudeUsageOverlay
{
    /// <summary>
    /// SettingsWindow のコードビハインド。
    /// 更新間隔と Windows スタートアップ登録を設定する。
    /// Claude.ai への認証は右クリックメニュー「ログイン」から行う。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // ────────────────────────────────────────────────────────────────
        // 定数
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// スタートアップ登録に使うレジストリキーのパス。
        /// HKCU 配下なので管理者権限不要で読み書きできる。
        /// </summary>
        private const string StartupRegistryKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>レジストリに登録するアプリ識別名</summary>
        private const string StartupValueName = "ClaudeUsageOverlay";

        // ────────────────────────────────────────────────────────────────
        // フィールド
        // ────────────────────────────────────────────────────────────────

        /// <summary>設定の読み書きを担当するサービス</summary>
        private readonly UsageService _usageService;

        // ────────────────────────────────────────────────────────────────
        // コンストラクタ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// SettingsWindow を初期化し、現在の設定値を各フィールドに反映する。
        /// </summary>
        /// <param name="usageService">UsageService インスタンス（MainWindow と共有）</param>
        public SettingsWindow(UsageService usageService)
        {
            InitializeComponent();
            _usageService = usageService;

            // 現在の設定値をフィールドに反映する
            var settings = _usageService.GetSettings();
            RefreshIntervalTextBox.Text = settings.RefreshIntervalSeconds.ToString();

            // レジストリの登録状態をチェックボックスに反映する
            StartupCheckBox.IsChecked = IsStartupRegistered();
        }

        // ────────────────────────────────────────────────────────────────
        // イベントハンドラ
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 「保存」ボタンのクリックハンドラ。
        /// 更新間隔をバリデートして保存し、スタートアップ登録を更新する。
        /// </summary>
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 更新間隔のバリデーション（5秒以上）
            if (!int.TryParse(RefreshIntervalTextBox.Text, out var refreshInterval)
                || refreshInterval < 5)
            {
                MessageBox.Show("更新間隔は 5 以上の整数（秒）を入力してください。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshIntervalTextBox.Focus();
                return;
            }

            // 更新間隔を保存する
            var settings = _usageService.GetSettings();
            settings.RefreshIntervalSeconds = refreshInterval;
            _usageService.SaveSettings(settings);

            // スタートアップ登録を更新する
            bool wantsStartup = StartupCheckBox.IsChecked == true;
            SetStartupRegistration(wantsStartup);

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 「キャンセル」ボタンのクリックハンドラ。
        /// 変更を保存せずに閉じる。
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ────────────────────────────────────────────────────────────────
        // スタートアップ登録ヘルパー
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// アプリが Windows スタートアップに登録済みかどうかを確認する。
        /// レジストリ HKCU\...\Run に本アプリのエントリが存在するか調べる。
        /// </summary>
        /// <returns>登録済みの場合は true</returns>
        private static bool IsStartupRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKeyPath, false);
                return key?.GetValue(StartupValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Windows スタートアップへの登録・解除を行う。
        /// 登録: HKCU\...\Run に実行ファイルのパスを書き込む
        /// 解除: 同キーからエントリを削除する
        /// 管理者権限は不要（HKCU のため）。
        /// </summary>
        /// <param name="register">true で登録、false で解除</param>
        private static void SetStartupRegistration(bool register)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    StartupRegistryKeyPath, writable: true);
                if (key == null) return;

                if (register)
                {
                    // 現在の実行ファイルのパスを登録する
                    // Environment.ProcessPath は .NET 6 以降で利用可能
                    var exePath = Environment.ProcessPath
                               ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
                }
                else
                {
                    // エントリが存在する場合のみ削除する
                    if (key.GetValue(StartupValueName) != null)
                        key.DeleteValue(StartupValueName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"スタートアップ設定の変更に失敗しました。\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
