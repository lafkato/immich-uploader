using System.Net;
using System.Security.Cryptography;
using ImmichUploaderApp.Services;

internal static class UpdateDownloadTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var bytes = new byte[1024]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));
        var name = "ImmichUploaderSetup-test-" + Guid.NewGuid().ToString("N") + ".exe";
        var oldPath = Path.Combine(Path.GetTempPath(), name);
        var folders = new List<string>();
        try
        {
            // Reproduce the user's error: an installer with the same filename is already locked.
            await using var locked = new FileStream(oldPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            await locked.WriteAsync(bytes);
            using var http = new HttpClient(new InstallerHandler(bytes));
            var updater = new UpdateService(http);
            var first = await updater.DownloadInstallerAsync("https://example.test/setup.exe", name, expectedDigest: digest, expectedSize: bytes.Length);
            folders.Add(Path.GetDirectoryName(first)!);
            var second = await updater.DownloadInstallerAsync("https://example.test/setup.exe", name, expectedDigest: digest, expectedSize: bytes.Length);
            folders.Add(Path.GetDirectoryName(second)!);
            check(first != oldPath && first != second && File.ReadAllBytes(first).SequenceEqual(bytes), "Locked legacy installer does not block GitHub update download");
            await using (var readable = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(readable.Length == bytes.Length, "Downloaded installer is fully closed before launch");
            var rejected = false;
            try { await updater.DownloadInstallerAsync("https://example.test/setup.exe", name, expectedSize: bytes.Length + 1); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "Truncated installer is rejected");
            rejected = false;
            try { await updater.DownloadInstallerAsync("https://example.test/setup.exe", name, expectedDigest: "sha256:" + new string('0', 64)); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "Installer checksum mismatch is rejected");
            using var invalidHttp = new HttpClient(new InstallerHandler(new byte[] { 1, 2, 3 }));
            rejected = false;
            try { await new UpdateService(invalidHttp).DownloadInstallerAsync("https://example.test/setup.exe", name); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "Non-executable response is not presented as a valid installer");
        }
        finally
        {
            File.Delete(oldPath);
            foreach (var folder in folders) Directory.Delete(folder, recursive: true);
        }
    }

    public static async Task<int> VerifyLiveReleaseAsync()
    {
        var updater = new UpdateService();
        var release = await updater.CheckForUpdateAsync();
        var name = Path.GetFileName(new Uri(release.DownloadUrl!).LocalPath);
        var path = await updater.DownloadInstallerAsync(release.DownloadUrl!, name, expectedDigest: release.Digest, expectedSize: release.Size);
        Console.WriteLine($"Live GitHub update download verified: {release.TagName}, {new FileInfo(path).Length} bytes, SHA256 matched.");
        File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!);
        return 0;
    }

    private sealed class InstallerHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.Headers.Accept.Any(h => h.MediaType == "application/octet-stream")) throw new InvalidOperationException("Missing binary download Accept header");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
