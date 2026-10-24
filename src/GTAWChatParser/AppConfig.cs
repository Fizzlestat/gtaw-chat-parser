using System.Text.Json;
using System.Text.Json.Serialization;

namespace GTAWChatParser;

public sealed class AppConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string BackupDirectory { get; set; } = AppPaths.DefaultBackupDirectory;

    public List<string> Keywords { get; set; } = [];

    public int DevtoolsPort { get; set; } = 13172;

    public bool NotificationsEnabled { get; set; } = true;

    public bool NotificationSoundEnabled { get; set; } = true;

    public int NotificationSeconds { get; set; } = 5;

    public bool NotificationCaseSensitive { get; set; }

    public string NotificationAnimation { get; set; } = "slide";

    public string PrimaryColor { get; set; } = "#E8A838";

    public string SecondaryColor { get; set; } = "#0C0E12";

    public string UiFont { get; set; } = "Segoe UI";

    public string TranscriptFont { get; set; } = "Consolas";

    public bool StartWithWindows { get; set; }

    public bool StartMinimizedToTray { get; set; }

    [JsonIgnore]
    public string FilePath { get; private set; } = AppPaths.ConfigPath;

    [JsonIgnore]
    public int ResolvedDevtoolsPort => DevtoolsPort is > 0 and <= 65535 ? DevtoolsPort : 13172;

    [JsonIgnore]
    public int ResolvedNotificationSeconds => NotificationSeconds is >= 1 and <= 60 ? NotificationSeconds : 5;

    [JsonIgnore]
    internal NotificationMotion ResolvedNotificationAnimation => NotificationMotionExtensions.Parse(NotificationAnimation);

    public static AppConfig Load()
    {
        Directory.CreateDirectory(AppPaths.Directory);
        if (!File.Exists(AppPaths.ConfigPath))
        {
            var created = new AppConfig();
            created.Save();
            return created;
        }

        var json = File.ReadAllText(AppPaths.ConfigPath);
        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions)
            ?? throw new InvalidDataException("Settings file is empty.");
        using (var document = JsonDocument.Parse(json))
        {
            if (!document.RootElement.TryGetProperty("startMinimizedToTray", out _))
                config.StartMinimizedToTray = config.StartWithWindows;
        }
        config.FilePath = AppPaths.ConfigPath;
        config.BackupDirectory = string.IsNullOrWhiteSpace(config.BackupDirectory)
            ? AppPaths.DefaultBackupDirectory
            : config.BackupDirectory;
        config.NotificationSeconds = config.ResolvedNotificationSeconds;
        config.NotificationAnimation = NotificationMotionExtensions.Format(config.ResolvedNotificationAnimation);
        config.Keywords = NormalizeKeywords(config.Keywords, config.NotificationCaseSensitive);
        config.PrimaryColor = HudPalette.NormalizeHex(config.PrimaryColor, "#E8A838");
        config.SecondaryColor = HudPalette.NormalizeHex(config.SecondaryColor, "#0C0E12");
        config.UiFont = string.IsNullOrWhiteSpace(config.UiFont) ? "Segoe UI" : config.UiFont.Trim();
        config.TranscriptFont = string.IsNullOrWhiteSpace(config.TranscriptFont) ? "Consolas" : config.TranscriptFont.Trim();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Directory);
        Keywords = NormalizeKeywords(Keywords, NotificationCaseSensitive);
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, overwrite: true);
    }

    public void ReplaceWith(AppConfig other)
    {
        BackupDirectory = string.IsNullOrWhiteSpace(other.BackupDirectory)
            ? AppPaths.DefaultBackupDirectory
            : other.BackupDirectory;
        Keywords = NormalizeKeywords(other.Keywords, other.NotificationCaseSensitive);
        DevtoolsPort = other.DevtoolsPort;
        NotificationsEnabled = other.NotificationsEnabled;
        NotificationSoundEnabled = other.NotificationSoundEnabled;
        NotificationSeconds = other.ResolvedNotificationSeconds;
        NotificationCaseSensitive = other.NotificationCaseSensitive;
        NotificationAnimation = NotificationMotionExtensions.Format(other.ResolvedNotificationAnimation);
        PrimaryColor = HudPalette.NormalizeHex(other.PrimaryColor, "#E8A838");
        SecondaryColor = HudPalette.NormalizeHex(other.SecondaryColor, "#0C0E12");
        UiFont = string.IsNullOrWhiteSpace(other.UiFont) ? "Segoe UI" : other.UiFont.Trim();
        TranscriptFont = string.IsNullOrWhiteSpace(other.TranscriptFont) ? "Consolas" : other.TranscriptFont.Trim();
        StartWithWindows = other.StartWithWindows;
        StartMinimizedToTray = other.StartMinimizedToTray;
    }

    public static List<string> NormalizeKeywords(IEnumerable<string>? keywords, bool caseSensitive = false)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var cleaned = new List<string>();
        if (keywords == null)
            return cleaned;

        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.Length == 0)
                continue;

            if (cleaned.Any(existing => string.Equals(existing, trimmed, comparison)))
                continue;

            cleaned.Add(trimmed);
        }

        return cleaned;
    }
}
