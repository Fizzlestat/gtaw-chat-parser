using Xunit;

namespace GTAWChatParser.Tests;

public class ChatLogTests
{
    private static readonly DateTime CapturedAt = new(2026, 10, 22, 14, 5, 9);

    [Fact]
    public void Header_and_filename_match_the_gtaw_archive_layout()
    {
        var startedAt = new DateTime(2026, 10, 22, 14, 5, 9);
        var content = ChatLog.Render(startedAt, ["[14:05:09] John Doe says: Hello"]);

        Assert.Equal(
            "[DATE: 22/OCT/2026 | TIME: 14:05:09]\r\n[14:05:09] John Doe says: Hello\r\n",
            content);

        var root = Path.Combine(Path.GetTempPath(), "gtaw-parser-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = LogArchive.Save(root, startedAt, content);
            Assert.Equal(
                Path.Combine(root, "2026", "OCT", "22.OCT.2026-14.05.09.txt"),
                path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'[', bytes[0]);
            Assert.Equal(content, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hud_timestamp_is_kept_and_starts_the_session()
    {
        var line = ChatLog.FormatLine("[9:01:02] Jane whispers: hi", CapturedAt);
        var startedAt = ChatLog.ResolveTimestamp("[9:01:02] Jane whispers: hi", CapturedAt);

        Assert.Equal("[9:01:02] Jane whispers: hi", line);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 1, 2), startedAt);
        Assert.Equal("[DATE: 22/OCT/2026 | TIME: 09:01:02]", ChatLog.CreateHeader(startedAt));
    }

    [Fact]
    public void Color_code_does_not_add_a_second_timestamp()
    {
        var line = ChatLog.FormatLine("^#71CB71[18:12:36] Health restored!", CapturedAt);
        Assert.Equal("^#71CB71[18:12:36] Health restored!", line);
        Assert.Equal(new DateTime(2026, 10, 22, 18, 12, 36), ChatLog.ResolveTimestamp(line, CapturedAt));
    }

    [Fact]
    public void Missing_timestamp_uses_the_capture_clock()
    {
        var line = ChatLog.FormatLine("  John   Doe says: Hello \r\n there ", CapturedAt);
        Assert.Equal("[14:05:09] John Doe says: Hello there", line);
    }

    [Fact]
    public void Overlap_appends_only_lines_that_scrolled_in()
    {
        var previous = new[] { "a", "b", "c", "d" };
        var current = new[] { "c", "d", "e" };

        Assert.Equal(2, ChatLog.FindOverlap(previous, current));
        Assert.Equal("e", Assert.Single(current.Skip(2)));
        Assert.Equal(2, ChatLog.FindOverlap(["a", "b"], ["a", "b"]));
        Assert.Equal(0, ChatLog.FindOverlap(["a", "b", "c"], ["d", "e"]));
        Assert.Equal(1, ChatLog.FindOverlap(["a", "b"], ["b"]));
    }

    [Fact]
    public void Keywords_are_case_insensitive_and_ignore_the_timestamp()
    {
        var line = "[14:05:09] JOHN Doe says: Meet me at the bank";

        var matched = ChatLog.FindKeywords(line, ["John", "BANK", "14:05"]);
        Assert.Equal(2, matched.Count);
        Assert.Equal("John", matched[0]);
        Assert.Equal("BANK", matched[1]);
        Assert.Empty(ChatLog.FindKeywords(line, ["  ", "14"]));
    }

    [Fact]
    public void Keywords_can_require_matching_letter_case()
    {
        var line = "[14:05:09] JOHN Doe says: Meet me at the bank";

        var matched = ChatLog.FindKeywords(line, ["John", "bank"], caseSensitive: true);
        Assert.Equal("bank", Assert.Single(matched));
    }

    [Fact]
    public void Shorter_rewrite_keeps_the_existing_log()
    {
        var startedAt = new DateTime(2026, 1, 3, 8, 0, 0);
        var root = Path.Combine(Path.GetTempPath(), "gtaw-parser-" + Guid.NewGuid().ToString("N"));
        try
        {
            var longer = ChatLog.Render(startedAt, ["[08:00:00] first", "[08:00:01] second"]);
            var shorter = ChatLog.Render(startedAt, ["[08:00:00] first"]);
            var path = LogArchive.Save(root, startedAt, longer);
            LogArchive.Save(root, startedAt, shorter);

            Assert.Equal(longer, File.ReadAllText(path));
            Assert.Equal(Path.Combine(root, "2026", "JAN", "03.JAN.2026-08.00.00.txt"), path);

            var grown = ChatLog.Render(startedAt, ["[08:00:00] first", "[08:00:01] second", "[08:00:02] third"]);
            LogArchive.Save(root, startedAt, grown);
            Assert.Equal(grown, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Saved_text_round_trips_through_the_session_header()
    {
        var session = ChatSession.Start("[9:01:02] Hello", CapturedAt);
        session.Add([ChatLog.FormatLine("[9:01:02] Hello", CapturedAt)], ["[9:01:02] ^#C8A2FFHello"]);
        var loaded = ChatSession.TryLoad(session.ToFileText());

        Assert.NotNull(loaded);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 1, 2), loaded.StartedAt);
        Assert.Equal("[9:01:02] Hello", Assert.Single(loaded.Lines));

        session.PaintColors(["[9:01:02] ^#FFBB33Hello"]);
        Assert.Equal("[9:01:02] Hello", session.Lines[0]);
        Assert.Equal("[9:01:02] ^#FFBB33Hello", session.DisplayLines[0]);
    }

    [Fact]
    public void Transcript_keeps_color_codes_unless_the_line_is_highlighted()
    {
        var plain = "[14:05:09] John Doe says: Hello";
        var colored = "[14:05:09] ^#FFBB33John Doe says: Hello";

        Assert.Equal(colored, ChatLog.ForTranscript(plain, colored, ["bank"], false));
        Assert.Equal(plain, ChatLog.ForTranscript(plain, colored, ["John"], false));
        Assert.Equal("John Doe says: Hello", ChatLog.StripColorCodes("^#FFBB33John Doe says: Hello"));

        var spans = ChatLog.SplitColorCodes("[14:05:09] ^#FFBB33John ^#FFFFFFDoe");
        Assert.Equal(3, spans.Count);
        Assert.Equal("[14:05:09] ", spans[0].Text);
        Assert.Null(spans[0].Hex);
        Assert.Equal("John ", spans[1].Text);
        Assert.Equal("#FFBB33", spans[1].Hex);
        Assert.Equal("Doe", spans[2].Text);
        Assert.Equal("#FFFFFF", spans[2].Hex);
    }

    [Fact]
    public void Missing_hud_color_uses_the_message_fallback()
    {
        Assert.StartsWith("^#F4E800", ChatLog.WithFallbackColor("[18:19:27] (( PM from (468) Selene Watson: private message ))"));
        Assert.StartsWith("^#F4E800", ChatLog.WithFallbackColor("[18:19:27] (( PM to (468) Selene Watson: private message ))"));
        Assert.StartsWith("^#FFFFFF", ChatLog.WithFallbackColor("[18:19:28] Selene Watson says: IC chat"));
        Assert.StartsWith("^#FFFFFF", ChatLog.WithFallbackColor("[18:19:28] Name Lastname says [low] quietly"));
        Assert.StartsWith("^#FFFFFF", ChatLog.WithFallbackColor("[18:19:28] Name Lastname says [lower] quietly"));
        Assert.StartsWith("^#FFFFFF", ChatLog.WithFallbackColor("[18:19:28] Name Last says [low] (to Name Lastname): hello"));
        Assert.StartsWith("^#939498", ChatLog.WithFallbackColor("[18:19:31] (( (468) Selene Watson: /b chat ))"));
        Assert.StartsWith("^#BB9BD9", ChatLog.WithFallbackColor("[18:19:32] * Selene Watson me line"));
        Assert.StartsWith("^#BB9BD9", ChatLog.WithFallbackColor("[18:19:34] * do line (( Selene Watson ))*"));
        Assert.StartsWith("^#BB9BD9", ChatLog.WithFallbackColor("[18:19:37] * Selene Watson's line"));
        Assert.Equal("[18:19:38] * (A) Admin message", ChatLog.WithFallbackColor("[18:19:38] * (A) Admin message"));
        Assert.Equal("[18:19:40] Weather forecast:", ChatLog.WithFallbackColor("[18:19:40] Weather forecast:"));

        var hud = "[18:19:32] ^#00FF00* Selene Watson me line";
        Assert.Equal(hud, ChatLog.ForTranscript("[18:19:32] * Selene Watson me line", hud, [], false));
        Assert.Equal("[18:19:32] * Selene Watson me line", ChatLog.ForTranscript("[18:19:32] * Selene Watson me line", null, ["Selene"], false));
    }

    [Fact]
    public void Inspector_result_keeps_the_chat_text()
    {
        const string json = """
            {
              "result": {
                "type": "string",
                "value": "[\"[05:54:17] Hello there\"]"
              }
            }
            """;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var text = NuiChatClient.ReadEvaluateText(document.RootElement);
        Assert.Equal("[\"[05:54:17] Hello there\"]", text);
    }
}
