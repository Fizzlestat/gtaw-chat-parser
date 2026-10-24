using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GTAWChatParser;

public readonly record struct ColorSpan(string Text, string? Hex);

public static class ChatLog
{
    private static readonly Regex ColorCode = new(
        @"\^#[0-9A-Fa-f]{6}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrivateMessage = new(
        @"^\(\(\s*PM\s+(from|to)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LocalOoc = new(
        @"^\(\(\s*\(\d+\)\s+[^:\r\n]+:\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnnotatedEmote = new(
        @"^>\s*\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RoleplayAction = new(
        @"^\*\s+\S",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AdminChat = new(
        @"^\*\s+\(A\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex InCharacter = new(
        @"\bsays(?::|\s+\[low(?:er)?\])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

        if (TimestampPrefix.IsMatch(SkipLeadingColorCodes(line)))
            return line;

        return $"[{capturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {line}";
    }

    public static DateTime ResolveTimestamp(string rawLine, DateTime capturedAt)
    {
        var match = TimestampPrefix.Match(SkipLeadingColorCodes(Collapse(rawLine)));
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

    public static string StripColorCodes(string line) => ColorCode.Replace(line, string.Empty);

    private static string SkipLeadingColorCodes(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var match = ColorCode.Match(line, index);
            if (!match.Success || match.Index != index)
                break;

            index += match.Length;
        }

        return index == 0 ? line : line[index..];
    }

    public static IReadOnlyList<ColorSpan> SplitColorCodes(string line)
    {
        var spans = new List<ColorSpan>();
        var index = 0;
        string? hex = null;
        foreach (Match match in ColorCode.Matches(line))
        {
            if (match.Index > index)
                spans.Add(new ColorSpan(line[index..match.Index], hex));

            hex = match.Value[1..];
            index = match.Index + match.Length;
        }

        if (index < line.Length)
            spans.Add(new ColorSpan(line[index..], hex));

        return spans;
    }

    public static string ForTranscript(string plain, string? colored, IEnumerable<string> keywords, bool caseSensitive = false)
    {
        if (FindKeywords(plain, keywords, caseSensitive).Count > 0)
            return plain;

        if (!string.IsNullOrWhiteSpace(colored) && colored.Contains("^#", StringComparison.Ordinal) && SameMessage(plain, colored))
            return colored;

        return WithFallbackColor(plain);
    }

    private static bool SameMessage(string plain, string colored)
    {
        var stripped = StripColorCodes(colored).Trim();
        var plainTime = TimestampPrefix.Match(plain);
        var coloredTime = TimestampPrefix.Match(stripped);
        if (plainTime.Success && coloredTime.Success &&
            !string.Equals(plainTime.Groups["time"].Value, coloredTime.Groups["time"].Value, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(plain, stripped, StringComparison.Ordinal) ||
               (plainTime.Success && coloredTime.Success);
    }

    public static string WithFallbackColor(string line)
    {
        var match = TimestampPrefix.Match(line);
        var body = match.Success ? line[match.Length..] : line;
        var hex = FallbackHex(body);
        if (hex == null)
            return line;

        return "^#" + hex + line;
    }

    private static string? FallbackHex(string body)
    {
        if (PrivateMessage.IsMatch(body))
            return "F4E800";

        if (LocalOoc.IsMatch(body))
            return "939498";

        if (AnnotatedEmote.IsMatch(body))
            return "C2A2DA";

        if (RoleplayAction.IsMatch(body) && !AdminChat.IsMatch(body))
            return "BB9BD9";

        if (InCharacter.IsMatch(body))
            return "FFFFFF";

        return null;
    }

    public static IReadOnlyList<string> FindKeywords(string formattedLine, IEnumerable<string> keywords, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var body = TimestampPrefix.Replace(formattedLine, string.Empty);
        var found = new List<string>();
        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.Length == 0)
                continue;

            if (body.Contains(trimmed, comparison) &&
                found.All(existing => !string.Equals(existing, trimmed, comparison)))
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
