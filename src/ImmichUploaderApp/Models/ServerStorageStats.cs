using System.Text.Json.Serialization;

namespace ImmichUploaderApp.Models;

public sealed class ServerStorageStats
{
    [JsonPropertyName("diskUseRaw")]
    public long DiskUseRaw { get; set; }

    [JsonPropertyName("diskSizeRaw")]
    public long DiskSizeRaw { get; set; }

    [JsonPropertyName("diskAvailableRaw")]
    public long DiskAvailableRaw { get; set; }

    [JsonPropertyName("diskUsagePercentage")]
    public double DiskUsagePercentage { get; set; }
}
