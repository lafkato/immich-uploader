using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class AlbumSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; } = string.Empty;
}
