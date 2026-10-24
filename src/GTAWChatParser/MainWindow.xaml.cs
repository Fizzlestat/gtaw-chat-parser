using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GTAWChatParser;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly CaptureService _service;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _noticeTimer;
    private readonly ObservableCollection<TranscriptLine> _lines = [];
    private readonly TrayIcon _tray;

    private Task? _captureTask;
    private string _status = "Starting";
    private string _rendered = "\u0001";
    private string? _lastLogPath;
    private bool _started;
    private bool _closing;
    private bool _exitConfirmed;
    private bool _confirming;
    private bool _binding = true;
    private bool _themeBinding = true;
    private int _lastSeconds = 5;
    private string _animation = "slide";

    public MainWindow()
    {
        try
        {
            _config = AppConfig.Load();
        }
        catch (Exception exception)
        {
            AppLog.Error("Settings file could not be read.", exception);
            _config = new AppConfig();
        }

        _service = new CaptureService(_config);
        try
        {
            WindowsStartup.Apply(_config.StartWithWindows, _config.StartMinimizedToTray);
        }
        catch (Exception exception)
        {
            AppLog.Error("Windows startup could not be updated.", exception);
        }

        HudPalette.Apply(_config);
        InitializeComponent();
        Icon = AppIcon.Image;
        ChatList.ItemsSource = _lines;

        _tray = new TrayIcon(this);
        _tray.OpenRequested += RestoreFromTray;
        _tray.ExitRequested += () => Dispatcher.Invoke(Close);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshTimer.Tick += (_, _) => RenderChat(_service.GetSnapshot());
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4200) };
        _noticeTimer.Tick += (_, _) => HideNotice();

        _service.StatusChanged += status => Post(() => SetStatus(status));
        _service.LinesAppended += lines => Post(() => OnLines(lines));
        _service.LogSaved += (path, gameClosed) => Post(() => OnLogSaved(path, gameClosed));

        BindFields();
        RefreshHeaders();
        SetStatus("Starting");
        AppLog.Info("Started.");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        try
        {
            var style = GetClassLongPtr(hwnd, -26);
            SetClassLongPtr(hwnd, -26, new IntPtr(style.ToInt64() | 0x00020000));
        }
        catch (EntryPointNotFoundException)
        {
        }

        _tray.Attach(hwnd);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_started)
            return;

        _started = true;
        _refreshTimer.Start();
        _captureTask = Task.Run(() => _service.RunAsync(_shutdown.Token));
        RenderChat(_service.GetSnapshot());
        if (App.LaunchInTray)
            HideToTray();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closing)
        {
            base.OnClosing(e);
            return;
        }

        if (!_exitConfirmed && !App.SystemShutdown)
        {
            e.Cancel = true;
            if (_confirming)
                return;

            _confirming = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var accepted = IsVisible
                        ? ConfirmWindow.Ask(this)
                        : ConfirmWindow.Ask(null);
                    if (!accepted)
                        return;

                    _exitConfirmed = true;
                    Close();
                }
                finally
                {
                    _confirming = false;
                }
            }));
            return;
        }

        _closing = true;
        _refreshTimer.Stop();
        _noticeTimer.Stop();
        KeywordToastWindow.CloseAll();
        _tray.Dispose();
        try
        {
            _service.SaveNow();
        }
        catch (Exception exception)
        {
            AppLog.Error("Save on exit failed.", exception);
        }

        _shutdown.Cancel();
        try
        {
            _captureTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
        }

        _service.Dispose();
        _shutdown.Dispose();
        base.OnClosing(e);
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Tray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void KeywordsHeader_Click(object sender, MouseButtonEventArgs e) => SetSection(0);

    private void LogsHeader_Click(object sender, MouseButtonEventArgs e) => SetSection(1);

    private void NotificationsHeader_Click(object sender, MouseButtonEventArgs e) => SetSection(2);

    private void AppearanceHeader_Click(object sender, MouseButtonEventArgs e) => SetSection(3);

    private void StartupHeader_Click(object sender, MouseButtonEventArgs e) => SetSection(4);

    private void SaveKeywords_Click(object sender, RoutedEventArgs e)
    {
        _config.Keywords = AppConfig.NormalizeKeywords(
            KeywordsBox.Text.Split(["\r\n", "\n"], StringSplitOptions.None),
            _config.NotificationCaseSensitive);
        _config.Save();
        BindKeywords();
        ShowNotice(_config.Keywords.Count == 0 ? "Keywords cleared." : $"Saved {_config.Keywords.Count} keyword{(_config.Keywords.Count == 1 ? "" : "s")}.");
        RenderChat(_service.GetSnapshot(), force: true);
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _service.SaveNow();
            if (path == null)
            {
                ShowNotice("There is no captured chat to save yet.");
                return;
            }

            _lastLogPath = path;
            ShowNotice("Saved " + path);
        }
        catch (Exception exception)
        {
            AppLog.Error("Manual save failed.", exception);
            ShowNotice("The log could not be saved. " + exception.Message);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_config.BackupDirectory);
            var target = _lastLogPath != null && File.Exists(_lastLogPath)
                ? _lastLogPath
                : _config.BackupDirectory;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(target) ? $"/select,\"{target}\"" : $"\"{_config.BackupDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowNotice("Could not open the log folder. " + exception.Message);
        }
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder that already contains your GTAW logs. New files are saved under Year\\MON inside it.",
            InitialDirectory = Directory.Exists(_config.BackupDirectory)
                ? _config.BackupDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        _config.BackupDirectory = dialog.FolderName;
        _config.Save();
        FolderText.Text = _config.BackupDirectory;
        ShowNotice("Log folder updated.");
    }

    private void Notifications_Changed(object sender, RoutedEventArgs e) => SaveNotificationSettings();

    private void Animation_Click(object sender, RoutedEventArgs e)
    {
        _animation = sender == PopButton ? "pop" : sender == NoneButton ? "none" : "slide";
        MarkAnimation();
        SaveNotificationSettings();
    }

    private void Seconds_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitDuration();
        e.Handled = true;
    }

    private void Seconds_Commit(object sender, RoutedEventArgs e) => CommitDuration();

    private void BindFields()
    {
        BindKeywords();
        _binding = true;
        NotificationsToggle.IsChecked = _config.NotificationsEnabled;
        SoundToggle.IsChecked = _config.NotificationSoundEnabled;
        StartupToggle.IsChecked = _config.StartWithWindows;
        TrayStartupToggle.IsChecked = _config.StartMinimizedToTray;
        UpdateTrayStartupEnabled();
        CaseToggle.IsChecked = _config.NotificationCaseSensitive;
        _lastSeconds = _config.ResolvedNotificationSeconds;
        SecondsBox.Text = _lastSeconds.ToString();
        _animation = _config.NotificationAnimation;
        MarkAnimation();
        _binding = false;
        BindAppearance();
    }

    private void BindAppearance()
    {
        _themeBinding = true;
        if (UiFontBox.Items.Count == 0)
        {
            var fonts = Fonts.SystemFontFamilies.Select(family => family.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
            UiFontBox.ItemsSource = fonts;
            TranscriptFontBox.ItemsSource = fonts;
        }

        if (HudPalette.TryParse(_config.PrimaryColor, out var primary))
            PrimaryWell.SetColor(primary);
        if (HudPalette.TryParse(_config.SecondaryColor, out var secondary))
            SecondaryWell.SetColor(secondary);
        UiFontBox.SelectedItem = HudPalette.NormalizeFont(_config.UiFont, HudPalette.DefaultUiFont);
        TranscriptFontBox.SelectedItem = HudPalette.NormalizeFont(_config.TranscriptFont, HudPalette.DefaultTranscriptFont);
        _themeBinding = false;
    }

    private void ColorWell_Edited(object sender, EventArgs e)
    {
        if (_themeBinding)
            return;

        PullColors();
        ApplyAppearance(persist: false);
    }

    private void ColorWell_Committed(object sender, EventArgs e)
    {
        if (_themeBinding)
            return;

        PullColors();
        ApplyAppearance(persist: true);
    }

    private void PullColors()
    {
        _config.PrimaryColor = HudPalette.Format(PrimaryWell.Color);
        _config.SecondaryColor = HudPalette.Format(SecondaryWell.Color);
    }

    private void Font_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_themeBinding || UiFontBox.SelectedItem is not string)
            return;

        if (UiFontBox.SelectedItem is string uiFont)
            _config.UiFont = uiFont;
        if (TranscriptFontBox.SelectedItem is string transcriptFont)
            _config.TranscriptFont = transcriptFont;
        ApplyAppearance(persist: true);
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _config.PrimaryColor = HudPalette.DefaultPrimary;
        _config.SecondaryColor = HudPalette.DefaultSecondary;
        _config.UiFont = HudPalette.DefaultUiFont;
        _config.TranscriptFont = HudPalette.DefaultTranscriptFont;
        BindAppearance();
        ApplyAppearance(persist: true);
    }

    private void ApplyAppearance(bool persist)
    {
        _config.PrimaryColor = HudPalette.NormalizeHex(_config.PrimaryColor, HudPalette.DefaultPrimary);
        _config.SecondaryColor = HudPalette.NormalizeHex(_config.SecondaryColor, HudPalette.DefaultSecondary);
        _config.UiFont = HudPalette.NormalizeFont(_config.UiFont, HudPalette.DefaultUiFont);
        _config.TranscriptFont = HudPalette.NormalizeFont(_config.TranscriptFont, HudPalette.DefaultTranscriptFont);
        if (persist)
            _config.Save();
        HudPalette.Apply(_config);
        RefreshHeaders();
        if (persist)
            RenderChat(_service.GetSnapshot(), force: true);
    }

    private void BindKeywords()
    {
        KeywordsBox.Text = string.Join(Environment.NewLine, _config.Keywords);
        FolderText.Text = _config.BackupDirectory;
    }

    private void CommitDuration()
    {
        if (!int.TryParse(SecondsBox.Text.Trim(), out var seconds) || seconds is < 1 or > 60)
        {
            SecondsBox.Text = _lastSeconds.ToString();
            return;
        }

        SecondsBox.Text = seconds.ToString();
        if (seconds == _lastSeconds)
            return;

        _lastSeconds = seconds;
        SaveNotificationSettings();
    }

    private void SaveNotificationSettings()
    {
        if (_binding)
            return;

        _config.NotificationsEnabled = NotificationsToggle.IsChecked == true;
        _config.NotificationSoundEnabled = SoundToggle.IsChecked == true;
        _config.NotificationCaseSensitive = CaseToggle.IsChecked == true;
        _config.NotificationSeconds = _lastSeconds;
        _config.NotificationAnimation = _animation;
        _config.Keywords = AppConfig.NormalizeKeywords(_config.Keywords, _config.NotificationCaseSensitive);
        _config.Save();
        BindKeywords();
        RenderChat(_service.GetSnapshot(), force: true);
    }

    private void MarkAnimation()
    {
        SlideButton.Style = (Style)FindResource(_animation == "slide" ? "HudChoiceSelectedButton" : "HudChoiceButton");
        PopButton.Style = (Style)FindResource(_animation == "pop" ? "HudChoiceSelectedButton" : "HudChoiceButton");
        NoneButton.Style = (Style)FindResource(_animation == "none" ? "HudChoiceSelectedButton" : "HudChoiceButton");
    }

    private void SetSection(int index)
    {
        var openKeywords = index == 0 && KeywordsBody.Visibility != Visibility.Visible;
        var openLogs = index == 1 && LogsBody.Visibility != Visibility.Visible;
        var openNotifications = index == 2 && NotificationsBody.Visibility != Visibility.Visible;
        var openAppearance = index == 3 && AppearanceBody.Visibility != Visibility.Visible;
        var openStartup = index == 4 && StartupBody.Visibility != Visibility.Visible;
        AnimateSection(KeywordsBody, KeywordsScale, KeywordsAngle, openKeywords);
        AnimateSection(LogsBody, LogsScale, LogsAngle, openLogs);
        AnimateSection(NotificationsBody, NotificationsScale, NotificationsAngle, openNotifications);
        AnimateSection(AppearanceBody, AppearanceScale, AppearanceAngle, openAppearance);
        AnimateSection(StartupBody, StartupScale, StartupAngle, openStartup);
        RefreshHeaders();
    }

    private void RefreshHeaders()
    {
        StyleHeader(KeywordsHeader, KeywordsChevron, KeywordsBody.Visibility == Visibility.Visible);
        StyleHeader(LogsHeader, LogsChevron, LogsBody.Visibility == Visibility.Visible);
        StyleHeader(NotificationsHeader, NotificationsChevron, NotificationsBody.Visibility == Visibility.Visible);
        StyleHeader(AppearanceHeader, AppearanceChevron, AppearanceBody.Visibility == Visibility.Visible);
        StyleHeader(StartupHeader, StartupChevron, StartupBody.Visibility == Visibility.Visible);
    }

    private void Header_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border header)
            AnimateHeader(header, ThemeColor("CardHoverBrush"));
    }

    private void Header_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border header)
            return;

        AnimateHeader(header, HeaderIsOpen(header) ? ThemeColor("CardBrush") : TransparentSide());
    }

    private void StyleHeader(Border header, TextBlock chevron, bool open)
    {
        header.CornerRadius = open ? new CornerRadius(8, 8, 0, 0) : new CornerRadius(8);
        chevron.Foreground = FindBrush(open ? "FrameBrush" : "MutedBrush");
        if (!header.IsMouseOver)
            AnimateHeader(header, open ? ThemeColor("CardBrush") : TransparentSide());
    }

    private bool HeaderIsOpen(Border header)
    {
        if (header == KeywordsHeader)
            return KeywordsBody.Visibility == Visibility.Visible;
        if (header == LogsHeader)
            return LogsBody.Visibility == Visibility.Visible;
        if (header == NotificationsHeader)
            return NotificationsBody.Visibility == Visibility.Visible;
        if (header == AppearanceHeader)
            return AppearanceBody.Visibility == Visibility.Visible;
        return StartupBody.Visibility == Visibility.Visible;
    }

    private static Color ThemeColor(string key)
    {
        return Application.Current.Resources[key] is SolidColorBrush brush ? brush.Color : Colors.Transparent;
    }

    private static Color TransparentSide()
    {
        var side = ThemeColor("SideBrush");
        return Color.FromArgb(0, side.R, side.G, side.B);
    }

    private static void AnimateHeader(Border header, Color color)
    {
        if (header.Background is not SolidColorBrush brush || brush.IsFrozen)
        {
            header.Background = new SolidColorBrush(color);
            return;
        }

        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(color, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private static void AnimateSection(FrameworkElement body, ScaleTransform scale, RotateTransform angle, bool open)
    {
        body.Tag = open;
        if (open)
            body.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        angle.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 90 : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        body.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        if (open)
            return;

        var hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(190) };
        hide.Tick += (_, _) =>
        {
            hide.Stop();
            if (body.Tag is false)
                body.Visibility = Visibility.Collapsed;
        };
        hide.Start();
    }

    private void OnLines(IReadOnlyList<string> lines)
    {
        RenderChat(_service.GetSnapshot());
        if (!_config.NotificationsEnabled || GameProcess.IsFiveMFocused())
            return;

        var hits = new List<(string Keywords, string Line)>();
        foreach (var line in lines)
        {
            var matched = ChatLog.FindKeywords(line, _config.Keywords, _config.NotificationCaseSensitive);
            if (matched.Count > 0)
                hits.Add((string.Join(", ", matched), line));
        }

        if (hits.Count == 0)
            return;

        AppLog.Info($"Keyword alert for {hits.Count} line(s).");
        KeywordToastWindow.ShowMatches(hits, _config.ResolvedNotificationSeconds, _config.ResolvedNotificationAnimation, _config.NotificationSoundEnabled);
    }

    private void OnLogSaved(string path, bool gameClosed)
    {
        _lastLogPath = path;
        if (!gameClosed)
            return;

        ShowNotice("Chat log saved. " + path);
        if (WindowState == WindowState.Minimized || !IsVisible)
            FlashWindow(new WindowInteropHelper(this).Handle, true);
    }

    private void SetStatus(string status)
    {
        if (string.Equals(status, _status, StringComparison.Ordinal))
            return;

        _status = status;
        StatusText.Text = status;
        StatusDot.Fill = StatusBrush(status);
    }

    private static Brush StatusBrush(string status)
    {
        if (status.Contains("Capturing", StringComparison.OrdinalIgnoreCase))
            return FindBrush("OkBrush");
        if (status.Contains("Looking", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Waiting for GTAW", StringComparison.OrdinalIgnoreCase)
            || status.Contains("saving", StringComparison.OrdinalIgnoreCase))
            return FindBrush("FrameBrush");
        return FindBrush("IdleBrush");
    }

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeBar.BeginAnimation(HeightProperty, null);
        NoticeBar.Height = 0;
        NoticeBar.Visibility = Visibility.Visible;
        NoticeBar.BeginAnimation(HeightProperty, new DoubleAnimation(32, TimeSpan.FromMilliseconds(160)));
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void HideNotice()
    {
        _noticeTimer.Stop();
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        animation.Completed += (_, _) =>
        {
            NoticeBar.Visibility = Visibility.Collapsed;
            NoticeText.Text = string.Empty;
        };
        NoticeBar.BeginAnimation(HeightProperty, animation);
    }

    private void RenderChat(string snapshot, bool force = false)
    {
        if (!force && snapshot == _rendered)
            return;

        var follow = _rendered.Length == 0 || IsNearBottom();
        _rendered = snapshot;
        _lines.Clear();
        foreach (var line in snapshot.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
                continue;

            _lines.Add(new TranscriptLine(line, LineBrush(line)));
        }

        var hasText = _lines.Count > 0;
        EmptyState.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ChatList.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        if (hasText && follow)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_lines.Count > 0)
                    ChatList.ScrollIntoView(_lines[^1]);
            }, DispatcherPriority.Background);
        }
    }

    private Brush LineBrush(string line)
    {
        if (line.StartsWith("[DATE:", StringComparison.Ordinal))
            return FindBrush("FrameBrush");

        return ChatLog.FindKeywords(line, _config.Keywords, _config.NotificationCaseSensitive).Count > 0
            ? FindBrush("HitBrush")
            : FindBrush("LineBrush");
    }

    private bool IsNearBottom()
    {
        if (ChatList.Items.Count == 0)
            return true;

        var scroll = FindDescendant<ScrollViewer>(ChatList);
        return scroll == null || scroll.ScrollableHeight - scroll.VerticalOffset < 32;
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding)
            return;

        var enabled = StartupToggle.IsChecked == true;
        UpdateTrayStartupEnabled();
        try
        {
            WindowsStartup.Apply(enabled, TrayStartupToggle.IsChecked == true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Windows startup could not be updated.", exception);
            _binding = true;
            StartupToggle.IsChecked = _config.StartWithWindows;
            UpdateTrayStartupEnabled();
            _binding = false;
            ShowNotice("Windows startup could not be changed.");
            return;
        }

        _config.StartWithWindows = enabled;
        _config.Save();
    }

    private void TrayStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (_binding)
            return;

        if (StartupToggle.IsChecked != true)
        {
            _binding = true;
            TrayStartupToggle.IsChecked = _config.StartMinimizedToTray;
            _binding = false;
            return;
        }

        var minimized = TrayStartupToggle.IsChecked == true;
        try
        {
            WindowsStartup.Apply(true, minimized);
        }
        catch (Exception exception)
        {
            AppLog.Error("Windows startup could not be updated.", exception);
            _binding = true;
            TrayStartupToggle.IsChecked = _config.StartMinimizedToTray;
            _binding = false;
            ShowNotice("Windows startup could not be changed.");
            return;
        }

        _config.StartMinimizedToTray = minimized;
        _config.Save();
    }

    private void UpdateTrayStartupEnabled()
    {
        var enabled = StartupToggle.IsChecked == true;
        TrayStartupToggle.IsEnabled = enabled;
        TrayStartupLabel.Opacity = enabled ? 1 : 0.45;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _tray.Visible = true;
    }

    private void RestoreFromTray()
    {
        Opacity = 1;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _tray.Visible = false;
    }

    private void Post(Action action)
    {
        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        action();
    }

    private static Brush FindBrush(string key) => (Brush)Application.Current.FindResource(key);

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match)
                return match;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }

        return null;
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);
}

internal sealed class TranscriptLine
{
    public TranscriptLine(string text, Brush foreground)
    {
        Text = text;
        Foreground = foreground;
    }

    public string Text { get; }

    public Brush Foreground { get; }
}
