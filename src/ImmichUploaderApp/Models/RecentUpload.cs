namespace ImmichUploaderApp.Models;

public sealed record RecentUpload(string FileName, DateTime UploadedAtLocal, long SizeBytes, byte[]? ThumbnailPng);
