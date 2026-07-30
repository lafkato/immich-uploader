using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class AssetSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; set; } = string.Empty;

    [JsonPropertyName("fileCreatedAt")]
    public DateTime FileCreatedAt { get; set; }

    /// "IMAGE" or "VIDEO" (verified against a live server's /search/metadata response).
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// "timeline" for normal assets, "hidden" for e.g. an iPhone Live Photo's paired motion-clip
    /// VIDEO asset - Immich itself excludes these from the main library view (verified live: the
    /// hidden VIDEO half of a Live Photo has a ~1-3s duration and the IMAGE half's
    /// livePhotoVideoId points at it).
    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = string.Empty;
}
