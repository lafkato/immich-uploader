namespace ImmichUploaderApp.Models;

public sealed record PhotoSyncActivitySnapshot(string StatusText, IReadOnlyList<RecentDownload> RecentDownloads);
