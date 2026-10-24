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

    public static Color Lerp(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            Blend(from.A, to.A, amount),
            Blend(from.R, to.R, amount),
            Blend(from.G, to.G, amount),
            Blend(from.B, to.B, amount));
    }

    private static int Blend(int from, int to, float amount) =>
        (int)Math.Round(from + (to - from) * amount);
}

internal sealed class HudEase : IDisposable
{
    private readonly Control _owner;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<string, float> _values = [];
    private readonly Dictionary<string, float> _targets = [];

    public HudEase(Control owner)
    {
        _owner = owner;
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (_, _) => Step();
    }

    public float this[string name] => _values.TryGetValue(name, out var value) ? value : 0f;

    public void Snap(string name, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        _values[name] = value;
        _targets[name] = value;
    }

    public void Set(string name, float target)
    {
        target = Math.Clamp(target, 0f, 1f);
        _targets[name] = target;
        if (!_values.ContainsKey(name))
            _values[name] = target;
        if (!_timer.Enabled && Math.Abs(_values[name] - target) > 0.01f)
            _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void Step()
    {
        if (_owner.IsDisposed)
        {
            _timer.Stop();
            return;
        }

        var settled = true;
        foreach (var name in _targets.Keys)
        {
            var current = _values[name];
            var target = _targets[name];
            var next = current + (target - current) * 0.38f;
            if (Math.Abs(target - next) < 0.02f)
                next = target;
            else
                settled = false;
            _values[name] = next;
        }

        _owner.Invalidate();
        if (settled)
            _timer.Stop();
    }
}
