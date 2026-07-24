using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AIUsageOverlay.Services;
using AIUsageOverlay.ViewModels;
using Application    = System.Windows.Application;
using Brush          = System.Windows.Media.Brush;
using Button         = System.Windows.Controls.Button;
using Color          = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point          = System.Windows.Point;

namespace AIUsageOverlay
{
    /// <summary>
    /// MainWindow のコードビハインド。
    /// 常時最前面オーバーレイとして動作し、表示形式 1a（縦積み）⇔ 1b（コンパクト⇔詳細）の切替、
    /// ドラッグ移動、閾値レベル色の反映、スパークライン描画（1a）を担当する。
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
                switch (e.PropertyName)
                {
                    // デザイン刷新: Claude セッションのレベル色を 1a/1b の各要素へ反映
                    case nameof(MainViewModel.SessionPercent):
                        UpdateSessionColor(_viewModel.SessionPercent);
                        break;
                    // 1b 詳細の Claude 週間バー・数値のレベル色を反映
                    case nameof(MainViewModel.WeeklyPercent):
                        UpdateWeeklyColor(_viewModel.WeeklyPercent);
                        break;
                    // Claude メタ行（残り時間＋ペース）の Inlines を再構築（1a 用）
                    case nameof(MainViewModel.ClaudeMetaPrimary):
                    case nameof(MainViewModel.SessionPaceText):
                    case nameof(MainViewModel.SessionPaceVisibility):
                    case nameof(MainViewModel.SessionPaceBrush):
                        UpdateClaudeMeta();
                        break;
                    // Codex メタ行の Inlines を再構築（1a 用）
                    case nameof(MainViewModel.CodexMetaPrimary):
                    case nameof(MainViewModel.CodexPaceText):
                    case nameof(MainViewModel.CodexPaceVisibility):
                    case nameof(MainViewModel.CodexPaceBrush):
                        UpdateCodexMeta();
                        break;
                    // 取得サイクル完了 → スパークライン再描画（%同値でも履歴は伸びる）
                    case nameof(MainViewModel.SparklineRevision):
                        RefreshAllSparklines();
                        break;
                }
            };

            // F-03: 閾値マーカーを設定値で初期化する
            ApplyThresholdMarkers();

            // 設定のオーバーレイ不透明度を適用する（設定画面でスライダー UI 化した項目）
            Opacity = _usageService.GetSettings().WindowOpacity;

            // F-10: 表示/非表示の変化を ViewModel へ伝える（適応間隔の判定に使う）
            _viewModel.IsOverlayVisible = IsVisible;
            IsVisibleChanged += (_, _) => _viewModel.IsOverlayVisible = IsVisible;

            // デザイン刷新: 初期表示のメタ行・Claude 色を一度組み立てる。
            // スパークラインは ActualWidth 確定後に描くため Loaded で初回描画する。
            UpdateSessionColor(_viewModel.SessionPercent);
            UpdateWeeklyColor(_viewModel.WeeklyPercent);
            UpdateClaudeMeta();
            UpdateCodexMeta();
            Loaded += (_, _) => RefreshAllSparklines();

            // デザイン刷新: オーバーレイ表示形式（1a/1b）と 1b の展開状態を適用する
            ApplyOverlayLayout();

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
            UpdateWeeklyColor(_viewModel.WeeklyPercent);

            // デザイン刷新: 表示形式（1a/1b）の変更を反映する（内部でスパークラインも再描画）
            ApplyOverlayLayout();

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
            // 1a / 1b どちらの更新ボタンからでも動くよう sender からボタンを取得する
            var button = (Button)sender;
            button.IsEnabled = false;
            var tb = (TextBlock)button.Content;
            tb.Text       = "⟳";
            tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8C00"));

            // F-11: 手動更新はスヌーズを解除して実行する。F-10: 操作として記録
            _viewModel.ClearSnooze();
            _viewModel.NotifyUserInteraction();
            await _viewModel.RefreshUsageAsync();

            tb.Text       = "↺";
            tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A8A92"));
            button.IsEnabled = true;
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
        /// セッション使用率に応じて Claude の状態ドット・%テキスト・バーの色を閾値色へ更新する（F-03）。
        /// 1a: ドット・数値のみレベル色（縦積みのバーは固定アクセント色）。
        /// 1b: コンパクト/詳細では Claude バー・数値もレベル色（1b カンプ準拠）。
        /// トレイアイコン（App.xaml.cs）と同じ閾値色になるよう <see cref="UsageLevelHelper"/> に一本化する。
        /// </summary>
        private void UpdateSessionColor(double sessionPercent)
        {
            var brush = LevelBrush(sessionPercent);
            // 1a
            ClaudeDot.Fill                 = brush;
            SessionPercentBlock.Foreground = brush;
            // 1b
            CompactClaudeBar.Foreground       = brush;
            CompactClaudePercent.Foreground   = brush;
            ExpandedSessionBar.Foreground     = brush;
            ExpandedSessionPercent.Foreground = brush;
        }

        /// <summary>
        /// 週間使用率に応じて 1b 詳細の Claude 週間バー・数値をレベル色に更新する。
        /// 1a では週間は見出しチップ（色固定）で表すため対象外。
        /// </summary>
        private void UpdateWeeklyColor(double weeklyPercent)
        {
            var brush = LevelBrush(weeklyPercent);
            ExpandedWeeklyBar.Foreground     = brush;
            ExpandedWeeklyPercent.Foreground = brush;
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

        // ────────────────────────────────────────────────────────────────
        // オーバーレイ表示形式（1a ⇔ 1b）— デザイン刷新
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 設定の表示形式（OverlayLayout）と 1b の展開状態（OverlayExpanded）に応じて
        /// 3 つのレイアウト（1a 縦積み / 1b コンパクト / 1b 詳細）の表示を切り替える。
        /// </summary>
        private void ApplyOverlayLayout()
        {
            var s = _usageService.GetSettings();
            bool compact = s.OverlayLayout == "compact";

            LayoutList.Visibility    = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactPill.Visibility   = compact && !s.OverlayExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExpandedPanel.Visibility = compact &&  s.OverlayExpanded ? Visibility.Visible : Visibility.Collapsed;

            // 1a に戻したときはスパークラインを描き直す（Collapsed だった間に幅が 0 化しているため）
            if (!compact)
                RefreshAllSparklines();
        }

        /// <summary>
        /// 1b のコンパクト⇔詳細を切り替える（▾/▴ ボタン）。状態は設定へ永続化して次回起動時に復元する。
        /// </summary>
        private void ToggleExpand_Click(object sender, RoutedEventArgs e)
        {
            var s = _usageService.GetSettings();
            s.OverlayExpanded = !s.OverlayExpanded;
            _usageService.SaveSettings(s);
            ApplyOverlayLayout();
            _viewModel.NotifyUserInteraction();   // F-10: 操作扱い
        }

        // ────────────────────────────────────────────────────────────────
        // メタ行（残り時間 ＋ ペースを 1 行に）— デザイン刷新（1a）
        // ────────────────────────────────────────────────────────────────

        /// <summary>Claude のメタ行 Inlines を「残り時間（Text2 色）＋ ペース（ペース色）」で組み立てる。</summary>
        private void UpdateClaudeMeta()
            => UpdateMeta(ClaudeMeta, ViewModel.ClaudeMetaPrimary, ViewModel.SessionPaceText,
                ViewModel.SessionPaceBrush, ViewModel.SessionPaceVisibility == Visibility.Visible);

        /// <summary>Codex のメタ行 Inlines を「残り時間（Text2 色）＋ ペース（ペース色）」で組み立てる。</summary>
        private void UpdateCodexMeta()
            => UpdateMeta(CodexMeta, ViewModel.CodexMetaPrimary, ViewModel.CodexPaceText,
                ViewModel.CodexPaceBrush, ViewModel.CodexPaceVisibility == Visibility.Visible);

        /// <summary>
        /// メタ行 TextBlock を 2 つの Run（主テキスト＋ペース）で再構築する。
        /// 主テキストは TextBlock の Foreground（Brush.Text2）を継承し、ペースだけレベル/ペース色を付ける。
        /// ペースが非表示のときは主テキストのみ。
        /// </summary>
        private static void UpdateMeta(TextBlock target, string primary,
            string paceText, Brush paceBrush, bool paceVisible)
        {
            target.Inlines.Clear();
            target.Inlines.Add(new Run(primary));   // Foreground 未指定 → TextBlock の Text2 色を継承
            if (paceVisible && !string.IsNullOrEmpty(paceText))
                target.Inlines.Add(new Run(" ・ " + paceText) { Foreground = paceBrush });
        }

        // ────────────────────────────────────────────────────────────────
        // スパークライン描画（使用率%の自己記録の推移）— デザイン刷新（1a）
        // ────────────────────────────────────────────────────────────────

        /// <summary>3 サービスのスパークラインをまとめて描き直す（取得サイクル完了・設定変更・初回表示時）。</summary>
        private void RefreshAllSparklines()
        {
            UpdateSparkline(ClaudeSparkline,  UsageHistoryService.SeriesClaude,  sectionAllows: true);
            UpdateSparkline(CopilotSparkline, UsageHistoryService.SeriesCopilot,
                sectionAllows: ViewModel.GitHubSectionVisibility == Visibility.Visible);
            UpdateSparkline(CodexSparkline,   UsageHistoryService.SeriesCodex,
                sectionAllows: ViewModel.CodexSectionVisibility == Visibility.Visible);
        }

        /// <summary>Polyline のサイズ確定・変化に追従して当該スパークラインを引き直す。</summary>
        private void Sparkline_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender == ClaudeSparkline)
                UpdateSparkline(ClaudeSparkline, UsageHistoryService.SeriesClaude, sectionAllows: true);
            else if (sender == CopilotSparkline)
                UpdateSparkline(CopilotSparkline, UsageHistoryService.SeriesCopilot,
                    sectionAllows: ViewModel.GitHubSectionVisibility == Visibility.Visible);
            else if (sender == CodexSparkline)
                UpdateSparkline(CodexSparkline, UsageHistoryService.SeriesCodex,
                    sectionAllows: ViewModel.CodexSectionVisibility == Visibility.Visible);
        }

        /// <summary>
        /// 1 本のスパークラインを、自己記録した使用率%履歴から ActualWidth 基準の実座標で描画する。
        /// Stretch=Fill を使わないのは線幅の歪みを避けるため（引継ぎ資料 §4）。
        ///
        /// 表示条件: セクション表示中 かつ スパークライン設定 ON かつ 点が 1 以上（1点は水平線）。
        /// 幅未確定（ActualWidth=0）のときは Visible にしてレイアウトを促し、SizeChanged で再描画する。
        /// 値が高いほど上（y が小さい）になるよう 0%→下端 / 100%→上端 にマップする。
        /// </summary>
        /// <param name="target">描画先 Polyline</param>
        /// <param name="series">履歴系列キー（<see cref="UsageHistoryService.SeriesClaude"/> 等）</param>
        /// <param name="sectionAllows">当該サービスのセクションが表示中か（F-15。一時的な取得失敗でバー→ドット化しても履歴があれば線を維持する）</param>
        private void UpdateSparkline(Polyline target, string series, bool sectionAllows)
        {
            var settings = _usageService.GetSettings();
            IReadOnlyList<double> data = _usageService.GetHistory(series);

            // 表示条件を満たさない（セクション非表示・OFF・点不足）→ 隠す。
            // data.Count<2 は 0 点（履歴なし）のみを弾く前提。1 点は GetSessionSeries が
            // 同値 2 点（水平線）へ複製するため、ここに 1 点が渡ることはない（F-16）。
            if (!sectionAllows || !settings.ShowSparkline || data.Count < 2)
            {
                target.Points = null;
                target.Visibility = Visibility.Collapsed;
                return;
            }

            double w = target.ActualWidth;
            double h = target.ActualHeight > 0 ? target.ActualHeight : target.Height;

            // 表示すべきだが幅・高さ未確定 → Visible にしてレイアウトを促し、SizeChanged 側で再描画させる
            if (w <= 0 || h <= 0)
            {
                target.Visibility = Visibility.Visible;
                return;
            }

            int n = data.Count;
            var points = new PointCollection(n);
            for (int i = 0; i < n; i++)
            {
                double x = w * i / (n - 1);
                // 使用率が高いほど上端に近づける（0%→下端 h、100%→上端 0）
                double y = h - (data[i] / 100.0) * h;
                points.Add(new Point(x, y));
            }

            target.Points = points;
            target.Visibility = Visibility.Visible;
        }
    }
}
