using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using ImmichUploaderApp.Models;

namespace ImmichUploaderApp.Services;

public sealed record UpdateCheckResult(Version LatestVersion, string TagName, string? DownloadUrl, string? ReleaseUrl, string? Digest = null, long Size = 0);

public sealed class UpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/lafkato/immich-uploader/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly HttpClient DownloadHttp = CreateDownloadHttpClient();
    private readonly HttpClient _http;
    private readonly HttpClient _downloadHttp;
    public UpdateService(HttpClient? http = null) { _http = http ?? Http; _downloadHttp = http ?? DownloadHttp; }
    private static HttpClient CreateDownloadHttpClient()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ImmichUploaderApp");
        return http;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ImmichUploaderApp");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(ReleasesApiUrl, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub palautti virheen ({(int)response.StatusCode} {response.StatusCode}).");

        var release = JsonSerializer.Deserialize<GitHubRelease>(body, JsonOptions);
        if (release is null) throw new InvalidOperationException("Tyhja vastaus GitHubilta.");

        var versionText = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
            throw new InvalidOperationException($"Tunnistamaton versiotunniste: {release.TagName}");

        var installerAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        return new UpdateCheckResult(latestVersion, release.TagName, installerAsset?.DownloadUrl, release.HtmlUrl, installerAsset?.Digest, installerAsset?.Size ?? 0);
    }

    public async Task<string> DownloadInstallerAsync(string downloadUrl, string fileName,
        Action<long, long>? onProgress = null, CancellationToken ct = default,
        string? expectedDigest = null, long expectedSize = 0)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            throw new ArgumentException("Invalid installer filename.", nameof(fileName));
        // Each attempt gets its own directory so an existing installer cannot lock the download.
        var folder = Path.Combine(Path.GetTempPath(), "ImmichUploader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var finalPath = Path.Combine(folder, fileName);
        var partialPath = finalPath + ".partial";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            using var response = await _downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Installer download failed ({(int)response.StatusCode} {response.StatusCode}).");
            var totalBytes = expectedSize > 0 ? expectedSize : response.Content.Headers.ContentLength ?? -1;
            long bytesRead = 0;
            await using (var httpStream = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var fileStream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await httpStream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    bytesRead += read;
                    onProgress?.Invoke(bytesRead, totalBytes);
                }
            }
            if (totalBytes > 0 && bytesRead != totalBytes) throw new InvalidDataException("Installer download is incomplete. Please retry.");
            await using (var stream = File.OpenRead(partialPath))
            {
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("Downloaded file is not a Windows installer.");
                if (expectedDigest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
                {
                    stream.Position = 0;
                    var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, timeout.Token));
                    if (!hash.Equals(expectedDigest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer checksum mismatch. Please retry.");
                }
            }
            File.Move(partialPath, finalPath);
            return finalPath;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Installer download timed out after 10 minutes. Please retry or download from the release page.");
        }
        finally
        {
            if (!File.Exists(finalPath))
            {
                try { File.Delete(partialPath); Directory.Delete(folder); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Compares only Major/Minor/Build: GitHub tags are 3-part ("1.0.2"), which leaves
    /// Version.Revision at -1 after parsing, while the running assembly's Version has
    /// Revision 0 - a naive Version.CompareTo would then treat an equal release as older.
    /// </summary>
    public static bool IsNewer(Version latest, Version current)
    {
        if (latest.Major != current.Major) return latest.Major > current.Major;
        if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
        return Math.Max(latest.Build, 0) > Math.Max(current.Build, 0);
    }
}
