using System.Text.Json;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); }

Check(ImmichClient.IsValidServerUrl("https://immich.example.com/api"), "HTTPS URL is accepted");
Check(ImmichClient.IsValidServerUrl("http://localhost:2283/api"), "HTTP localhost URL is accepted");
Check(!ImmichClient.IsValidServerUrl("immich.example.com/api"), "Relative URL is rejected");
Check(!ImmichClient.IsValidServerUrl("file:///C:/data"), "Non-HTTP URL is rejected");
Check(ImmichClient.GetContentType("photo.HEIC") == "image/heic", "HEIC MIME type");
Check(ImmichClient.GetContentType("clip.mkv") == "video/x-matroska", "MKV MIME type");

var serialized = JsonSerializer.Serialize(new AppConfig { ApiKey = "do-not-persist-this" });
Check(!serialized.Contains("do-not-persist-this", StringComparison.Ordinal), "API key is never serialized in plaintext");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed: " + string.Join("; ", failures));
    return 1;
}

Console.WriteLine("Smoke tests passed.");
return 0;
