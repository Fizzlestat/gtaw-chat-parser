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

    [JsonIgnore]
    public string FilePath { get; private set; } = AppPaths.ConfigPath;

    [JsonIgnore]
    public int ResolvedDevtoolsPort => DevtoolsPort is > 0 and <= 65535 ? DevtoolsPort : 13172;

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
        config.FilePath = AppPaths.ConfigPath;
        config.BackupDirectory = string.IsNullOrWhiteSpace(config.BackupDirectory)
            ? AppPaths.DefaultBackupDirectory
            : config.BackupDirectory;
        config.Keywords = NormalizeKeywords(config.Keywords);
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Directory);
        Keywords = NormalizeKeywords(Keywords);
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
        Keywords = NormalizeKeywords(other.Keywords);
        DevtoolsPort = other.DevtoolsPort;
    }

    public static List<string> NormalizeKeywords(IEnumerable<string>? keywords)
    {
        var cleaned = new List<string>();
        if (keywords == null)
            return cleaned;

        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.Length == 0)
                continue;

            if (cleaned.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                continue;

            cleaned.Add(trimmed);
        }

        return cleaned;
    }
}
