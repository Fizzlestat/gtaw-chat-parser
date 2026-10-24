namespace GTAWChatParser;

internal sealed class HudButton : Button
{
    private readonly HudEase _ease = null!;

    public HudButton()
    {
        _ease = new HudEase(this);
        _ease.Snap("hot", 0f);
        _ease.Snap("press", 0f);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        UseVisualStyleBackColor = false;
        Height = 36;
        Cursor = Cursors.Hand;
        Font = HudTheme.ButtonFont;
        ForeColor = HudTheme.Text;
        BackColor = HudTheme.Button;
        TabStop = true;
    }

    private bool _primary;

    public bool Primary
    {
        get => _primary;
        set
        {
            if (_primary == value)
                return;

            _primary = value;
            Invalidate();
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
        _ease.Set("press", 0f);
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
        var hot = Enabled ? _ease["hot"] : 0f;
        var press = Enabled ? _ease["press"] : 0f;
        var fill = Primary
            ? HudTheme.Lerp(HudTheme.Frame, Color.FromArgb(242, 186, 84), hot)
            : HudTheme.Lerp(HudTheme.Button, HudTheme.ButtonHover, hot);
        var text = Primary ? Color.FromArgb(28, 20, 8) : HudTheme.Lerp(HudTheme.Line, HudTheme.Text, 0.45f + hot * 0.55f);
        if (!Enabled)
        {
            fill = Color.FromArgb(32, 34, 40);
            text = HudTheme.Muted;
        }
        else if (press > 0f)
        {
            fill = HudTheme.Lerp(fill, Color.FromArgb(18, 16, 14), press * 0.28f);
        }

        var backdrop = Parent?.BackColor ?? HudTheme.Card;
        HudShapes.FillRounded(e.Graphics, ClientRectangle, 8, fill, backdrop);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal static class HudShapes
{
    public static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Min(Math.Max(1f, radius * 2f), Math.Min(bounds.Width, bounds.Height));
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics graphics, Rectangle area, float radius, Color fill, Color backdrop)
    {
        var smooth = graphics.SmoothingMode;
        var offset = graphics.PixelOffsetMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
        using (var wash = new SolidBrush(backdrop))
            graphics.FillRectangle(wash, area);

        var inner = new RectangleF(area.X + 0.5f, area.Y + 0.5f, Math.Max(1f, area.Width - 1f), Math.Max(1f, area.Height - 1f));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        using var path = Rounded(inner, radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = smooth;
        graphics.PixelOffsetMode = offset;
    }

    public static void FillFramed(Graphics graphics, Rectangle area, float radius, float thickness, Color frame, Color fill, Color backdrop)
    {
        FillRounded(graphics, area, radius, frame, backdrop);
        var inner = new RectangleF(
            area.X + thickness,
            area.Y + thickness,
            area.Width - thickness * 2f,
            area.Height - thickness * 2f);
        if (inner.Width < 4f || inner.Height < 4f)
            return;

        var smooth = graphics.SmoothingMode;
        var offset = graphics.PixelOffsetMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        using var path = Rounded(
            new RectangleF(inner.X + 0.5f, inner.Y + 0.5f, Math.Max(1f, inner.Width - 1f), Math.Max(1f, inner.Height - 1f)),
            Math.Max(1f, radius - thickness));
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        graphics.SmoothingMode = smooth;
        graphics.PixelOffsetMode = offset;
    }

    public static void StrokeRounded(Graphics graphics, Rectangle area, float radius, Color color, float width)
    {
        var inset = width / 2f + 0.5f;
        var inner = new RectangleF(area.X + inset, area.Y + inset, area.Width - inset * 2f, area.Height - inset * 2f);
        if (inner.Width < 2f || inner.Height < 2f)
            return;

        var smooth = graphics.SmoothingMode;
        var offset = graphics.PixelOffsetMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        using var path = Rounded(inner, Math.Max(1f, radius - inset));
        using var pen = new Pen(color, width)
        {
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.DrawPath(pen, path);
        graphics.SmoothingMode = smooth;
        graphics.PixelOffsetMode = offset;
    }

    public static void Hairline(Graphics graphics, int x, int y, int width, int height, Color color)
    {
        if (width < 1 || height < 1)
            return;

        var smooth = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, x, y, width, height);
        graphics.SmoothingMode = smooth;
    }

    public static void DrawBrackets(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        using var pen = new Pen(color, 1.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        const float inset = 4f;
        const float arm = 11f;
        const float radius = 5f;
        DrawCorner(graphics, pen, bounds.X + inset, bounds.Y + inset, 1, 1, arm, radius);
        DrawCorner(graphics, pen, bounds.Right - inset, bounds.Y + inset, -1, 1, arm, radius);
        DrawCorner(graphics, pen, bounds.X + inset, bounds.Bottom - inset, 1, -1, arm, radius);
        DrawCorner(graphics, pen, bounds.Right - inset, bounds.Bottom - inset, -1, -1, arm, radius);
    }

    private static void DrawCorner(Graphics graphics, Pen pen, float x, float y, int hx, int hy, float arm, float radius)
    {
        var arc = radius * 2f;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (hx > 0 && hy > 0)
        {
            path.AddLine(x, y + arm, x, y + radius);
            path.AddArc(x, y, arc, arc, 180, 90);
            path.AddLine(x + radius, y, x + arm, y);
        }
        else if (hx < 0 && hy > 0)
        {
            path.AddLine(x - arm, y, x - radius, y);
            path.AddArc(x - arc, y, arc, arc, 270, 90);
            path.AddLine(x, y + radius, x, y + arm);
        }
        else if (hx > 0 && hy < 0)
        {
            path.AddLine(x, y - arm, x, y - radius);
            path.AddArc(x, y - arc, arc, arc, 180, -90);
            path.AddLine(x + radius, y, x + arm, y);
        }
        else
        {
            path.AddLine(x - arm, y, x - radius, y);
            path.AddArc(x - arc, y - arc, arc, arc, 90, -90);
            path.AddLine(x, y - radius, x, y - arm);
        }

        graphics.DrawPath(pen, path);
    }
}

internal static class HudWindow
{
    public static void ApplyFrame(Form form)
    {
        form.BackColor = HudTheme.Frame;
        form.Padding = Padding.Empty;
        form.Controls.Add(Edge(DockStyle.Left));
        form.Controls.Add(Edge(DockStyle.Right));
        form.Controls.Add(Edge(DockStyle.Top));
        form.Controls.Add(Edge(DockStyle.Bottom));
    }

    private static Panel Edge(DockStyle dock) => new()
    {
        Dock = dock,
        Width = 1,
        Height = 1,
        BackColor = HudTheme.Frame
    };
}

internal sealed class HudWell : Panel
{
    private readonly HudEase _ease;

    public HudWell()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = HudTheme.Transcript;
        _ease = new HudEase(this);
        _ease.Snap("hot", 0f);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        var child = e.Control;
        if (child is null)
            return;
        child.MouseEnter += (_, _) => RefreshHot();
        child.MouseLeave += (_, _) => QueueRefresh();
        child.GotFocus += (_, _) => RefreshHot();
        child.LostFocus += (_, _) => RefreshHot();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        RefreshHot();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        QueueRefresh();
        base.OnMouseLeave(e);
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
        var hot = _ease["hot"];
        var border = HudTheme.Lerp(HudTheme.Lerp(BackColor, HudTheme.Frame, 0.72f), HudTheme.Frame, hot);
        var backdrop = Parent?.BackColor ?? HudTheme.Card;
        HudShapes.FillFramed(e.Graphics, ClientRectangle, 8, 2f, border, BackColor, backdrop);
    }

    private void QueueRefresh()
    {
        if (IsHandleCreated)
            BeginInvoke(RefreshHot);
        else
            RefreshHot();
    }

    private void RefreshHot()
    {
        if (IsDisposed)
            return;

        var inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
        _ease.Set("hot", inside || ContainsFocus ? 1f : 0f);
    }
}

internal sealed class HudToggle : Control
{
    private readonly HudEase _ease;
    private bool _checked;

    public HudToggle()
    {
        Size = new Size(78, 28);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _ease = new HudEase(this);
        _ease.Snap("on", 0f);
        _ease.Snap("hot", 0f);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            if (IsHandleCreated)
                _ease.Set("on", value ? 1f : 0f);
            else
                _ease.Snap("on", value ? 1f : 0f);
        }
    }

    public event EventHandler? CheckedChanged;

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

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
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
        var on = _ease["on"];
        var hot = _ease["hot"];
        var fill = HudTheme.Lerp(HudTheme.Button, HudTheme.Frame, on);
        fill = HudTheme.Lerp(fill, HudTheme.ButtonHover, hot * (1f - on) * 0.85f);
        fill = HudTheme.Lerp(fill, Color.FromArgb(242, 186, 84), hot * on * 0.7f);
        var backdrop = Parent?.BackColor ?? HudTheme.Card;
        HudShapes.FillRounded(e.Graphics, ClientRectangle, Height / 2f, fill, backdrop);
        var caption = on >= 0.5f ? "On" : "Off";
        var text = HudTheme.Lerp(HudTheme.Text, Color.FromArgb(28, 20, 8), on);
        TextRenderer.DrawText(
            e.Graphics,
            caption,
            HudTheme.ButtonFont,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
