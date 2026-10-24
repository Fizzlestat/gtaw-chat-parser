using Xunit;

namespace GTAWChatParser.Tests;

public class CitizenKvpTests
{
    private const string Transcript = "[DATE: 24/OCT/2026 | TIME: 18:00:00]\n[18:00:00] Welcome\n[18:00:01] > Alex waves\n";

    [Fact]
    public void Crc32c_matches_the_known_vector()
    {
        var crc = Crc32C.Compute("123456789"u8);
        Assert.Equal(0xE3069283u, crc);
    }

    [Fact]
    public void Snappy_literal_round_trips()
    {
        byte[] compressed = [0x05, 0x10, 0x68, 0x65, 0x6c, 0x6c, 0x6f];
        Assert.True(Snappy.TryDecompress(compressed, out var text));
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(text));
    }

    [Fact]
    public void Wal_record_returns_the_latest_transcript()
    {
        var older = LevelDb.EncodeWal(4, "res:client:gtaw_chat_log", "[DATE: 24/OCT/2026 | TIME: 17:00:00]\n[17:00:00] Old\n");
        var newer = LevelDb.EncodeWal(9, "res:client:gtaw_chat_log", Transcript);
        var file = new byte[older.Length + newer.Length];
        older.CopyTo(file, 0);
        newer.CopyTo(file, older.Length);

        var found = LevelDb.ReadWal(file);
        Assert.True(found.Has);
        Assert.Equal(Transcript, found.Text);
    }

    [Fact]
    public void Wal_resume_reads_only_the_appended_record()
    {
        var older = LevelDb.EncodeWal(4, "res:client:gtaw_chat_log", "[DATE: 24/OCT/2026 | TIME: 17:00:00]\n[17:00:00] Old\n");
        var newer = LevelDb.EncodeWal(9, "res:client:gtaw_chat_log", Transcript);
        var offset = 0;
        var assembling = false;
        var fragment = new List<byte>();
        var best = LevelDb.ReadWal(older, ref offset, fragment, ref assembling, default);
        Assert.Equal(older.Length, offset);

        var file = new byte[older.Length + newer.Length];
        older.CopyTo(file, 0);
        newer.CopyTo(file, older.Length);
        best = LevelDb.ReadWal(file, ref offset, fragment, ref assembling, best);
        Assert.Equal(Transcript, best.Text);
        Assert.Equal(file.Length, offset);
    }

    [Fact]
    public void Table_record_returns_the_transcript()
    {
        var file = LevelDb.EncodeTable(12, "res:client:gtaw_chat_log", Transcript);
        var found = LevelDb.ReadTable(file);
        Assert.True(found.Has);
        Assert.Equal(Transcript, found.Text);
        Assert.True(LogLines.TrySplitTranscript(found.Text!, out var header, out var lines));
        Assert.Equal("[DATE: 24/OCT/2026 | TIME: 18:00:00]", header);
        Assert.Equal("[18:00:01] > Alex waves", lines[1]);
    }
}
