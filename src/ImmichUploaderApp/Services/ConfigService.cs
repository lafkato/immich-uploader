using System.Text.Json;
using ImmichUploaderApp.Models;

namespace ImmichUploaderApp.Services;

public sealed class ConfigService
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".immich-uploader");

    public static string ConfigPath => Path.Combine(StateDir, "config.json");
    public static string LogDir => Path.Combine(StateDir, "logs");
    public static string UploadedLogPath => Path.Combine(StateDir, "uploaded.log");
    public static string UploadHistoryPath => Path.Combine(StateDir, "upload-history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AppConfig LoadOrCreate()
    {
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(LogDir);

        if (!File.Exists(ConfigPath))
        {
            var fresh = new AppConfig();
            Save(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null) throw new InvalidDataException("config.json deserialized to null");
            if (string.IsNullOrWhiteSpace(config.DeviceId)) config.DeviceId = Guid.NewGuid().ToString();
            if (!string.IsNullOrWhiteSpace(config.ApiKeyProtected))
            {
                config.ApiKey = ApiKeyProtector.Unprotect(config.ApiKeyProtected);
            }
            return config;
        }
        catch (Exception)
        {
            // Korruptoitunut config.json: varmuuskopioidaan ja aloitetaan tyhjästä sen sijaan että kaadutaan käynnistyksessä.
            var backupPath = ConfigPath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(ConfigPath, backupPath, overwrite: true);
            var fresh = new AppConfig();
            Save(fresh);
            return fresh;
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(StateDir);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            config.ApiKeyProtected = ApiKeyProtector.Protect(config.ApiKey);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }
}
