namespace GTAWChatParser;

public sealed class ChatSession
{
    private ChatSession(DateTime startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTime StartedAt { get; }

    public List<string> Lines { get; } = [];

    public List<string> DisplayLines { get; } = [];

    public bool IsDirty { get; private set; }

    public static ChatSession Start(string firstRawLine, DateTime capturedAt)
    {
        return new ChatSession(ChatLog.ResolveTimestamp(firstRawLine, capturedAt))
        {
            IsDirty = true
        };
    }

    public static ChatSession? TryLoad(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (!ChatLog.TryParseHeader(lines[0], out var startedAt))
            return null;

        var session = new ChatSession(startedAt);
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length > 0)
            {
                session.Lines.Add(lines[i]);
                session.DisplayLines.Add(lines[i]);
            }
        }

        return session;
    }

    public void Add(IReadOnlyList<string> formattedLines, IReadOnlyList<string> coloredLines)
    {
        var added = false;
        for (var i = 0; i < formattedLines.Count; i++)
        {
            var line = formattedLines[i];
            if (line.Length == 0)
                continue;

            var colored = i < coloredLines.Count ? coloredLines[i] : line;
            Lines.Add(line);
            DisplayLines.Add(colored.Length == 0 ? line : colored);
            added = true;
        }

        if (added)
            IsDirty = true;
    }

    public void PaintColors(IReadOnlyList<string> coloredTail)
    {
        var count = Math.Min(coloredTail.Count, Lines.Count);
        var start = Lines.Count - count;
        for (var i = 0; i < count; i++)
        {
            var colored = coloredTail[i];
            var index = start + i;
            if (index >= DisplayLines.Count || string.IsNullOrEmpty(colored) || !colored.Contains("^#", StringComparison.Ordinal))
                continue;

            DisplayLines[index] = colored;
        }
    }

    public void MarkClean() => IsDirty = false;

    public string ToFileText() => ChatLog.Render(StartedAt, Lines);
}
