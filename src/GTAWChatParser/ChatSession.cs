namespace GTAWChatParser;

public sealed class ChatSession
{
    private ChatSession(DateTime startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTime StartedAt { get; }

    public List<string> Lines { get; } = [];

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
                session.Lines.Add(lines[i]);
        }

        return session;
    }

    public void Add(IEnumerable<string> formattedLines)
    {
        var added = false;
        foreach (var line in formattedLines)
        {
            if (line.Length == 0)
                continue;

            Lines.Add(line);
            added = true;
        }

        if (added)
            IsDirty = true;
    }

    public void MarkClean() => IsDirty = false;

    public string ToFileText() => ChatLog.Render(StartedAt, Lines);
}
