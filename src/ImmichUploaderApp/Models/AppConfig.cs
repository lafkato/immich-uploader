using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class AppConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    // Kept in memory only. ConfigService persists the protected counterpart below.
    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;
    // One-release migration path for existing plaintext config.json files. A write removes it.
    [JsonPropertyName("apiKey")]
    public string LegacyApiKey { set => ApiKey = value; }
    public string ApiKeyProtected { get; set; } = string.Empty;
    public string AlbumName { get; set; } = string.Empty;
    public List<string> Directories { get; set; } = new();
    public List<string> ExcludeDirectories { get; set; } = new();
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();
    public string DeviceName { get; set; } = Environment.MachineName;

    /// "System", "Light", or "Dark".
    public string Theme { get; set; } = "System";

    /// ISO 639-1 code: "fi", "en", "sv", "de".
    public string Language { get; set; } = "fi";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
