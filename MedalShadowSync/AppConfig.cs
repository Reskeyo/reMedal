using System.Text.Json;

namespace MedalShadowSync;

public sealed class AppConfig
{
    public string WatchFolder { get; set; } = "";
    public bool IncludeSubdirectories { get; set; } = true;
    public string? MedalDbPath { get; set; }
    public string? MedalClipFolder { get; set; }
    public bool StartMinimized { get; set; }
    public int MaxBackups { get; set; } = 5;
    public bool ThrowbacksEnabled { get; set; }
    public int ThrowbackWindowDays { get; set; } = 3;
    public bool ThrowbackOnlyLastYear { get; set; } = false;
    public bool BlockAdsEnabled { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "reMedal");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string BackupsDir => Path.Combine(AppDataDir, "backups");

    public static AppConfig Load()
    {
        try
        {
            var path = File.Exists(ConfigPath)
                ? ConfigPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedalShadowSync", "config.json");

            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path));
                if (cfg is not null)
                    return cfg;
            }
        }
        catch
        {
            // Fall back to defaults on corrupt config
        }

        return new AppConfig { WatchFolder = MedalEnvironment.FindDefaultWatchFolder() };
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
