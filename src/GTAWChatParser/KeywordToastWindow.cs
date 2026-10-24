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

    private readonly DispatcherTimer _clock;
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Border _progress;
    private long _phaseStart;
    private int _holdMs = 5000;
    private bool _leaving;
    private NotificationMotion _motion = NotificationMotion.Slide;
    private double _slotHeight;

    private KeywordToastWindow(string line, NotificationMotion motion, int holdMs)
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

        var title = new TextBlock
        {
            Text = "Keyword Notification",
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("FrameBrush"),
            FontSize = 13
        };
        var body = new TextBlock
        {
            Text = line,
            Foreground = FindBrush("LineBrush"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            MaxHeight = 96,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        body.Measure(new Size(MaxToastWidth - 48, double.PositiveInfinity));
        var width = Math.Clamp(Math.Max(180, body.DesiredSize.Width + 48), 220, MaxToastWidth);
        var height = 18 + title.FontSize + 8 + Math.Min(96, body.DesiredSize.Height) + 22;
        Width = width;
        Height = height;
        _slotHeight = height;

        _progress = new Border
        {
            Height = 3,
            Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("FrameBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        var stack = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(_progress);
        var card = new Border
        {
            Background = FindBrush("CardBrush"),
            CornerRadius = new CornerRadius(10),
            Child = stack
        };
        Content = card;

        var group = new TransformGroup();
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
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _clock.Tick += (_, _) => Step();
    }

    public static void ShowMatches(IReadOnlyList<(string Keywords, string Line)> hits, int seconds, NotificationMotion motion, bool playSound)
    {
        if (hits.Count == 0)
            return;

        var holdMs = Math.Clamp(seconds, 1, 60) * 1000;
        foreach (var (_, line) in hits.Take(3))
        {
            KeywordToastWindow? oldest = null;
            lock (OpenToasts)
            {
                if (OpenToasts.Count >= 3)
                    oldest = OpenToasts[0];
            }

            oldest?.Close();
            var toast = new KeywordToastWindow(line, motion, holdMs);
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

        if (playSound)
            NotificationPing.Play();
    }

    public static void CloseAll()
    {
        KeywordToastWindow[] snapshot;
        lock (OpenToasts)
            snapshot = OpenToasts.ToArray();
        foreach (var toast in snapshot)
            toast.Close();
    }

    private void BeginEnter()
    {
        _phaseStart = Environment.TickCount64;
        _leaving = false;
        if (_motion == NotificationMotion.None)
        {
            Opacity = 1;
            _clock.Start();
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

        _clock.Start();
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
        var elapsed = Environment.TickCount64 - _phaseStart;
        if (!_leaving)
        {
            var enter = Math.Clamp(elapsed / 280.0, 0, 1);
            var eased = 1 - Math.Pow(1 - enter, 3);
            if (_motion == NotificationMotion.Pop)
            {
                Opacity = eased;
                _slide.X = 0;
                var scale = 0.62 + 0.38 * eased;
                _scale.ScaleX = scale;
                _scale.ScaleY = scale;
            }
            else if (_motion == NotificationMotion.Slide)
            {
                _slide.X = Width * (1 - eased);
            }

            var remain = 1 - Math.Clamp((elapsed - (_motion == NotificationMotion.None ? 0 : 280)) / (double)_holdMs, 0, 1);
            if (_progress.RenderTransform is ScaleTransform bar)
                bar.ScaleX = Math.Max(0, remain);
            if (elapsed >= (_motion == NotificationMotion.None ? 0 : 280) + _holdMs)
                BeginLeave();
            return;
        }

        if (_motion == NotificationMotion.None)
        {
            Close();
            return;
        }

        var leave = Math.Clamp(elapsed / 140.0, 0, 1);
        var leaveEased = leave * leave;
        if (_motion == NotificationMotion.Pop)
        {
            Opacity = 1 - leaveEased;
            var scale = 1 - 0.2 * leaveEased;
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

        var area = WorkArea();
        var y = area.Bottom - Edge;
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            var toast = snapshot[i];
            y -= toast._slotHeight;
            toast.Left = area.Right - toast.Width - Edge;
            toast.Top = y;
            y -= Gap;
        }
    }

    private static Rect WorkArea()
    {
        var point = GetCursorPos(out var cursor) ? cursor : default;
        var monitor = MonitorFromPoint(point, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var work = info.rcWork;
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
