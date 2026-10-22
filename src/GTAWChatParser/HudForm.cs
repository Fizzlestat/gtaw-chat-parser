using System.Runtime.InteropServices;

namespace GTAWChatParser;

internal sealed class HudForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    private const int WM_SETREDRAW = 0x0B;
    private const int SideWidth = 372;

    private readonly AppConfig _config;
    private readonly CaptureService _service;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _noticeTimer;

    private readonly Panel _header;
    private readonly Label _notice;
    private readonly Panel _statusPill;
    private readonly Label _statusLabel;
    private RichTextBox _chat = null!;
    private Label _empty = null!;
    private Panel _transcript = null!;
    private Panel _help = null!;
    private TextBox _keywords = null!;
    private TextBox _port = null!;
    private Label _folder = null!;

    private Task? _captureTask;
    private string _status = "Starting";
    private string _rendered = "\u0001";
    private string? _lastLogPath;
    private bool _started;
    private HudButton? _helpButton;
    private bool _closing;

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
        Text = "GTAW Chat Parser";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(1020, 720);
        Size = new Size(1180, 760);
        BackColor = HudTheme.Frame;
        Padding = new Padding(1);
        DoubleBuffered = true;
        Icon = CreateIcon();

        var shell = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Background };
        Controls.Add(shell);

        _header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = HudTheme.Header };
        _header.MouseDown += BeginDrag;
        _header.MouseDoubleClick += (_, _) => ToggleMaximize();
        _header.Paint += PaintHeader;

        var minimize = new HudButton { Text = "—", Width = 36, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var close = new HudButton { Text = "×", Width = 36, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close.Click += (_, _) => Close();
        _header.Controls.Add(minimize);
        _header.Controls.Add(close);
        _header.Resize += (_, _) =>
        {
            close.Location = new Point(_header.Width - 48, 20);
            minimize.Location = new Point(_header.Width - 90, 20);
            LayoutStatus();
        };

        _statusPill = new Panel { Height = 28, BackColor = Color.Transparent };
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
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 12, 0),
            ForeColor = HudTheme.Background,
            BackColor = HudTheme.Frame,
            Font = HudTheme.Body
        };

        var side = BuildSide();
        var chatColumn = BuildChatColumn();

        shell.Controls.Add(chatColumn);
        shell.Controls.Add(side);
        shell.Controls.Add(_notice);
        shell.Controls.Add(_header);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _refreshTimer.Tick += (_, _) => RenderChat(_service.GetSnapshot());
        _noticeTimer = new System.Windows.Forms.Timer { Interval = 4200 };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            _notice.Height = 0;
            _notice.Text = string.Empty;
        };

        _service.StatusChanged += status => Post(() => SetStatus(status));
        _service.LinesAppended += lines => Post(() => OnLines(lines));
        _service.LogSaved += (path, gameClosed) => Post(() => OnLogSaved(path, gameClosed));

        BindFields();
        SetStatus("Starting");
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
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closing)
        {
            base.OnFormClosing(e);
            return;
        }

        _closing = true;
        _refreshTimer.Stop();
        _noticeTimer.Stop();
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

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            const int WS_EX_COMPOSITED = 0x02000000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CS_DROPSHADOW;
            parameters.ExStyle |= WS_EX_COMPOSITED;
            return parameters;
        }
    }

    private Panel BuildSide()
    {
        var side = new Panel
        {
            Dock = DockStyle.Right,
            Width = SideWidth,
            BackColor = HudTheme.Side,
            Padding = new Padding(14, 14, 14, 12)
        };

        var keywords = BuildKeywordCard();
        var log = BuildLogCard();
        var connection = BuildConnectionCard();
        var footer = BuildFooter();

        // Docked last-to-first: footer sits on the bottom, keywords take the rest.
        side.Controls.Add(keywords);
        side.Controls.Add(Gap());
        side.Controls.Add(log);
        side.Controls.Add(Gap());
        side.Controls.Add(connection);
        side.Controls.Add(Gap());
        side.Controls.Add(footer);
        return side;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 98,
            BackColor = HudTheme.Side,
            Padding = new Padding(0, 10, 0, 8)
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(48, HudTheme.Frame));
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        _helpButton = CreateButton("How this works", false, (_, _) => ToggleHelp(), 0, 0, 10);
        _helpButton.Dock = DockStyle.Top;
        _helpButton.Height = 34;
        var exit = CreateButton("Exit", false, (_, _) => Close(), 0, 0, 10);
        exit.Dock = DockStyle.Bottom;
        exit.Height = 34;
        var gap = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = HudTheme.Side };

        footer.Controls.Add(gap);
        footer.Controls.Add(exit);
        footer.Controls.Add(_helpButton);
        return footer;
    }

    private Panel BuildKeywordCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = HudTheme.Card,
            Padding = new Padding(14, 12, 14, 12)
        };

        var title = SectionLabel("KEYWORDS");
        title.Dock = DockStyle.Top;
        title.Height = 22;
        var hint = HintLabel("One keyword or phrase per line. Matching ignores letter case.");
        hint.Dock = DockStyle.Top;
        hint.Height = 34;
        var belowHint = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = HudTheme.Card };

        var save = CreateButton("Save keywords", true, (_, _) => SaveKeywords(), 0, 0, 10);
        save.Dock = DockStyle.Bottom;
        save.Height = 34;
        var aboveSave = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = HudTheme.Card };

        _keywords = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Font = HudTheme.Body,
            BackColor = HudTheme.Transcript,
            ForeColor = HudTheme.Text,
            Dock = DockStyle.Fill
        };

        card.Controls.Add(_keywords);
        card.Controls.Add(aboveSave);
        card.Controls.Add(save);
        card.Controls.Add(belowHint);
        card.Controls.Add(hint);
        card.Controls.Add(title);
        return card;
    }

    private Panel BuildLogCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 182,
            BackColor = HudTheme.Card,
            Padding = new Padding(14, 12, 14, 12)
        };

        var title = SectionLabel("LOG FOLDER");
        title.Dock = DockStyle.Top;
        title.Height = 22;
        _folder = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            ForeColor = HudTheme.Text,
            Font = HudTheme.Body
        };
        var hint = HintLabel("Saved as Year\\MON\\dd.MON.yyyy-HH.mm.ss.txt");
        hint.Dock = DockStyle.Top;
        hint.Height = 20;

        var save = CreateButton("Save log now", true, (_, _) => SaveNow(), 0, 0, 10);
        save.Dock = DockStyle.Bottom;
        save.Height = 34;
        var aboveSave = new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = HudTheme.Card };
        var row = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = HudTheme.Card };
        PlaceHalves(
            row,
            CreateButton("Change folder", false, (_, _) => ChooseLogFolder(), 0, 0, 10),
            CreateButton("Open folder", false, (_, _) => OpenLogFolder(), 0, 0, 10));

        card.Controls.Add(row);
        card.Controls.Add(aboveSave);
        card.Controls.Add(save);
        card.Controls.Add(hint);
        card.Controls.Add(_folder);
        card.Controls.Add(title);
        return card;
    }

    private Panel BuildConnectionCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            BackColor = HudTheme.Card,
            Padding = new Padding(14, 12, 14, 12)
        };

        var title = SectionLabel("CONNECTION");
        title.Dock = DockStyle.Top;
        title.Height = 22;
        var hint = HintLabel("FiveM inspector port");
        hint.Dock = DockStyle.Top;
        hint.Height = 18;
        var underHint = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = HudTheme.Card };

        var portRow = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = HudTheme.Card };
        _port = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = HudTheme.Body,
            BackColor = HudTheme.Transcript,
            ForeColor = HudTheme.Text
        };
        var apply = CreateButton("Apply port", false, (_, _) => ApplyPort(), 0, 0, 10);
        portRow.Resize += (_, _) =>
        {
            _port.SetBounds(0, 4, 96, 26);
            apply.SetBounds(104, 0, Math.Max(40, portRow.ClientSize.Width - 104), 34);
        };
        portRow.Controls.Add(_port);
        portRow.Controls.Add(apply);

        var reloadRow = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = HudTheme.Card };
        PlaceHalves(
            reloadRow,
            CreateButton("Reload settings", false, (_, _) => ReloadSettings(), 0, 0, 10),
            CreateButton("Open settings file", false, (_, _) => OpenSettingsFile(), 0, 0, 10));

        card.Controls.Add(reloadRow);
        card.Controls.Add(portRow);
        card.Controls.Add(underHint);
        card.Controls.Add(hint);
        card.Controls.Add(title);
        return card;
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

        _transcript = new Panel { Dock = DockStyle.Fill, BackColor = HudTheme.Transcript };
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
        _transcript.Controls.Add(_chat);
        _transcript.Controls.Add(_empty);

        _help = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AutoScroll = true,
            BackColor = HudTheme.Transcript,
            Padding = new Padding(8)
        };
        var helpText = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = HudTheme.Line,
            Font = HudTheme.Body,
            BackColor = HudTheme.Transcript,
            Text = HelpCopy()
        };
        _help.Controls.Add(helpText);

        frame.Controls.Add(_transcript);
        frame.Controls.Add(_help);
        frame.Controls.Add(caption);
        column.Controls.Add(frame);
        return column;
    }

    private void BindFields()
    {
        _keywords.Text = string.Join(Environment.NewLine, _config.Keywords);
        _port.Text = _config.DevtoolsPort.ToString();
        _folder.Text = _config.BackupDirectory;
    }

    private void SaveKeywords()
    {
        _config.Keywords = AppConfig.NormalizeKeywords(_keywords.Text.Split(["\r\n", "\n"], StringSplitOptions.None));
        _config.Save();
        _keywords.Text = string.Join(Environment.NewLine, _config.Keywords);
        ShowNotice(_config.Keywords.Count == 0 ? "Keywords cleared." : $"Saved {_config.Keywords.Count} keyword{(_config.Keywords.Count == 1 ? "" : "s")}.");
        RenderChat(_service.GetSnapshot(), force: true);
    }

    private void ApplyPort()
    {
        if (!int.TryParse(_port.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            ShowNotice("Enter a port from 1 to 65535.");
            _port.Text = _config.DevtoolsPort.ToString();
            return;
        }

        _config.DevtoolsPort = port;
        _config.Save();
        ShowNotice($"Inspector port set to {port}.");
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
        _folder.Text = _config.BackupDirectory;
        ShowNotice("Log folder updated.");
    }

    private void ReloadSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigPath))
                _config.Save();

            var fresh = AppConfig.Load();
            _config.ReplaceWith(fresh);
            BindFields();
            ShowNotice("Settings reloaded.");
            RenderChat(_service.GetSnapshot(), force: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Settings reload failed.", exception);
            ShowNotice("Settings could not be reloaded. " + exception.Message);
        }
    }

    private void OpenSettingsFile()
    {
        try
        {
            if (!File.Exists(_config.FilePath))
                _config.Save();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _config.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowNotice("Could not open the settings file. " + exception.Message);
        }
    }

    private void ToggleHelp()
    {
        var showHelp = !_help.Visible;
        _help.Visible = showHelp;
        _transcript.Visible = !showHelp;
        if (_helpButton != null)
            _helpButton.Text = showHelp ? "Back to chat" : "How this works";
    }

    private void OnLines(IReadOnlyList<string> lines)
    {
        RenderChat(_service.GetSnapshot());
        if (GameProcess.IsFiveMFocused())
            return;

        var hits = new List<(string Keywords, string Line)>();
        foreach (var line in lines)
        {
            var matched = ChatLog.FindKeywords(line, _config.Keywords);
            if (matched.Count > 0)
                hits.Add((string.Join(", ", matched), line));
        }

        if (hits.Count == 0)
            return;

        AppLog.Info($"Keyword alert for {hits.Count} line(s).");
        KeywordToast.ShowMatches(hits);
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
        _status = status;
        _statusLabel.Text = status;
        LayoutStatus();
        _statusPill.Invalidate();
    }

    private void LayoutStatus()
    {
        var textWidth = TextRenderer.MeasureText(_status, HudTheme.Body).Width;
        var width = Math.Min(420, textWidth + 36);
        _statusPill.SetBounds(_header.Width - 108 - width, 22, width, 28);
        _statusLabel.SetBounds(22, 0, Math.Max(0, width - 30), 28);
        using var path = Rounded(new Rectangle(0, 0, Math.Max(1, width), 28), 14);
        _statusPill.Region = new Region(path);
    }

    private void ShowNotice(string message)
    {
        _notice.Text = message;
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

        return ChatLog.FindKeywords(line, _config.Keywords).Count > 0 ? HudTheme.Hit : HudTheme.Line;
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
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var gold = new SolidBrush(HudTheme.Frame);
        graphics.FillEllipse(gold, 20, 18, 36, 36);
        using var mark = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(graphics, "G", mark, new Rectangle(20, 18, 36, 36), Color.FromArgb(28, 20, 8), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(graphics, "GTAW CHAT", HudTheme.Title, new Point(68, 12), HudTheme.Text);
        TextRenderer.DrawText(graphics, "Saves every 5 minutes and when the game closes", HudTheme.Subtitle, new Point(70, 42), HudTheme.Muted);
    }

    private void PaintStatus(object? sender, PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var color = StatusColor(_status);
        using var brush = new SolidBrush(Color.FromArgb(36, color.R, color.G, color.B));
        using var path = Rounded(_statusPill.ClientRectangle, 14);
        graphics.FillPath(brush, path);
        using var dot = new SolidBrush(color);
        graphics.FillEllipse(dot, 8, 10, 8, 8);
    }

    private static Color StatusColor(string status)
    {
        if (status.Contains("Capturing", StringComparison.OrdinalIgnoreCase))
            return HudTheme.Ok;
        if (status.Contains("Waiting for GTAW", StringComparison.OrdinalIgnoreCase) || status.Contains("saving", StringComparison.OrdinalIgnoreCase))
            return HudTheme.Wait;
        return HudTheme.Idle;
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter - 1;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter - 1;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
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

    private static string HelpCopy() =>
        """
        GTAW Chat Parser watches GTA World chat while FiveM is running. Open it before or after you are already in game. The chat on screen is read immediately, and new lines are added after that.

        Logs use the same folders and file names as GTAW Log Parser:

            LogFolder\2026\OCT\22.OCT.2026-14.05.09.txt

        Each file starts with a header:

            [DATE: 22/OCT/2026 | TIME: 14:05:09]

        Point the log folder at the directory that already contains your year folders. New sessions are added beside the logs you already have.

        The current session is written every 5 minutes, and written again when the game closes.

        Keywords ignore letter case and match anywhere in the message. When a keyword appears and FiveM is in the background, a notification is shown. Lines that match a keyword are highlighted in the transcript.

        Chat is read from FiveM on this PC at 127.0.0.1. The default inspector port is 13172. If the status stays on "Waiting for GTAW chat" after you have spawned, change the port and apply it.
        """;

    private static Panel Gap() => new()
    {
        Dock = DockStyle.Bottom,
        Height = 8,
        BackColor = HudTheme.Side
    };

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        Font = HudTheme.Section,
        ForeColor = HudTheme.Frame,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label HintLabel(string text) => new()
    {
        Text = text,
        Font = HudTheme.Subtitle,
        ForeColor = HudTheme.Muted
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

    private static HudButton CreateButton(string text, bool primary, EventHandler click, int x, int y, int width)
    {
        var button = new HudButton
        {
            Text = text,
            Primary = primary,
            Bounds = new Rectangle(x, y, width, 34)
        };
        button.Click += click;
        return button;
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
}

internal sealed class HudFrame : Panel
{
    public HudFrame()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = HudTheme.Transcript;
        Padding = new Padding(12, 8, 12, 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(HudTheme.Transcript);
        using var pen = new Pen(Color.FromArgb(120, HudTheme.Frame), 1.6f);
        const int mark = 16;
        var right = Width - 2;
        var bottom = Height - 2;
        graphics.DrawLines(pen, new[] { new Point(1, mark), new Point(1, 1), new Point(mark, 1) });
        graphics.DrawLines(pen, new[] { new Point(right - mark, 1), new Point(right, 1), new Point(right, mark) });
        graphics.DrawLines(pen, new[] { new Point(1, bottom - mark), new Point(1, bottom), new Point(mark, bottom) });
        graphics.DrawLines(pen, new[] { new Point(right - mark, bottom), new Point(right, bottom), new Point(right, bottom - mark) });
    }
}
