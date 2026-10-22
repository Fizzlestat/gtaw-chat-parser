namespace GTAWChatParser;

internal static class HudTheme
{
    public static readonly Color Frame = Color.FromArgb(232, 168, 56);
    public static readonly Color Background = Color.FromArgb(12, 14, 18);
    public static readonly Color Header = Color.FromArgb(18, 21, 28);
    public static readonly Color Side = Color.FromArgb(16, 18, 24);
    public static readonly Color Card = Color.FromArgb(26, 30, 38);
    public static readonly Color Transcript = Color.FromArgb(14, 16, 22);
    public static readonly Color Text = Color.FromArgb(236, 232, 223);
    public static readonly Color Muted = Color.FromArgb(148, 154, 166);
    public static readonly Color Line = Color.FromArgb(214, 210, 200);
    public static readonly Color Hit = Color.FromArgb(255, 214, 140);
    public static readonly Color Button = Color.FromArgb(38, 43, 54);
    public static readonly Color ButtonHover = Color.FromArgb(52, 58, 72);
    public static readonly Color Ok = Color.FromArgb(126, 196, 140);
    public static readonly Color Wait = Color.FromArgb(232, 168, 56);
    public static readonly Color Idle = Color.FromArgb(120, 128, 142);

    public static readonly Font Title = new("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Subtitle = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Section = new("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Body = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font ButtonFont = new("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font TranscriptFont = new("Consolas", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
}
