namespace GTAWChatParser;

internal sealed class HudButton : Button
{
    private bool _hover;

    public HudButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 36;
        Cursor = Cursors.Hand;
        Font = HudTheme.ButtonFont;
        ForeColor = HudTheme.Text;
        BackColor = HudTheme.Button;
        TabStop = true;
    }

    public bool Primary { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var fill = Primary
            ? (_hover ? Color.FromArgb(242, 186, 84) : HudTheme.Frame)
            : (_hover ? HudTheme.ButtonHover : HudTheme.Button);
        var text = Primary ? Color.FromArgb(28, 20, 8) : HudTheme.Text;
        if (!Enabled)
        {
            fill = Color.FromArgb(32, 34, 40);
            text = HudTheme.Muted;
        }

        using var brush = new SolidBrush(fill);
        graphics.FillRectangle(brush, ClientRectangle);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
