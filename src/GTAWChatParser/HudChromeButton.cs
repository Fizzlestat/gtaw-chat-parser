using System.Drawing.Drawing2D;

namespace GTAWChatParser;

internal enum ChromeGlyph
{
    Minimize,
    Tray,
    Close
}

internal sealed class HudChromeButton : Control
{
    private readonly HudEase _ease;

    public HudChromeButton(ChromeGlyph glyph)
    {
        Glyph = glyph;
        Size = new Size(32, 32);
        Cursor = Cursors.Hand;
        BackColor = HudTheme.Header;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        _ease = new HudEase(this);
        _ease.Snap("hot", 0f);
        _ease.Snap("press", 0f);
    }

    public ChromeGlyph Glyph { get; }

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
        using var brush = new SolidBrush(Parent?.BackColor ?? HudTheme.Header);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var hot = Math.Clamp(_ease["hot"] + _ease["press"] * 0.35f, 0f, 1f);
        var backdrop = Parent?.BackColor ?? HudTheme.Header;
        if (hot > 0.01f)
        {
            var fill = Glyph == ChromeGlyph.Close
                ? Color.FromArgb((int)(70 * hot), HudTheme.Frame)
                : HudTheme.Lerp(HudTheme.Header, HudTheme.ButtonHover, hot);
            HudShapes.FillRounded(graphics, ClientRectangle, 8, fill, backdrop);
        }
        else
        {
            using var wash = new SolidBrush(backdrop);
            graphics.FillRectangle(wash, ClientRectangle);
        }

        var color = Glyph == ChromeGlyph.Close
            ? HudTheme.Lerp(HudTheme.Text, HudTheme.Frame, hot)
            : HudTheme.Lerp(HudTheme.Text, Color.FromArgb(255, 248, 236), hot);
        using var pen = new Pen(color, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var center = new PointF(Width / 2f, Height / 2f);
        switch (Glyph)
        {
            case ChromeGlyph.Minimize:
                graphics.DrawLine(pen, center.X - 5.5f, center.Y, center.X + 5.5f, center.Y);
                break;
            case ChromeGlyph.Tray:
                DrawTray(graphics, pen, center);
                break;
            default:
                graphics.DrawLine(pen, center.X - 4.5f, center.Y - 4.5f, center.X + 4.5f, center.Y + 4.5f);
                graphics.DrawLine(pen, center.X + 4.5f, center.Y - 4.5f, center.X - 4.5f, center.Y + 4.5f);
                break;
        }
    }

    private static void DrawTray(Graphics graphics, Pen pen, PointF center)
    {
        var left = center.X - 6f;
        var right = center.X + 6f;
        var bowlTop = center.Y + 1f;
        var bowlBottom = center.Y + 5.5f;
        graphics.DrawLines(pen, new[]
        {
            new PointF(left, bowlTop),
            new PointF(left, bowlBottom),
            new PointF(right, bowlBottom),
            new PointF(right, bowlTop)
        });
        graphics.DrawLine(pen, center.X, center.Y - 6f, center.X, center.Y);
        graphics.DrawLines(pen, new[]
        {
            new PointF(center.X - 3.2f, center.Y - 2.6f),
            new PointF(center.X, center.Y + 0.4f),
            new PointF(center.X + 3.2f, center.Y - 2.6f)
        });
    }
}
