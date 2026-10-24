using System.Runtime.InteropServices;

namespace GTAWChatParser;

internal sealed class HudForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    private const int WM_SETREDRAW = 0x0B;

    private readonly AppConfig _config;
    private readonly CaptureService _service;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _noticeTimer;
    private readonly NotifyIcon _tray;

    private readonly Panel _header;
    private readonly Label _notice;
    private readonly Panel _statusPill;
    private readonly Label _statusLabel;
    private readonly HudChromeButton _minimize;
    private readonly HudChromeButton _trayButton;
    private readonly HudChromeButton _close;
    private readonly SettingsPanel _settings;
    private RichTextBox _chat = null!;
    private Label _empty = null!;

    private Task? _captureTask;
    private string _status = "Starting";
    private string _rendered = "\u0001";
    private string? _lastLogPath;
    private bool _started;
    private bool _closing;
    private bool _exitConfirmed;
    private bool _confirming;
    private bool _needsRestorePaint;

    public HudForm()
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

        Font = HudTheme.Body;
        Text = "GTA: World Chat Parser";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1020, 720);
        Size = new Size(1180, 760);
        BackColor = HudTheme.Frame;
        DoubleBuffered = true;
        Icon = CreateIcon();

        _tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "GTA: World Chat Parser",
            Visible = false
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                RestoreFromTray();
        };
        var trayMenu = new ContextMenuStrip
        {
            Renderer = new HudMenuRenderer(),
            BackColor = HudTheme.Card,
            ForeColor = HudTheme.Text,
            ShowImageMargin = false
        };
        trayMenu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = trayMenu;

        var shell = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Background };
        Controls.Add(shell);

        _header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = HudTheme.Header };
        _header.MouseDown += BeginDrag;
        _header.MouseDoubleClick += (_, _) => ToggleMaximize();
        _header.Paint += PaintHeader;

        _minimize = new HudChromeButton(ChromeGlyph.Minimize);
        _trayButton = new HudChromeButton(ChromeGlyph.Tray);
        _close = new HudChromeButton(ChromeGlyph.Close);
        _minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _trayButton.Click += (_, _) => HideToTray();
        _close.Click += (_, _) => Close();
        _header.Controls.Add(_minimize);
        _header.Controls.Add(_trayButton);
        _header.Controls.Add(_close);
        var tips = new ToolTip { ShowAlways = true };
        tips.SetToolTip(_minimize, "Minimize");
        tips.SetToolTip(_trayButton, "Minimize to tray");
        tips.SetToolTip(_close, "Close");
        _header.Resize += (_, _) => LayoutHeader();

        _statusPill = new Panel { Height = 28, BackColor = HudTheme.Header };
        _statusPill.Paint += PaintStatus;
        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = HudTheme.Text,
            Font = HudTheme.Body,
            BackColor = Color.Transparent
        };
        _statusPill.Controls.Add(_statusLabel);
        _header.Controls.Add(_statusPill);

        _notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 0,
            Visible = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 12, 0),
            ForeColor = HudTheme.Background,
            BackColor = HudTheme.Frame,
            Font = HudTheme.Body
        };

        _settings = BuildSettings();
        var chatColumn = BuildChatColumn();

        shell.Controls.Add(chatColumn);
        var divider = new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = HudTheme.Lerp(HudTheme.Background, HudTheme.Frame, 0.42f)
        };
        shell.Controls.Add(divider);
        shell.Controls.Add(_settings);
        shell.Controls.Add(_notice);
        shell.Controls.Add(_header);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _refreshTimer.Tick += (_, _) => RenderChat(_service.GetSnapshot());
        _noticeTimer = new System.Windows.Forms.Timer { Interval = 4200 };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice.Height = 0;
            _notice.Visible = false;
            _notice.Text = string.Empty;
        };

        _service.StatusChanged += status => Post(() => SetStatus(status));
        _service.LinesAppended += lines => Post(() => OnLines(lines));
        _service.LogSaved += (path, gameClosed) => Post(() => OnLogSaved(path, gameClosed));

        BindFields();
        SetStatus("Starting");
        HudWindow.ApplyFrame(this);
        AppLog.Info("Started.");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_started)
            return;

        _started = true;
        _refreshTimer.Start();
        _captureTask = Task.Run(() => _service.RunAsync(_shutdown.Token));
        RenderChat(_service.GetSnapshot());
        _header.Invalidate(true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closing)
        {
            base.OnFormClosing(e);
            return;
        }

        if (!_exitConfirmed && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (_confirming)
                return;

            _confirming = true;
            BeginInvoke(() =>
            {
                try
                {
                    if (IsDisposed || !ConfirmDialog.Ask(this))
                        return;

                    _exitConfirmed = true;
                    Close();
                }
                finally
                {
                    _confirming = false;
                }
            });
            return;
        }

        _closing = true;
        _refreshTimer.Stop();
        _noticeTimer.Stop();
        _tray.Visible = false;
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
            // The capture loop is already stopping.
        }

        _service.Dispose();
        _shutdown.Dispose();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_SIZE = 0x0005;
        const int SIZE_MINIMIZED = 1;
        base.WndProc(ref m);
        if (m.Msg != WM_SIZE || !_started)
            return;

        if (m.WParam.ToInt32() == SIZE_MINIMIZED)
        {
            _needsRestorePaint = true;
            return;
        }

        if (!_needsRestorePaint)
            return;

        _needsRestorePaint = false;
        BeginInvoke(new Action(RepaintSurface));
    }

    private void RepaintSurface()
    {
        if (IsDisposed || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized)
            return;

        const uint RDW_INVALIDATE = 0x0001;
        const uint RDW_ALLCHILDREN = 0x0080;
        const uint RDW_UPDATENOW = 0x0100;
        Invalidate(true);
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
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

    private SettingsPanel BuildSettings()
    {
        var settings = new SettingsPanel { Dock = DockStyle.Right };
        settings.SaveKeywordsRequested += (_, _) => SaveKeywords();
        settings.ChooseFolderRequested += (_, _) => ChooseLogFolder();
        settings.OpenFolderRequested += (_, _) => OpenLogFolder();
        settings.SaveLogRequested += (_, _) => SaveNow();
        settings.NotificationsChanged += (_, _) => SaveNotificationSettings();
        settings.ExitRequested += (_, _) => Close();
        return settings;
    }

    private Control BuildChatColumn()
    {
        var column = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 8, 16),
            BackColor = HudTheme.Background
        };

        var frame = new HudFrame { Dock = DockStyle.Fill };
        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "LIVE TRANSCRIPT",
            Font = HudTheme.Section,
            ForeColor = HudTheme.Frame,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            BackColor = HudTheme.Transcript
        };

        var transcript = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Transcript };
        _chat = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = HudTheme.Transcript,
            ForeColor = HudTheme.Line,
            Font = HudTheme.TranscriptFont,
            HideSelection = true
        };
        _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = HudTheme.Muted,
            Font = HudTheme.Subtitle,
            Text = "Waiting for FiveM.\r\nChat already on screen is picked up as soon as the HUD attaches.",
            BackColor = HudTheme.Transcript
        };
        transcript.Controls.Add(_chat);
        transcript.Controls.Add(_empty);

        frame.Controls.Add(transcript);
        frame.Controls.Add(caption);
        column.Controls.Add(frame);
        return column;
    }

    private void BindFields()
    {
        _settings.Bind(_config.Keywords, _config.BackupDirectory);
        _settings.BindNotifications(
            _config.NotificationsEnabled,
            _config.NotificationCaseSensitive,
            _config.ResolvedNotificationSeconds,
            _config.NotificationAnimation);
    }

    private void SaveKeywords()
    {
        _config.Keywords = AppConfig.NormalizeKeywords(
            _settings.Keywords.Text.Split(["\r\n", "\n"], StringSplitOptions.None),
            _config.NotificationCaseSensitive);
        _config.Save();
        _settings.Bind(_config.Keywords, _config.BackupDirectory);
        ShowNotice(_config.Keywords.Count == 0 ? "Keywords cleared." : $"Saved {_config.Keywords.Count} keyword{(_config.Keywords.Count == 1 ? "" : "s")}.");
        RenderChat(_service.GetSnapshot(), force: true);
    }

    private void SaveNow()
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

    private void OpenLogFolder()
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

    private void ChooseLogFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder that already contains your GTAW logs. New files are saved under Year\\MON inside it.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_config.BackupDirectory)
                ? _config.BackupDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        _config.BackupDirectory = dialog.SelectedPath;
        _config.Save();
        _settings.Bind(_config.Keywords, _config.BackupDirectory);
        ShowNotice("Log folder updated.");
    }

    private void SaveNotificationSettings()
    {
        _config.NotificationsEnabled = _settings.NotificationsEnabled;
        _config.NotificationCaseSensitive = _settings.CaseSensitive;
        _config.NotificationSeconds = _settings.NotificationSeconds;
        _config.NotificationAnimation = _settings.NotificationAnimation;
        _config.Keywords = AppConfig.NormalizeKeywords(_config.Keywords, _config.NotificationCaseSensitive);
        _config.Save();
        _settings.Bind(_config.Keywords, _config.BackupDirectory);
        RenderChat(_service.GetSnapshot(), force: true);
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
        KeywordToast.ShowMatches(hits, _config.ResolvedNotificationSeconds, _config.ResolvedNotificationAnimation);
    }

    private void OnLogSaved(string path, bool gameClosed)
    {
        _lastLogPath = path;
        if (!gameClosed)
            return;

        ShowNotice("Chat log saved. " + path);
        if (WindowState == FormWindowState.Minimized)
            FlashWindow(Handle, true);
    }

    private void SetStatus(string status)
    {
        if (string.Equals(status, _status, StringComparison.Ordinal))
            return;

        _status = status;
        _statusLabel.Text = status;
        LayoutStatus();
        _header.Invalidate(true);
    }

    private void LayoutHeader()
    {
        var y = (_header.Height - _close.Height) / 2;
        var x = _header.Width - 14 - _close.Width;
        _close.Location = new Point(x, y);
        x -= 6 + _trayButton.Width;
        _trayButton.Location = new Point(x, y);
        x -= 6 + _minimize.Width;
        _minimize.Location = new Point(x, y);
        LayoutStatus();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        _tray.Visible = true;
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _tray.Visible = false;
    }

    private void LayoutStatus()
    {
        var textWidth = TextRenderer.MeasureText(_status, HudTheme.Body).Width;
        var width = Math.Min(360, textWidth + 36);
        var right = _minimize.Left - 14;
        var top = (_header.Height - 28) / 2;
        _statusPill.SetBounds(Math.Max(280, right - width), top, width, 28);
        _statusLabel.BackColor = Color.FromArgb(32, 37, 48);
        _statusLabel.SetBounds(20, 3, Math.Max(0, width - 36), 22);
    }

    private void ShowNotice(string message)
    {
        _notice.Text = message;
        _notice.Visible = true;
        _notice.Height = 32;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void RenderChat(string snapshot, bool force = false)
    {
        if (!force && snapshot == _rendered)
            return;

        var follow = _rendered.Length == 0 || IsNearBottom();
        _rendered = snapshot;
        var hasText = snapshot.Length > 0;
        _empty.Visible = !hasText;
        _chat.Visible = hasText;
        if (!hasText)
        {
            _chat.Clear();
            return;
        }

        SendMessage(_chat.Handle, WM_SETREDRAW, 0, 0);
        _chat.Clear();
        foreach (var line in snapshot.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
                continue;

            _chat.SelectionStart = _chat.TextLength;
            _chat.SelectionColor = LineColor(line);
            _chat.AppendText(line + Environment.NewLine);
        }

        if (follow)
        {
            _chat.SelectionStart = _chat.TextLength;
            _chat.ScrollToCaret();
        }

        SendMessage(_chat.Handle, WM_SETREDRAW, 1, 0);
        _chat.Invalidate();
    }

    private Color LineColor(string line)
    {
        if (line.StartsWith("[DATE:", StringComparison.Ordinal))
            return HudTheme.Frame;

        return ChatLog.FindKeywords(line, _config.Keywords, _config.NotificationCaseSensitive).Count > 0 ? HudTheme.Hit : HudTheme.Line;
    }

    private bool IsNearBottom()
    {
        if (_chat.TextLength == 0)
            return true;

        var position = _chat.GetPositionFromCharIndex(Math.Max(0, _chat.TextLength - 1));
        return position.Y < _chat.ClientSize.Height + 24;
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(HudTheme.Header);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var gold = new SolidBrush(HudTheme.Frame);
        var markY = (_header.Height - 32) / 2;
        graphics.FillEllipse(gold, 18, markY, 32, 32);
        using var mark = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, "G", mark, new Rectangle(18, markY, 32, 32), Color.FromArgb(28, 20, 8), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var titleRight = Math.Max(220, _statusPill.Left - 12);
        TextRenderer.DrawText(
            graphics,
            "GTA: World Chat Parser",
            HudTheme.Title,
            new Rectangle(60, 0, titleRight - 60, _header.Height),
            HudTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void PaintStatus(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var color = StatusColor(_status);
        var fill = Color.FromArgb(32, 37, 48);
        HudShapes.FillRounded(graphics, _statusPill.ClientRectangle, 14, fill, HudTheme.Header);
        using var dot = new SolidBrush(color);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.FillEllipse(dot, 11, 10, 8, 8);
    }

    private static Color StatusColor(string status)
    {
        if (status.Contains("Capturing", StringComparison.OrdinalIgnoreCase))
            return HudTheme.Ok;
        if (status.Contains("Looking", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Waiting for GTAW", StringComparison.OrdinalIgnoreCase)
            || status.Contains("saving", StringComparison.OrdinalIgnoreCase))
            return HudTheme.Wait;
        return HudTheme.Idle;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is already closing.
        }
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(HudTheme.Frame);
            graphics.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString("G", font, Brushes.Black, 8, 6);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hWnd, bool invert);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr update, IntPtr region, uint flags);
}

internal sealed class HudFrame : Panel
{
    public HudFrame()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = HudTheme.Transcript;
        Padding = new Padding(18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(HudTheme.Transcript);
        HudShapes.DrawBrackets(graphics, ClientRectangle, Color.FromArgb(210, HudTheme.Frame));
    }
}

internal sealed class HudMenuRenderer : ToolStripProfessionalRenderer
{
    public HudMenuRenderer()
        : base(new HudMenuColors())
    {
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = HudTheme.Text;
        base.OnRenderItemText(e);
    }
}

internal sealed class HudMenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => HudTheme.ButtonHover;
    public override Color MenuItemSelectedGradientBegin => HudTheme.ButtonHover;
    public override Color MenuItemSelectedGradientEnd => HudTheme.ButtonHover;
    public override Color MenuItemPressedGradientBegin => HudTheme.Button;
    public override Color MenuItemPressedGradientEnd => HudTheme.Button;
    public override Color MenuItemBorder => Color.FromArgb(80, HudTheme.Frame);
    public override Color MenuBorder => Color.FromArgb(80, HudTheme.Frame);
    public override Color ToolStripDropDownBackground => HudTheme.Card;
    public override Color ImageMarginGradientBegin => HudTheme.Card;
    public override Color ImageMarginGradientMiddle => HudTheme.Card;
    public override Color ImageMarginGradientEnd => HudTheme.Card;
    public override Color SeparatorDark => Color.FromArgb(48, HudTheme.Frame);
    public override Color SeparatorLight => Color.FromArgb(48, HudTheme.Frame);
}
