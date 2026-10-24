using System.Text.Json;
using System.Text.Json.Nodes;
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

    public bool CheckUpdatesOnStartup { get; set; } = true;

    [JsonIgnore]
    public string FilePath { get; internal set; } = AppPaths.ConfigPath;

    private readonly JsonObject _preserved = [];

    private bool _protectExistingFile;

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
        var config = Parse(json);
        config.FilePath = AppPaths.ConfigPath;
        return config;
    }

    internal static AppConfig Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings file is empty.");

        var config = new AppConfig();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (config.TryApply(property.Name, property.Value))
                seen.Add(property.Name);
            else
                config.Keep(property.Name, property.Value);
        }

        if (!seen.Contains("startMinimizedToTray"))
            config.StartMinimizedToTray = config.StartWithWindows;

        config.Normalize();
        return config;
    }

    public void Save()
    {
        if (_protectExistingFile && File.Exists(FilePath))
        {
            AppLog.Error("Settings were not saved because the existing file could not be read.");
            return;
        }

        Directory.CreateDirectory(AppPaths.Directory);
        Normalize();
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, FilePath, overwrite: true);
        _protectExistingFile = false;
    }

    internal void ProtectExistingFile() => _protectExistingFile = true;

    internal string ToJson()
    {
        var node = JsonSerializer.SerializeToNode(this, SerializerOptions) as JsonObject ?? [];
        foreach (var extra in _preserved)
        {
            if (extra.Value != null)
                node[extra.Key] = extra.Value.DeepClone();
        }

        return node.ToJsonString(SerializerOptions);
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
        CheckUpdatesOnStartup = other.CheckUpdatesOnStartup;
        _preserved.Clear();
        foreach (var extra in other._preserved)
        {
            if (extra.Value != null)
                _preserved[extra.Key] = extra.Value.DeepClone();
        }
    }

    private void Normalize()
    {
        BackupDirectory = string.IsNullOrWhiteSpace(BackupDirectory)
            ? AppPaths.DefaultBackupDirectory
            : BackupDirectory;
        NotificationSeconds = ResolvedNotificationSeconds;
        NotificationAnimation = NotificationMotionExtensions.Format(ResolvedNotificationAnimation);
        Keywords = NormalizeKeywords(Keywords, NotificationCaseSensitive);
        PrimaryColor = HudPalette.NormalizeHex(PrimaryColor, "#E8A838");
        SecondaryColor = HudPalette.NormalizeHex(SecondaryColor, "#0C0E12");
        UiFont = string.IsNullOrWhiteSpace(UiFont) ? "Segoe UI" : UiFont.Trim();
        TranscriptFont = string.IsNullOrWhiteSpace(TranscriptFont) ? "Consolas" : TranscriptFont.Trim();
    }

    private bool TryApply(string name, JsonElement value)
    {
        try
        {
            switch (name)
            {
                case "backupDirectory":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    BackupDirectory = value.GetString() ?? BackupDirectory;
                    return true;
                case "keywords":
                    if (value.ValueKind != JsonValueKind.Array)
                        return false;
                    var keywords = new List<string>();
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            return false;
                        keywords.Add(item.GetString() ?? "");
                    }

                    Keywords = keywords;
                    return true;
                case "devtoolsPort":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var port))
                        return false;
                    DevtoolsPort = port;
                    return true;
                case "notificationsEnabled":
                    if (!TryReadBool(value, out var notifications))
                        return false;
                    NotificationsEnabled = notifications;
                    return true;
                case "notificationSoundEnabled":
                    if (!TryReadBool(value, out var sound))
                        return false;
                    NotificationSoundEnabled = sound;
                    return true;
                case "notificationSeconds":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var seconds))
                        return false;
                    NotificationSeconds = seconds;
                    return true;
                case "notificationCaseSensitive":
                    if (!TryReadBool(value, out var caseSensitive))
                        return false;
                    NotificationCaseSensitive = caseSensitive;
                    return true;
                case "notificationAnimation":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    NotificationAnimation = value.GetString() ?? NotificationAnimation;
                    return true;
                case "primaryColor":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    PrimaryColor = value.GetString() ?? PrimaryColor;
                    return true;
                case "secondaryColor":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    SecondaryColor = value.GetString() ?? SecondaryColor;
                    return true;
                case "uiFont":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    UiFont = value.GetString() ?? UiFont;
                    return true;
                case "transcriptFont":
                    if (value.ValueKind != JsonValueKind.String)
                        return false;
                    TranscriptFont = value.GetString() ?? TranscriptFont;
                    return true;
                case "startWithWindows":
                    if (!TryReadBool(value, out var startWithWindows))
                        return false;
                    StartWithWindows = startWithWindows;
                    return true;
                case "startMinimizedToTray":
                    if (!TryReadBool(value, out var startMinimized))
                        return false;
                    StartMinimizedToTray = startMinimized;
                    return true;
                case "checkUpdatesOnStartup":
                    if (!TryReadBool(value, out var checkUpdates))
                        return false;
                    CheckUpdatesOnStartup = checkUpdates;
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Keep(string name, JsonElement value)
    {
        var copy = JsonNode.Parse(value.GetRawText());
        if (copy != null)
            _preserved[name] = copy;
    }

    private static bool TryReadBool(JsonElement value, out bool result)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            result = true;
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            result = false;
            return true;
        }

        result = false;
        return false;
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
