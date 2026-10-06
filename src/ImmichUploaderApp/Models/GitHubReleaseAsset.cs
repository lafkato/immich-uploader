using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;
}
