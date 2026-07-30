using System.Reflection;
using System.Text.Json;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

// PhotoSyncService.SanitizeFileName/SanitizeFolderName are what stand between a server-supplied
// asset filename or album name (which can come from another user's shared album, or any client
// that never validated what it sent) and Path.Combine writing outside the configured sync folder.
// Reflection because they're intentionally private - this is a regression guard, not a public API.
{
    var syncType = typeof(PhotoSyncService);
    var sanitizeFileName = syncType.GetMethod("SanitizeFileName", BindingFlags.NonPublic | BindingFlags.Static)!;
    var sanitizeFolderName = syncType.GetMethod("SanitizeFolderName", BindingFlags.NonPublic | BindingFlags.Static)!;

    string FileName(string name) => (string)sanitizeFileName.Invoke(null, new object[] { name, "FALLBACK" })!;
    string FolderName(string name) => (string)sanitizeFolderName.Invoke(null, new object[] { name })!;

    Check(FileName("..") == "FALLBACK", "Bare '..' filename falls back instead of resolving to a parent directory");
    Check(FileName(".") == "FALLBACK", "Bare '.' filename falls back");
    Check(FileName(@"..\..\..\evil.exe") == "......evil.exe", "Embedded traversal separators are stripped, not preserved as path structure");
    Check(FileName("normal-photo.jpg") == "normal-photo.jpg", "An ordinary filename passes through unchanged");
    Check(FolderName("..") == Loc.T("sync.noAlbum"), "Bare '..' album name falls back instead of resolving to a parent directory");
    Check(FolderName("My Album") == "My Album", "An ordinary album name passes through unchanged");
}

Check(ImmichClient.IsValidServerUrl("https://immich.example.com/api"), "HTTPS URL is accepted");
Check(ImmichClient.IsValidServerUrl("http://localhost:2283/api"), "HTTP localhost URL is accepted");
Check(!ImmichClient.IsValidServerUrl("immich.example.com/api"), "Relative URL is rejected");
Check(!ImmichClient.IsValidServerUrl("file:///C:/data"), "Non-HTTP URL is rejected");
Check(ImmichClient.GetContentType("photo.HEIC") == "image/heic", "HEIC MIME type");
Check(ImmichClient.GetContentType("clip.mkv") == "video/x-matroska", "MKV MIME type");

var serialized = JsonSerializer.Serialize(new AppConfig { ApiKey = "do-not-persist-this" });
Check(!serialized.Contains("do-not-persist-this", StringComparison.Ordinal), "API key is never serialized in plaintext");

Check(UpdateService.IsNewer(new Version(1, 0, 3), new Version(1, 0, 2)), "1.0.3 is newer than 1.0.2");
Check(!UpdateService.IsNewer(new Version(1, 0, 2), new Version(1, 0, 2)), "1.0.2 is not newer than itself");
Check(!UpdateService.IsNewer(new Version(1, 0, 2), new Version(1, 0, 2, 0)), "3-part tag version vs 4-part assembly version compares equal, not older");
Check(UpdateService.IsNewer(new Version(2, 0, 0), new Version(1, 9, 9)), "Major version bump is newer");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("Smoke tests passed.");
return 0;
