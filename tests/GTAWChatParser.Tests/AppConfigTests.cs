using System.Text.Json;
using NAudio.Wave;
using Xunit;

namespace GTAWChatParser.Tests;

public class AppConfigTests
{
    [Fact]
    public void Older_file_keeps_keywords_and_fills_in_settings_that_were_added_later()
    {
        AppConfig config = AppConfig.Parse("""
            {
              "backupDirectory": "C:\\Logs",
              "keywords": [ "Alex", "Selene Watson" ],
              "notificationAnimation": "pop",
              "startWithWindows": true
            }
            """);

        Assert.Equal(["Alex", "Selene Watson"], config.Keywords);
        Assert.Equal("pop", config.NotificationAnimation);
        Assert.True(config.StartWithWindows);
        Assert.True(config.StartMinimizedToTray);
        Assert.True(config.CheckUpdatesOnStartup);
        Assert.Equal(5, config.NotificationSeconds);

        JsonElement saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("pop", saved.GetProperty("notificationAnimation").GetString());
        Assert.True(saved.GetProperty("startWithWindows").GetBoolean());
        Assert.True(saved.GetProperty("checkUpdatesOnStartup").GetBoolean());
    }

    [Fact]
    public void Saving_keeps_settings_this_version_does_not_know_about()
    {
        AppConfig config = AppConfig.Parse("""
            {
              "keywords": [ "Alex" ],
              "notificationAnimation": "pop",
              "startMinimizedToTray": false,
              "futureSetting": { "kept": true }
            }
            """);

        config.NotificationSeconds = 8;
        JsonElement saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("pop", saved.GetProperty("notificationAnimation").GetString());
        Assert.False(saved.GetProperty("startMinimizedToTray").GetBoolean());
        Assert.Equal(8, saved.GetProperty("notificationSeconds").GetInt32());
        Assert.True(saved.GetProperty("futureSetting").GetProperty("kept").GetBoolean());
    }

    [Fact]
    public void A_value_this_version_cannot_read_is_left_in_the_file()
    {
        AppConfig config = AppConfig.Parse("""
            {
              "keywords": [ "Alex" ],
              "notificationSeconds": "forever"
            }
            """);

        JsonElement saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("forever", saved.GetProperty("notificationSeconds").GetString());
    }

    [Theory]
    [InlineData(@"C:\Sounds\alert.mp3", true)]
    [InlineData(@"C:\Sounds\alert.WAV", true)]
    [InlineData(@"C:\Sounds\alert.ogg", true)]
    [InlineData(@"C:\Sounds\alert.txt", false)]
    [InlineData("", false)]
    public void Only_mp3_wav_and_ogg_can_be_notification_sounds(string path, bool supported)
    {
        Assert.Equal(supported, NotificationSounds.IsSupportedFile(path));
    }

    [Fact]
    public void Custom_sound_path_is_saved_with_the_other_settings()
    {
        AppConfig config = AppConfig.Parse("""
            {
              "keywords": [ "Alex" ],
              "notificationSoundPath": "C:\\Sounds\\alert.ogg"
            }
            """);

        Assert.Equal(@"C:\Sounds\alert.ogg", config.NotificationSoundPath);
        JsonElement saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal(@"C:\Sounds\alert.ogg", saved.GetProperty("notificationSoundPath").GetString());
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2.25, true)]
    public void Sounds_longer_than_two_seconds_are_rejected(double seconds, bool tooLong)
    {
        string path = Path.Combine(Path.GetTempPath(), "gtaw-sound-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WaveFormat format = new WaveFormat(8000, 16, 1);
            int count = (int)Math.Round(format.AverageBytesPerSecond * seconds);
            count -= count % format.BlockAlign;
            using (WaveFileWriter writer = new WaveFileWriter(path, format))
                writer.Write(new byte[count], 0, count);

            Assert.Equal(tooLong, NotificationSounds.IsLongerThanLimit(path));
            bool opened = NotificationSounds.TryOpen(path, out _, out IDisposable? source);
            source?.Dispose();
            Assert.Equal(!tooLong, opened);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_unsupported_sound_path_is_cleared()
    {
        AppConfig config = AppConfig.Parse("""
            { "notificationSoundPath": "C:\\Sounds\\alert.flac" }
            """);

        Assert.Equal("", config.NotificationSoundPath);
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_replaced()
    {
        string path = Path.Combine(Path.GetTempPath(), "gtaw-config-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            AppConfig config = new AppConfig { FilePath = path };
            config.ProtectExistingFile();
            config.Keywords = ["Alex"];
            config.Save();
            Assert.Equal("{ not json", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
