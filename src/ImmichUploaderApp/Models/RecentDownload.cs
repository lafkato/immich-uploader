namespace ImmichUploaderApp.Models;

public sealed record RecentDownload(string FileName, DateTime DownloadedAtLocal, long SizeBytes, byte[]? ThumbnailPng);
