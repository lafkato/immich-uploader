namespace ImmichUploaderApp.Models;

public sealed record WatcherActivitySnapshot(
    string StatusText,
    string? CurrentFileName,
    double? CurrentFileProgressPercent,
    int QueueCount,
    IReadOnlyList<RecentUpload> RecentUploads,
    IReadOnlyList<RecentFailure> RecentFailures, bool IsScanning = false, int FilesChecked = 0, DateTime? LastScanAtLocal = null, string? ScanError = null);
