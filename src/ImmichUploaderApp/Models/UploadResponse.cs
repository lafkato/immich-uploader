using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class UploadResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
