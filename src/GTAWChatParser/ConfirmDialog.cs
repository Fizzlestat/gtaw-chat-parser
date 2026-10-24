namespace GTAWChatParser;

internal sealed class ConfirmDialog : Form
{
    private readonly System.Windows.Forms.Timer _motion;
    private long _motionStart;
    private Point _restLocation;

    private ConfirmDialog()
    {
        Font = HudTheme.Body;
        Text = "Are you sure?";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(460, 198);
        DoubleBuffered = true;

        var shell = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Background, Padding = new Padding(24, 20, 24, 18) };
        Controls.Add(shell);

        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Text = "Are you sure?",
            Font = HudTheme.Title,
            ForeColor = HudTheme.Text,
            BackColor = HudTheme.Background
        };
        var message = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Text = "The current chat log is saved before the parser closes.",
            Font = HudTheme.Body,
            ForeColor = HudTheme.Muted,
            BackColor = HudTheme.Background
        };
        var buttons = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = HudTheme.Background };
        var cancel = new HudButton { Text = "Cancel", Primary = false };
        var close = new HudButton { Text = "Close", Primary = true };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        close.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(close);
        buttons.Resize += (_, _) =>
        {
            close.SetBounds(buttons.Width - 124, 4, 124, 36);
            cancel.SetBounds(buttons.Width - 124 - 10 - 112, 4, 112, 36);
        };

        shell.Controls.Add(buttons);
        shell.Controls.Add(message);
        shell.Controls.Add(title);

        AcceptButton = close;
        CancelButton = cancel;
        title.MouseDown += BeginDrag;
        shell.MouseDown += BeginDrag;
        HudWindow.ApplyFrame(this);

        _motion = new System.Windows.Forms.Timer { Interval = 16 };
        _motion.Tick += (_, _) => AdvanceMotion();
    }

    public static bool Ask(Form owner)
    {
        using var dialog = new ConfirmDialog();
        dialog.Icon = owner.Icon;
        if (owner.Visible && owner.WindowState != FormWindowState.Minimized)
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }

        dialog.StartPosition = FormStartPosition.CenterScreen;
        dialog.TopMost = true;
        return dialog.ShowDialog() == DialogResult.OK;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _restLocation = Location;
        Location = new Point(_restLocation.X, _restLocation.Y + 12);
        Opacity = 0.01;
        _motionStart = Environment.TickCount64;
        _motion.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _motion.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _motion.Dispose();
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CS_DROPSHADOW;
            return parameters;
        }
    }

    private void AdvanceMotion()
    {
        var amount = Math.Clamp((Environment.TickCount64 - _motionStart) / 180.0, 0, 1);
        var eased = 1 - Math.Pow(1 - amount, 3);
        Opacity = Math.Max(0.01, eased);
        Location = new Point(_restLocation.X, _restLocation.Y + (int)Math.Round(12 * (1 - eased)));
        if (amount < 1)
            return;

        Opacity = 1;
        Location = _restLocation;
        _motion.Stop();
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        ReleaseCapture();
        _ = SendMessage(Handle, 0xA1, 0x2, 0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
