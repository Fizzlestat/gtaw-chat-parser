using System.Text.Json;
using Xunit;

namespace GTAWChatParser.Tests;

public class AppConfigTests
{
    [Fact]
    public void Older_file_keeps_keywords_and_fills_in_settings_that_were_added_later()
    {
        var config = AppConfig.Parse("""
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

        var saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("pop", saved.GetProperty("notificationAnimation").GetString());
        Assert.True(saved.GetProperty("startWithWindows").GetBoolean());
        Assert.True(saved.GetProperty("checkUpdatesOnStartup").GetBoolean());
    }

    [Fact]
    public void Saving_keeps_settings_this_version_does_not_know_about()
    {
        var config = AppConfig.Parse("""
            {
              "keywords": [ "Alex" ],
              "notificationAnimation": "pop",
              "startMinimizedToTray": false,
              "futureSetting": { "kept": true }
            }
            """);

        config.NotificationSeconds = 8;
        var saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("pop", saved.GetProperty("notificationAnimation").GetString());
        Assert.False(saved.GetProperty("startMinimizedToTray").GetBoolean());
        Assert.Equal(8, saved.GetProperty("notificationSeconds").GetInt32());
        Assert.True(saved.GetProperty("futureSetting").GetProperty("kept").GetBoolean());
    }

    [Fact]
    public void A_value_this_version_cannot_read_is_left_in_the_file()
    {
        var config = AppConfig.Parse("""
            {
              "keywords": [ "Alex" ],
              "notificationSeconds": "forever"
            }
            """);

        var saved = JsonDocument.Parse(config.ToJson()).RootElement;
        Assert.Equal("Alex", saved.GetProperty("keywords")[0].GetString());
        Assert.Equal("forever", saved.GetProperty("notificationSeconds").GetString());
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_not_replaced()
    {
        var path = Path.Combine(Path.GetTempPath(), "gtaw-config-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var config = new AppConfig { FilePath = path };
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
