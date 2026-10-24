namespace GTAWChatParser;

internal enum NotificationMotion
{
    Slide,
    Pop,
    None
}

internal static class NotificationMotionExtensions
{
    public static NotificationMotion Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pop" => NotificationMotion.Pop,
        "none" => NotificationMotion.None,
        _ => NotificationMotion.Slide
    };

    public static string Format(NotificationMotion motion) => motion switch
    {
        NotificationMotion.Pop => "pop",
        NotificationMotion.None => "none",
        _ => "slide"
    };
}

internal sealed class KeywordToast : Form
{
    private const int MaxToastWidth = 440;
    private const int Edge = 16;
    private const int Gap = 10;
    private const int EnterMs = 280;
    private const int LeaveMs = 140;

    private static readonly List<KeywordToast> OpenToasts = [];

    private readonly System.Windows.Forms.Timer _clock;

    private Phase _phase = Phase.Enter;
    private long _phaseStart;
    private int _offRight;
    private int _leaveFromX;
    private int _pinnedY;
    private float _remain = 1f;
    private Point _target;
    private bool _dismissed;
    private NotificationMotion _motion = NotificationMotion.Slide;
    private int _holdMs = 5000;
    private Size _slot;

    private KeywordToast(string line)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = HudTheme.Card;
        ForeColor = HudTheme.Text;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        const int side = 16;
        var title = "Keyword Notification";
        var titleWidth = TextRenderer.MeasureText(title, HudTheme.ButtonFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        var single = TextRenderer.MeasureText(line, HudTheme.Body, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var textWidth = Math.Min(int.MaxValue - 8, single.Width + 8);
        var inner = Math.Clamp(Math.Max(titleWidth, textWidth), titleWidth, MaxToastWidth - side * 2);
        var wrapped = TextRenderer.MeasureText(line, HudTheme.Body, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
        var bodyHeight = Math.Min(wrapped.Height + 1, 96);
        Size = new Size(inner + side * 2, 12 + 18 + 6 + bodyHeight + 18);

        var titleLabel = new Label
        {
            Text = title,
            Font = HudTheme.ButtonFont,
            ForeColor = HudTheme.Frame,
            BackColor = HudTheme.Card,
            AutoEllipsis = true,
            Cursor = Cursors.Hand,
            Bounds = new Rectangle(side, 10, inner, 18)
        };
        var bodyLabel = new Label
        {
            Text = line,
            Font = HudTheme.Body,
            ForeColor = HudTheme.Line,
            BackColor = HudTheme.Card,
            UseMnemonic = false,
            AutoEllipsis = wrapped.Height > 96,
            Cursor = Cursors.Hand,
            Bounds = new Rectangle(side, 34, inner, bodyHeight)
        };

        Controls.Add(titleLabel);
        Controls.Add(bodyLabel);
        titleLabel.MouseDown += (_, _) => BeginLeave();
        bodyLabel.MouseDown += (_, _) => BeginLeave();
        MouseDown += (_, _) => BeginLeave();

        _clock = new System.Windows.Forms.Timer { Interval = 15 };
        _clock.Tick += (_, _) => Step();
    }

    private enum Phase
    {
        Enter,
        Hold,
        Leave
    }

    public static void ShowMatches(IReadOnlyList<(string Keywords, string Line)> hits, int seconds, NotificationMotion motion)
    {
        if (hits.Count == 0)
            return;

        var holdMs = Math.Clamp(seconds, 1, 60) * 1000;
        var area = WorkArea();
        foreach (var (_, line) in hits.Take(3))
        {
            var toast = new KeywordToast(line);
            toast.PlaceInitial(area, motion, holdMs);
            lock (OpenToasts)
                OpenToasts.Add(toast);
            toast.FormClosed += (_, _) =>
            {
                lock (OpenToasts)
                    OpenToasts.Remove(toast);
                Reflow();
            };
            toast.Show();
        }

        Reflow();
        System.Media.SystemSounds.Asterisk.Play();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_COMPOSITED = 0x02000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_COMPOSITED;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _clock.Start();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width < 8 || ClientSize.Height < 8)
            return;

        var previous = Region;
        using var path = HudShapes.Rounded(new RectangleF(0.5f, 0.5f, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1)), 10);
        Region = new Region(path);
        if (previous is not null)
            previous.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _clock.Stop();
        _clock.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        var bar = new RectangleF(10, 12, 3, Math.Max(8, Height - 28));
        using (var accent = new SolidBrush(HudTheme.Frame))
        using (var path = HudShapes.Rounded(bar, 1.5f))
            graphics.FillPath(accent, path);

        var track = new RectangleF(18, Height - 10, Math.Max(8, Width - 36), 3);
        using (var trackBrush = new SolidBrush(Color.FromArgb(64, HudTheme.Frame)))
        using (var trackPath = HudShapes.Rounded(track, 1.5f))
            graphics.FillPath(trackBrush, trackPath);
        var fillWidth = track.Width * Math.Clamp(_remain, 0f, 1f);
        if (fillWidth > 1f)
        {
            using var accent = new SolidBrush(HudTheme.Frame);
            using var fillPath = HudShapes.Rounded(new RectangleF(track.X, track.Y, fillWidth, track.Height), 1.5f);
            graphics.FillPath(accent, fillPath);
        }
    }

    private void PlaceInitial(Rectangle area, NotificationMotion motion, int holdMs)
    {
        _motion = motion;
        _holdMs = holdMs;
        _slot = Size;
        _offRight = area.Right;
        var y = area.Bottom - _slot.Height - Edge;
        _target = new Point(area.Right - _slot.Width - Edge, y);
        _phaseStart = Environment.TickCount64;
        _remain = 1f;
        if (motion == NotificationMotion.None)
        {
            _phase = Phase.Hold;
            Opacity = 1;
            Location = _target;
            return;
        }

        _phase = Phase.Enter;
        if (motion == NotificationMotion.Pop)
        {
            Opacity = 0;
            ApplyScale(0f);
            return;
        }

        Opacity = 1;
        Location = new Point(_offRight, y);
    }

    private void ApplyScale(float amount)
    {
        var scale = 0.82f + 0.18f * Math.Clamp(amount, 0f, 1f);
        var width = Math.Max(1, (int)Math.Round(_slot.Width * scale));
        var height = Math.Max(1, (int)Math.Round(_slot.Height * scale));
        var x = _target.X + (_slot.Width - width) / 2;
        var y = _target.Y + (_slot.Height - height) / 2;
        if (Width != width || Height != height || Location.X != x || Location.Y != y)
            SetBounds(x, y, width, height);

        var opacity = Math.Clamp(amount, 0f, 1f);
        if (Math.Abs(Opacity - opacity) > 0.02)
            Opacity = opacity;
    }

    private Size SlotSize() => _slot;

    private void SetTarget(Point target, int offRight)
    {
        _target = target;
        _offRight = offRight;
    }

    private void BeginLeave()
    {
        if (_phase == Phase.Leave || _dismissed || IsDisposed)
            return;

        if (_motion == NotificationMotion.None)
        {
            _dismissed = true;
            _clock.Stop();
            Close();
            return;
        }

        _leaveFromX = Location.X;
        _pinnedY = Location.Y;
        _phase = Phase.Leave;
        _phaseStart = Environment.TickCount64;
    }

    private void Step()
    {
        if (IsDisposed || _dismissed)
            return;

        var elapsed = Environment.TickCount64 - _phaseStart;
        if (_motion == NotificationMotion.Pop && _phase == Phase.Enter)
        {
            var enter = Math.Clamp(elapsed / (float)EnterMs, 0f, 1f);
            ApplyScale(EaseOut(enter));
            if (enter >= 1f)
                FinishEntrance();
            Invalidate(new Rectangle(0, Height - 16, Width, 16));
            return;
        }

        if (_motion == NotificationMotion.Pop && _phase == Phase.Leave)
        {
            var leave = Math.Clamp(elapsed / (float)LeaveMs, 0f, 1f);
            ApplyScale(1f - leave * leave);
            if (leave >= 1f)
                Dismiss();
            return;
        }

        int x;
        switch (_phase)
        {
            case Phase.Enter:
                var enter = Math.Clamp(elapsed / (float)EnterMs, 0f, 1f);
                x = Lerp(_offRight, _target.X, EaseOut(enter));
                if (enter >= 1f)
                    FinishEntrance();
                break;
            case Phase.Hold:
                _remain = 1f - elapsed / (float)_holdMs;
                x = _target.X;
                if (_remain <= 0f)
                {
                    _remain = 0f;
                    BeginLeave();
                    x = Location.X;
                }

                break;
            default:
                var leave = Math.Clamp(elapsed / (float)LeaveMs, 0f, 1f);
                x = Lerp(_leaveFromX, _offRight, leave * leave);
                if (leave >= 1f)
                {
                    Dismiss();
                    return;
                }

                break;
        }

        var yTarget = _phase == Phase.Leave ? _pinnedY : _target.Y;
        var y = Location.Y;
        if (y != yTarget)
        {
            var next = y + (yTarget - y) * 0.28f;
            y = Math.Abs(yTarget - next) < 1f ? yTarget : (int)Math.Round(next);
        }

        if (Location.X != x || Location.Y != y)
            Location = new Point(x, y);

        Invalidate(new Rectangle(0, Height - 16, Width, 16));
    }

    private static void Reflow()
    {
        var area = WorkArea();
        List<KeywordToast> snapshot;
        lock (OpenToasts)
            snapshot = OpenToasts.ToList();

        var parked = snapshot.Where(toast => toast._phase != Phase.Leave && !toast.IsDisposed).ToList();
        var y = area.Bottom - Edge;
        for (var index = parked.Count - 1; index >= 0; index--)
        {
            var toast = parked[index];
            var slot = toast.SlotSize();
            y -= slot.Height;
            toast.SetTarget(new Point(area.Right - slot.Width - Edge, y), area.Right);
            y -= Gap;
        }
    }

    private void FinishEntrance()
    {
        _phase = Phase.Hold;
        _phaseStart = Environment.TickCount64;
        _remain = 1f;
        Opacity = 1;
        if (Size != _slot)
            Size = _slot;
        Location = _target;
    }

    private void Dismiss()
    {
        _dismissed = true;
        _clock.Stop();
        Close();
    }

    private static Rectangle WorkArea()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        if (screen == null)
            screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();

        return screen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
    }

    private static int Lerp(int from, int to, float amount) =>
        (int)Math.Round(from + (to - from) * amount);

    private static float EaseOut(float amount) => 1f - MathF.Pow(1f - amount, 3f);
}
