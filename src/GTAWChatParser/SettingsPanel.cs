namespace GTAWChatParser;

internal sealed class SettingsPanel : Panel
{
    private readonly Panel _deck;
    private readonly List<SettingsCategory> _categories = [];
    private readonly HudToggle _notificationsEnabled = new();
    private readonly HudToggle _caseSensitive = new();
    private readonly TextBox _seconds = new();
    private readonly HudButton _slide = new() { Text = "Slide in" };
    private readonly HudButton _pop = new() { Text = "Pop in" };
    private readonly HudButton _none = new() { Text = "None" };
    private readonly System.Windows.Forms.Timer _deckTimer;
    private float[] _shown = [];
    private int _openIndex;
    private bool _ready;
    private bool _binding;
    private string _animation = "slide";
    private int _lastSeconds = 5;

    public SettingsPanel()
    {
        BackColor = HudTheme.Side;
        Padding = new Padding(16, 16, 16, 14);
        SetStyle(ControlStyles.ResizeRedraw, true);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "SETTINGS",
            Font = HudTheme.Section,
            ForeColor = HudTheme.Frame,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = HudTheme.Side
        };

        _deck = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = HudTheme.Side
        };

        var exitHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            BackColor = HudTheme.Side,
            Padding = new Padding(0, 14, 0, 0)
        };
        exitHost.Paint += (_, e) => HudShapes.Hairline(e.Graphics, 0, 8, exitHost.Width, 1, Color.FromArgb(90, HudTheme.Frame));
        var exit = new HudButton { Text = "Exit", Dock = DockStyle.Fill };
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        exitHost.Controls.Add(exit);

        Controls.Add(_deck);
        Controls.Add(exitHost);
        Controls.Add(title);

        _categories.Add(new SettingsCategory("Keywords", 228, BuildKeywords()));
        _categories.Add(new SettingsCategory("Logs", 176, BuildLogs()));
        _categories.Add(new SettingsCategory("Notifications", 236, BuildNotifications()));
        foreach (var category in _categories)
        {
            category.HeaderClicked += (_, _) => Toggle(category);
            _deck.Controls.Add(category);
        }

        _deckTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _deckTimer.Tick += (_, _) =>
        {
            if (StepDeck(snap: false))
                _deckTimer.Stop();
        };
        _openIndex = 0;
        _ready = true;
        Width = 400;
        ApplyOpenState();
    }

    public TextBox Keywords { get; private set; } = null!;

    public Label Folder { get; private set; } = null!;

    public event EventHandler? SaveKeywordsRequested;

    public event EventHandler? ChooseFolderRequested;

    public event EventHandler? OpenFolderRequested;

    public event EventHandler? SaveLogRequested;

    public event EventHandler? NotificationsChanged;

    public event EventHandler? ExitRequested;

    public bool NotificationsEnabled => _notificationsEnabled.Checked;

    public bool CaseSensitive => _caseSensitive.Checked;

    public int NotificationSeconds => _lastSeconds;

    public string NotificationAnimation => _animation;

    public void Bind(IReadOnlyList<string> keywords, string folder)
    {
        Keywords.Text = string.Join(Environment.NewLine, keywords);
        Folder.Text = folder;
    }

    public void BindNotifications(bool enabled, bool caseSensitive, int seconds, string animation)
    {
        _binding = true;
        _notificationsEnabled.Checked = enabled;
        _caseSensitive.Checked = caseSensitive;
        _lastSeconds = seconds is >= 1 and <= 60 ? seconds : 5;
        _seconds.Text = _lastSeconds.ToString();
        _animation = animation is "pop" or "none" ? animation : "slide";
        _slide.Primary = _animation == "slide";
        _pop.Primary = _animation == "pop";
        _none.Primary = _animation == "none";
        _binding = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutDeck();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutDeck();
    }

    private void Toggle(SettingsCategory category)
    {
        var index = _categories.IndexOf(category);
        _openIndex = _openIndex == index ? -1 : index;
        ApplyOpenState();
    }

    private void ApplyOpenState()
    {
        for (var i = 0; i < _categories.Count; i++)
            _categories[i].Open = i == _openIndex;
        LayoutDeck();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _deckTimer.Dispose();
        base.Dispose(disposing);
    }

    private void LayoutDeck()
    {
        if (!_ready || _deck.ClientSize.Width <= 0)
            return;

        var snap = _shown.Length != _categories.Count;
        if (StepDeck(snap))
            _deckTimer.Stop();
        else
            _deckTimer.Start();
    }

    private bool StepDeck(bool snap)
    {
        if (_deck.ClientSize.Width <= 0)
            return true;

        var width = _deck.ClientSize.Width;
        var targets = new float[_categories.Count];
        for (var i = 0; i < _categories.Count; i++)
        {
            var category = _categories[i];
            targets[i] = category.Open ? category.HeaderHeight + category.BodyHeight : category.HeaderHeight;
        }

        if (snap || _shown.Length != targets.Length)
            _shown = targets;

        var settled = true;
        var y = 0;
        for (var i = 0; i < _categories.Count; i++)
        {
            if (!snap)
            {
                var next = _shown[i] + (targets[i] - _shown[i]) * 0.34f;
                if (Math.Abs(targets[i] - next) < 0.8f)
                    next = targets[i];
                else
                    settled = false;
                _shown[i] = next;
            }

            var category = _categories[i];
            var height = Math.Max(category.HeaderHeight, (int)Math.Round(_shown[i]));
            category.SetBounds(0, y, width, Math.Max(category.HeaderHeight, height));
            category.SyncReveal();
            y += height + 8;
        }

        _deck.AutoScrollMinSize = new Size(0, Math.Max(0, y - 8));
        return settled;
    }

    private Control BuildKeywords()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Card, Padding = new Padding(14, 10, 14, 12) };
        var hint = Hint("One keyword or phrase per line.");
        hint.Dock = DockStyle.Top;
        hint.Height = 20;
        var underHint = Spacer(8);

        var save = new HudButton { Text = "Save keywords", Primary = true, Dock = DockStyle.Bottom, Height = 36 };
        save.Click += (_, _) => SaveKeywordsRequested?.Invoke(this, EventArgs.Empty);
        var aboveSave = Spacer(10);
        aboveSave.Dock = DockStyle.Bottom;

        var well = new HudWell { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 4, 6) };
        Keywords = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = HudTheme.Body,
            BackColor = HudTheme.Transcript,
            ForeColor = HudTheme.Text,
            Dock = DockStyle.Fill
        };
        well.Controls.Add(Keywords);

        body.Controls.Add(well);
        body.Controls.Add(aboveSave);
        body.Controls.Add(save);
        body.Controls.Add(underHint);
        body.Controls.Add(hint);
        return body;
    }

    private Control BuildLogs()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Card, Padding = new Padding(14, 8, 14, 12) };
        Folder = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            ForeColor = HudTheme.Text,
            Font = HudTheme.Body,
            BackColor = HudTheme.Card
        };
        var hint = Hint("Saved as Year\\MON\\dd.MON.yyyy-HH.mm.ss.txt");
        hint.Dock = DockStyle.Top;
        hint.Height = 22;

        var save = new HudButton { Text = "Save log now", Primary = true, Dock = DockStyle.Bottom, Height = 36 };
        save.Click += (_, _) => SaveLogRequested?.Invoke(this, EventArgs.Empty);
        var aboveSave = Spacer(8);
        aboveSave.Dock = DockStyle.Bottom;

        var row = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = HudTheme.Card };
        var change = new HudButton { Text = "Change folder" };
        var open = new HudButton { Text = "Open folder" };
        change.Click += (_, _) => ChooseFolderRequested?.Invoke(this, EventArgs.Empty);
        open.Click += (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        PlaceHalves(row, change, open);

        body.Controls.Add(row);
        body.Controls.Add(aboveSave);
        body.Controls.Add(save);
        body.Controls.Add(hint);
        body.Controls.Add(Folder);
        return body;
    }

    private Control BuildNotifications()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Card, Padding = new Padding(14, 10, 14, 12) };
        _notificationsEnabled.CheckedChanged += (_, _) => NotifyNotifications();
        _caseSensitive.CheckedChanged += (_, _) => NotifyNotifications();
        _slide.Click += (_, _) => SelectAnimation("slide");
        _pop.Click += (_, _) => SelectAnimation("pop");
        _none.Click += (_, _) => SelectAnimation("none");
        _seconds.BorderStyle = BorderStyle.None;
        _seconds.Font = HudTheme.Body;
        _seconds.BackColor = HudTheme.Transcript;
        _seconds.ForeColor = HudTheme.Text;
        _seconds.TextAlign = HorizontalAlignment.Center;
        _seconds.Text = "5";
        _seconds.Leave += (_, _) => CommitDuration();
        _seconds.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter)
                return;

            CommitDuration();
            args.SuppressKeyPress = true;
        };

        var animation = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = HudTheme.Card };
        PlaceThirds(animation, _slide, _pop, _none);
        var animationLabel = Hint("Animation");
        animationLabel.Dock = DockStyle.Top;
        animationLabel.Height = 22;

        body.Controls.Add(animation);
        body.Controls.Add(Spacer(6));
        body.Controls.Add(animationLabel);
        body.Controls.Add(Spacer(8));
        body.Controls.Add(DurationRow());
        body.Controls.Add(Spacer(8));
        body.Controls.Add(LabeledRow("Case sensitive", _caseSensitive));
        body.Controls.Add(Spacer(8));
        body.Controls.Add(LabeledRow("Notifications", _notificationsEnabled));
        return body;
    }

    private Panel DurationRow()
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = HudTheme.Card };
        var label = new Label
        {
            Text = "Duration",
            ForeColor = HudTheme.Text,
            Font = HudTheme.Body,
            BackColor = HudTheme.Card,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var suffix = new Label
        {
            Text = "seconds",
            ForeColor = HudTheme.Muted,
            Font = HudTheme.Body,
            BackColor = HudTheme.Card,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var well = new HudWell { Padding = new Padding(6, 5, 6, 4) };
        _seconds.Dock = DockStyle.Fill;
        well.Controls.Add(_seconds);
        row.Controls.Add(label);
        row.Controls.Add(well);
        row.Controls.Add(suffix);
        row.Resize += (_, _) =>
        {
            label.SetBounds(0, 0, 110, row.Height);
            suffix.SetBounds(row.Width - 68, 0, 68, row.Height);
            well.SetBounds(row.Width - 136, 3, 62, 30);
        };
        return row;
    }

    private static Panel LabeledRow(string caption, Control value)
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = HudTheme.Card };
        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = HudTheme.Text,
            Font = HudTheme.Body,
            BackColor = HudTheme.Card
        };
        value.Dock = DockStyle.Right;
        value.Width = 78;
        row.Controls.Add(label);
        row.Controls.Add(value);
        return row;
    }

    private void SelectAnimation(string animation)
    {
        _animation = animation;
        _slide.Primary = animation == "slide";
        _pop.Primary = animation == "pop";
        _none.Primary = animation == "none";
        NotifyNotifications();
    }

    private void CommitDuration()
    {
        if (!int.TryParse(_seconds.Text.Trim(), out var seconds) || seconds is < 1 or > 60)
        {
            _seconds.Text = _lastSeconds.ToString();
            return;
        }

        _seconds.Text = seconds.ToString();
        if (seconds == _lastSeconds)
            return;

        _lastSeconds = seconds;
        NotifyNotifications();
    }

    private void NotifyNotifications()
    {
        if (_binding)
            return;

        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void PlaceThirds(Control row, Control left, Control middle, Control right)
    {
        void Place(object? sender, EventArgs e)
        {
            var width = Math.Max(90, row.ClientSize.Width - 16);
            var part = width / 3;
            var height = Math.Max(1, row.ClientSize.Height);
            left.SetBounds(0, 0, part, height);
            middle.SetBounds(part + 8, 0, part, height);
            right.SetBounds(part + 8 + part + 8, 0, Math.Max(1, width - part * 2), height);
        }

        row.Resize += Place;
        row.Controls.Add(left);
        row.Controls.Add(middle);
        row.Controls.Add(right);
        Place(null, EventArgs.Empty);
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        Font = HudTheme.Subtitle,
        ForeColor = HudTheme.Muted,
        BackColor = HudTheme.Card
    };

    private static Panel Spacer(int height) => new()
    {
        Dock = DockStyle.Top,
        Height = height,
        BackColor = HudTheme.Card
    };

    private static void PlaceHalves(Control row, Control left, Control right)
    {
        void Place(object? sender, EventArgs e)
        {
            var width = Math.Max(80, row.ClientSize.Width - 8);
            var half = width / 2;
            left.SetBounds(0, 0, half, Math.Max(1, row.ClientSize.Height));
            right.SetBounds(half + 8, 0, Math.Max(1, width - half), Math.Max(1, row.ClientSize.Height));
        }

        row.Resize += Place;
        row.Controls.Add(left);
        row.Controls.Add(right);
        Place(null, EventArgs.Empty);
    }
}

internal sealed class SettingsCategory : Panel
{
    private readonly Panel _accent;

    public SettingsCategory(string title, int bodyHeight, Control content)
    {
        HeaderHeight = 44;
        BodyHeight = bodyHeight;
        BackColor = HudTheme.Side;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Header = new CategoryHeader(title) { Dock = DockStyle.Top, Height = HeaderHeight };
        Header.Click += (_, _) => HeaderClicked?.Invoke(this, EventArgs.Empty);

        Body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = HudTheme.Card,
            Visible = false
        };
        content.Dock = DockStyle.Fill;
        Body.Controls.Add(content);

        _accent = new Panel
        {
            Width = 3,
            BackColor = HudTheme.Frame,
            Visible = false
        };

        Controls.Add(Body);
        Controls.Add(Header);
        Controls.Add(_accent);
        _accent.BringToFront();
    }

    public CategoryHeader Header { get; }

    public Panel Body { get; }

    public int HeaderHeight { get; }

    public int BodyHeight { get; }

    public bool Open
    {
        get => Header.Open;
        set
        {
            Header.Open = value;
            Header.Invalidate();
        }
    }

    public void SyncReveal()
    {
        var revealed = Height > HeaderHeight + 2;
        if (Body.Visible != revealed)
            Body.Visible = revealed;

        var barHeight = Math.Max(0, Height - 18);
        var show = Height > HeaderHeight + 8 && barHeight > 14;
        if (_accent.Visible != show)
            _accent.Visible = show;
        if (show)
            _accent.Bounds = new Rectangle(0, (Height - barHeight) / 2, 3, barHeight);
    }

    public event EventHandler? HeaderClicked;
}

internal sealed class CategoryHeader : Control
{
    private readonly HudEase _ease;
    private bool _open;

    public CategoryHeader(string title)
    {
        Title = title;
        Cursor = Cursors.Hand;
        BackColor = HudTheme.Side;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _ease = new HudEase(this);
        _ease.Snap("hot", 0f);
        _ease.Snap("open", 0f);
    }

    public string Title { get; }

    public bool Open
    {
        get => _open;
        set
        {
            _open = value;
            if (IsHandleCreated)
                _ease.Set("open", value ? 1f : 0f);
            else
                _ease.Snap("open", value ? 1f : 0f);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _ease.Set("hot", 1f);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _ease.Set("hot", 0f);
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _ease.Set("press", 1f);
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _ease.Set("press", 0f);
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ease.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var open = _ease["open"];
        var hot = Math.Clamp(_ease["hot"] + _ease["press"] * 0.25f, 0f, 1f);
        var resting = HudTheme.Lerp(HudTheme.Side, HudTheme.Card, open);
        var hover = HudTheme.Lerp(HudTheme.Button, HudTheme.ButtonHover, open);
        var fill = HudTheme.Lerp(resting, hover, hot * (open > 0.5f ? 0.4f : 1f));
        using var brush = new SolidBrush(fill);
        graphics.FillRectangle(brush, ClientRectangle);

        var chevron = HudTheme.Lerp(HudTheme.Muted, HudTheme.Frame, Math.Max(open, hot));
        using var pen = new Pen(chevron, 1.6f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        var midY = Height / 2f;
        var spread = 4f - open * 1.5f;
        if (open >= 0.5f)
        {
            graphics.DrawLines(pen, new[]
            {
                new PointF(16, midY - 2f),
                new PointF(20, midY + 2.5f),
                new PointF(24, midY - 2f)
            });
        }
        else
        {
            graphics.DrawLines(pen, new[]
            {
                new PointF(16, midY - spread),
                new PointF(20, midY),
                new PointF(16, midY + spread)
            });
        }

        TextRenderer.DrawText(
            graphics,
            Title,
            HudTheme.ButtonFont,
            new Rectangle(34, 0, Math.Max(1, Width - 48), Height),
            HudTheme.Lerp(HudTheme.Line, HudTheme.Text, Math.Max(open, hot * 0.6f)),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (open < 0.98f)
        {
            var alpha = (int)Math.Round(80 * (1f - open));
            if (alpha > 0)
                HudShapes.Hairline(graphics, 14, Height - 1, Math.Max(1, Width - 28), 1, Color.FromArgb(alpha, HudTheme.Frame));
        }
    }
}
