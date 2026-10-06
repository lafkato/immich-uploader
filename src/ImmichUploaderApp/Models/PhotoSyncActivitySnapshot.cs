namespace ImmichUploaderApp.Models;

public sealed record PhotoSyncActivitySnapshot(string StatusText, IReadOnlyList<RecentDownload> RecentDownloads, IReadOnlyList<RecentDeletion> RecentDeletions, bool IsScanning = false, int FilesChecked = 0, int FilesTransferred = 0, string? CurrentFileName = null, double? ProgressPercent = null, long BytesTransferred = 0, DateTime? LastScanAtLocal = null, string? ScanError = null);
