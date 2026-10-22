using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GTAWChatParser;

/// <summary>
/// Turns captured HUD lines into the archive text used by GTAW Log Parser.
/// </summary>
public static class ChatLog
{
    private static readonly Regex TimestampPrefix = new(
        @"^\[(?<time>\d{1,2}:\d{2}:\d{2})\]\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HeaderPattern = new(
        @"^\[DATE: (?<date>\d{2}/[A-Z]{3}/\d{4}) \| TIME: (?<time>\d{2}:\d{2}:\d{2})\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string FormatLine(string rawLine, DateTime capturedAt)
    {
        var line = Collapse(rawLine);
        if (line.Length == 0)
            return string.Empty;

        if (TimestampPrefix.IsMatch(line))
            return line;

        return $"[{capturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {line}";
    }

    public static DateTime ResolveTimestamp(string rawLine, DateTime capturedAt)
    {
        var match = TimestampPrefix.Match(Collapse(rawLine));
        if (!match.Success)
            return capturedAt;

        if (!DateTime.TryParseExact(match.Groups["time"].Value, ["H:mm:ss", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return capturedAt;

        return capturedAt.Date.Add(parsed.TimeOfDay);
    }

    public static string Render(DateTime startedAt, IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder();
        builder.Append(CreateHeader(startedAt));
        builder.Append("\r\n");
        foreach (var line in lines)
        {
            builder.Append(line);
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    public static string CreateHeader(DateTime startedAt)
    {
        var date = startedAt.ToString("dd/MMM/yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        var time = startedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"[DATE: {date} | TIME: {time}]";
    }

    public static bool TryParseHeader(string line, out DateTime startedAt)
    {
        startedAt = default;
        var match = HeaderPattern.Match(line.Trim());
        if (!match.Success)
            return false;

        if (!DateTime.TryParseExact(match.Groups["date"].Value, "dd/MMM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        if (!DateTime.TryParseExact(match.Groups["time"].Value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;

        startedAt = date.Date.Add(time.TimeOfDay);
        return true;
    }

    /// <summary>
    /// Visible chat is a sliding window: the oldest lines fall off the top and new lines are added at the bottom.
    /// Returns how many leading lines of the new snapshot are already present at the end of the previous snapshot.
    /// </summary>
    public static int FindOverlap(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var max = Math.Min(previous.Count, current.Count);
        for (var length = max; length > 0; length--)
        {
            var matches = true;
            for (var i = 0; i < length; i++)
            {
                if (!string.Equals(previous[previous.Count - length + i], current[i], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return length;
        }

        return 0;
    }

    public static IReadOnlyList<string> FindKeywords(string formattedLine, IEnumerable<string> keywords)
    {
        var body = TimestampPrefix.Replace(formattedLine, string.Empty);
        var found = new List<string>();
        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.Length == 0)
                continue;

            if (body.Contains(trimmed, StringComparison.OrdinalIgnoreCase) &&
                found.All(existing => !string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add(trimmed);
            }
        }

        return found;
    }

    private static string Collapse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var flattened = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var builder = new StringBuilder(flattened.Length);
        var pendingSpace = false;
        foreach (var character in flattened)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
