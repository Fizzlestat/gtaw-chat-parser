using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace GTAWChatParser;

internal sealed class KeywordToastWindow : Window
{
    private const int MaxToastWidth = 440;
    private const int Edge = 16;
    private const int Gap = 10;

    private static readonly List<KeywordToastWindow> OpenToasts = [];

    private bool _ticking;
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _progress;
    private long _phaseStart;
    private int _holdMs = 5000;
    private bool _leaving;
    private NotificationMotion _motion = NotificationMotion.Slide;
    private double _slotHeight;

    private KeywordToastWindow(string title, string line, NotificationMotion motion, int holdMs)
    {
        _motion = motion;
        _holdMs = holdMs;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        FontFamily uiFont = (FontFamily)Application.Current.FindResource("UiFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        StackPanel headingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        headingRow.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = FindBrush("FrameBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 0)
        });
        headingRow.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = uiFont,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("MutedBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        TextBlock body = new TextBlock
        {
            Text = line,
            FontFamily = (FontFamily)Application.Current.FindResource("TranscriptFont"),
            Foreground = FindBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 19,
            MaxHeight = 96,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        body.Measure(new Size(MaxToastWidth - 40, double.PositiveInfinity));
        double width = Math.Clamp(body.DesiredSize.Width + 40, 260, MaxToastWidth);

        // Time-left line: a faint track with the accent draining out of it.
        _progress = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = FindBrush("FrameBrush"),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        Border track = new Border
        {
            Height = 2,
            Margin = new Thickness(0, 12, 0, 0),
            CornerRadius = new CornerRadius(1),
            Background = FindBrush("HairlineBrush"),
            Child = _progress
        };

        StackPanel stack = new StackPanel { Margin = new Thickness(18, 12, 18, 14) };
        stack.Children.Add(headingRow);
        stack.Children.Add(body);
        stack.Children.Add(track);
        Border card = new Border
        {
            Background = FindBrush("SideBrush"),
            BorderBrush = FindBrush("HairlineStrongBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = stack
        };
        card.Measure(new Size(width, double.PositiveInfinity));
        double height = Math.Ceiling(card.DesiredSize.Height);
        Width = width;
        Height = height;
        _slotHeight = height;
        Content = card;

        TransformGroup group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_slide);
        card.RenderTransform = group;
        if (motion == NotificationMotion.Pop)
        {
            Opacity = 0;
            _scale.ScaleX = 0.62;
            _scale.ScaleY = 0.62;
            card.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        else if (motion == NotificationMotion.Slide)
        {
            _slide.X = width;
            card.RenderTransformOrigin = new Point(1, 0.5);
        }

        MouseDown += (_, _) => BeginLeave();
    }

    // Driven per rendered frame rather than by a DispatcherTimer: Windows throttles timers
    // for apps with no visible window (e.g. minimized to tray), which made the motion stutter.
    private void StartClock()
    {
        if (_ticking)
            return;

        _ticking = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopClock()
    {
        if (!_ticking)
            return;

        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e) => Step();

    protected override void OnClosed(EventArgs e)
    {
        StopClock();
        base.OnClosed(e);
    }

    private void StartProgress(int delayMs)
    {
        if (_progress.RenderTransform is not ScaleTransform bar)
            return;

        // Runs on WPF's animation clock so it stays smooth even when the UI thread is quiet.
        bar.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(_holdMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs)
        });
    }

    public static void ShowMatches(IReadOnlyList<(string Keywords, string Line)> hits, int seconds, NotificationMotion motion, bool playSound, string? soundPath)
    {
        if (hits.Count == 0)
            return;

        int holdMs = Math.Clamp(seconds, 1, 60) * 1000;
        foreach ((string _, string line) in hits.Take(3))
            ShowToast(Locale.Get("toastKeyword"), line, motion, holdMs);

        if (playSound)
            NotificationPing.Play(soundPath, fallbackToDefault: true);
    }

    public static void ShowSavedLog(string path, int seconds, NotificationMotion motion, bool playSound, string? soundPath)
    {
        string name = Path.GetFileName(path);
        ShowToast(Locale.Get("toastLogSaved"), string.IsNullOrWhiteSpace(name) ? Locale.Get("toastLogSavedBody") : name, motion, Math.Clamp(seconds, 1, 60) * 1000);
        if (playSound)
            NotificationPing.Play(soundPath, fallbackToDefault: true);
    }

    private static void ShowToast(string title, string line, NotificationMotion motion, int holdMs)
    {
        KeywordToastWindow? oldest = null;
        lock (OpenToasts)
        {
            if (OpenToasts.Count >= 3)
                oldest = OpenToasts[0];
        }

        oldest?.Close();
        KeywordToastWindow toast = new KeywordToastWindow(title, line, motion, holdMs);
        toast.Closed += (_, _) =>
        {
            lock (OpenToasts)
                OpenToasts.Remove(toast);
            Reflow();
        };
        lock (OpenToasts)
            OpenToasts.Add(toast);
        Reflow();
        toast.Show();
        toast.BeginEnter();
    }

    public static void CloseAll()
    {
        KeywordToastWindow[] snapshot;
        lock (OpenToasts)
            snapshot = OpenToasts.ToArray();
        foreach (KeywordToastWindow toast in snapshot)
            toast.Close();
    }

    private void BeginEnter()
    {
        _phaseStart = Environment.TickCount64;
        _leaving = false;
        if (_motion == NotificationMotion.None)
        {
            Opacity = 1;
            StartProgress(0);
            StartClock();
            return;
        }

        if (_motion == NotificationMotion.Pop)
        {
            Opacity = 0;
            _scale.ScaleX = 0.62;
            _scale.ScaleY = 0.62;
            _slide.X = 0;
        }
        else
        {
            Opacity = 1;
            _slide.X = Width;
        }

        StartProgress(280);
        StartClock();
    }

    private void BeginLeave()
    {
        if (_leaving)
            return;

        _leaving = true;
        _phaseStart = Environment.TickCount64;
    }

    private void Step()
    {
        long elapsed = Environment.TickCount64 - _phaseStart;
        if (!_leaving)
        {
            double enter = Math.Clamp(elapsed / 280.0, 0, 1);
            double eased = 1 - Math.Pow(1 - enter, 3);
            if (_motion == NotificationMotion.Pop)
            {
                Opacity = eased;
                _slide.X = 0;
                double scale = 0.62 + 0.38 * eased;
                _scale.ScaleX = scale;
                _scale.ScaleY = scale;
            }
            else if (_motion == NotificationMotion.Slide)
            {
                _slide.X = Width * (1 - eased);
            }

            if (elapsed >= (_motion == NotificationMotion.None ? 0 : 280) + _holdMs)
                BeginLeave();
            return;
        }

        if (_motion == NotificationMotion.None)
        {
            Close();
            return;
        }

        double leave = Math.Clamp(elapsed / 140.0, 0, 1);
        double leaveEased = leave * leave;
        if (_motion == NotificationMotion.Pop)
        {
            Opacity = 1 - leaveEased;
            double scale = 1 - 0.2 * leaveEased;
            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
        }
        else
        {
            _slide.X = Width * leaveEased;
        }

        if (leave >= 1)
            Close();
    }

    private static void Reflow()
    {
        List<KeywordToastWindow> snapshot;
        lock (OpenToasts)
            snapshot = OpenToasts.ToList();

        Rect area = WorkArea();
        double y = area.Bottom - Edge;
        for (int i = snapshot.Count - 1; i >= 0; i--)
        {
            KeywordToastWindow toast = snapshot[i];
            y -= toast._slotHeight;
            toast.Left = area.Right - toast.Width - Edge;
            toast.Top = y;
            y -= Gap;
        }
    }

    private static Rect WorkArea()
    {
        POINT point = GetCursorPos(out POINT cursor) ? cursor : default;
        nint monitor = MonitorFromPoint(point, 2);
        MONITORINFO info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            RECT work = info.rcWork;
            return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        }

        return SystemParameters.WorkArea;
    }

    private static Brush FindBrush(string key) => (Brush)Application.Current.FindResource(key);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
