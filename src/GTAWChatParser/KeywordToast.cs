namespace GTAWChatParser;

internal sealed class KeywordToast : Form
{
    private const int ToastWidth = 380;
    private const int ToastHeight = 104;
    private static readonly List<KeywordToast> OpenToasts = [];
    private static readonly Font TitleFont = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font BodyFont = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

    private readonly System.Windows.Forms.Timer _closeTimer;

    private KeywordToast(string title, string body)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(ToastWidth, ToastHeight);
        BackColor = Color.FromArgb(28, 28, 30);
        ForeColor = Color.White;
        DoubleBuffered = true;

        var titleLabel = new Label
        {
            Text = title,
            Font = TitleFont,
            ForeColor = Color.FromArgb(232, 168, 56),
            AutoEllipsis = true,
            Bounds = new Rectangle(18, 12, ToastWidth - 30, 22)
        };
        var bodyLabel = new Label
        {
            Text = body,
            Font = BodyFont,
            ForeColor = Color.FromArgb(235, 235, 235),
            Bounds = new Rectangle(18, 38, ToastWidth - 30, 52)
        };

        Controls.Add(titleLabel);
        Controls.Add(bodyLabel);
        titleLabel.Click += (_, _) => Close();
        bodyLabel.Click += (_, _) => Close();
        Click += (_, _) => Close();

        _closeTimer = new System.Windows.Forms.Timer { Interval = 7000 };
        _closeTimer.Tick += (_, _) => Close();
    }

    public static void ShowMatches(IReadOnlyList<(string Keywords, string Line)> hits)
    {
        if (hits.Count == 0)
            return;

        var shown = hits.Take(3).ToList();
        if (hits.Count > 3)
            shown[2] = ($"{hits.Count - 2} more matches", "Open the captured chat to read the rest.");

        foreach (var (keywords, line) in shown)
        {
            var body = line.Length > 180 ? line[..177] + "..." : line;
            var toast = new KeywordToast(keywords, body);
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
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _closeTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closeTimer.Stop();
        _closeTimer.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var accent = new SolidBrush(Color.FromArgb(232, 168, 56));
        e.Graphics.FillRectangle(accent, 0, 0, 5, Height);
        using var border = new Pen(Color.FromArgb(72, 72, 76));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    private static void Reflow()
    {
        var screen = Screen.PrimaryScreen ?? (Screen.AllScreens.Length > 0 ? Screen.AllScreens[0] : null);
        if (screen == null)
            return;

        var area = screen.WorkingArea;
        List<KeywordToast> snapshot;
        lock (OpenToasts)
            snapshot = OpenToasts.ToList();

        for (var index = 0; index < snapshot.Count; index++)
        {
            var toast = snapshot[index];
            if (toast.IsDisposed)
                continue;

            toast.Location = new Point(
                area.Right - toast.Width - 16,
                area.Bottom - (index + 1) * (toast.Height + 8) - 8);
        }
    }
}
