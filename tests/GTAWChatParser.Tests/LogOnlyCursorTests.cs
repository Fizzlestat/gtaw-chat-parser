using Xunit;

namespace GTAWChatParser.Tests;

public class LogOnlyCursorTests
{
    private const string Header = "[DATE: 24/OCT/2026 | TIME: 18:00:00]";

    [Fact]
    public void First_snapshot_is_not_replayed()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        string[] log = new[] { "[18:00:00] Welcome", "[18:00:01] > Alex waves" };

        Assert.Empty(cursor.Take(Header, log, []));
        Assert.Empty(cursor.Take(Header, log, []));
    }

    [Fact]
    public void Nearby_emote_is_added_on_the_next_poll()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        string[] initial = new[] { "[18:00:00] Welcome" };
        Assert.Empty(cursor.Take(Header, initial, ["[18:00:00] Welcome"]));

        string[] grown = new[] { "[18:00:00] Welcome", "[18:00:02] > Alex waves" };
        Assert.Empty(cursor.Take(Header, grown, ["[18:00:00] Welcome"]));
        Assert.Equal("[18:00:02] > Alex waves", Assert.Single(cursor.Take(Header, grown, ["[18:00:00] Welcome"])));
    }

    [Fact]
    public void Chat_already_in_the_session_is_not_copied_from_the_log()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        Assert.Empty(cursor.Take(Header, ["[18:00:00] Welcome"], ["[18:00:00] Welcome"]));

        string[] log = new[] { "[18:00:00] Welcome", "[18:00:03] Alex says: hello" };
        string[] session = new[] { "[18:00:00] Welcome", "[18:00:03] Alex says: hello" };
        Assert.Empty(cursor.Take(Header, log, session));
        Assert.Empty(cursor.Take(Header, log, session));
    }

    [Fact]
    public void Emote_between_chat_lines_is_added_immediately()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        Assert.Empty(cursor.Take(Header, ["[18:00:00] Welcome"], ["[18:00:00] Welcome"]));

        string[] log = new[]
        {
            "[18:00:00] Welcome",
            "[18:00:04] > Alex waves",
            "[18:00:05] Alex says: hello"
        };
        string[] session = new[] { "[18:00:00] Welcome", "[18:00:05] Alex says: hello" };

        Assert.Equal("[18:00:04] > Alex waves", Assert.Single(cursor.Take(Header, log, session)));

        string[] saved = new[] { "[18:00:00] Welcome", "[18:00:05] Alex says: hello", "[18:00:04] > Alex waves" };
        Assert.Empty(cursor.Take(Header, log, saved));
        Assert.Empty(cursor.Take(Header, log, saved));
    }

    [Fact]
    public void Slash_markup_and_a_one_second_clock_skew_still_match()
    {
        Assert.True(LogLines.Same("[18:00:01] * Alex /waves/", "[18:00:02] * Alex waves"));
        Assert.False(LogLines.Same("[18:00:01] Alex says: hi", "[18:00:09] Alex says: hi"));
    }

    [Fact]
    public void A_new_session_header_does_not_dump_the_existing_log()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        Assert.Empty(cursor.Take(Header, ["[18:00:00] Welcome"], []));

        string restarted = "[DATE: 24/OCT/2026 | TIME: 19:00:00]";
        string[] log = new[] { "[19:00:00] Welcome back", "[19:00:01] > Alex sits" };
        Assert.Empty(cursor.Take(restarted, log, []));
        Assert.Empty(cursor.Take(restarted, log, []));
    }

    [Fact]
    public void A_newline_inside_one_message_is_not_logged_again()
    {
        const string stored = "[DATE: 24/OCT/2026 | TIME: 21:40:15]\n[21:40:15] [AdmREPORT]: Alex has submitted a report:\ncan somebody check the logs\n";
        Assert.True(LogLines.TrySplitTranscript(stored, out _, out List<string> lines));
        string joined = Assert.Single(lines);
        Assert.Equal("[21:40:15] [AdmREPORT]: Alex has submitted a report: can somebody check the logs", joined);

        LogOnlyCursor cursor = new LogOnlyCursor();
        Assert.Empty(cursor.Take(Header, [], []));
        Assert.Empty(cursor.Take(Header, lines, [joined]));
        Assert.Empty(cursor.Take(Header, lines, [joined]));
    }

    [Fact]
    public void Dom_can_claim_a_line_the_log_already_added()
    {
        LogOnlyCursor cursor = new LogOnlyCursor();
        cursor.Remember(["[18:00:06] Alex says: hello"]);

        Assert.True(cursor.ClaimDom("[18:00:06] Alex says: hello"));
        Assert.False(cursor.ClaimDom("[18:00:06] Alex says: hello"));
    }
}
