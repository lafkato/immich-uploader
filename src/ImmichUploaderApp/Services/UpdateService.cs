using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using ImmichUploaderApp.Models;

namespace ImmichUploaderApp.Services;

public sealed record UpdateCheckResult(Version LatestVersion, string TagName, string? DownloadUrl, string? ReleaseUrl);

public sealed class UpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/lafkato/immich-uploader/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();
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
        using var response = await Http.GetAsync(ReleasesApiUrl, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub palautti virheen ({(int)response.StatusCode} {response.StatusCode}).");

        var release = JsonSerializer.Deserialize<GitHubRelease>(body, JsonOptions);
        if (release is null) throw new InvalidOperationException("Tyhja vastaus GitHubilta.");

        var versionText = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
            throw new InvalidOperationException($"Tunnistamaton versiotunniste: {release.TagName}");

        var installerAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        return new UpdateCheckResult(latestVersion, release.TagName, installerAsset?.DownloadUrl, release.HtmlUrl);
    }

    public async Task<string> DownloadInstallerAsync(string downloadUrl, string fileName,
        Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Latauksen haku epaonnistui ({(int)response.StatusCode} {response.StatusCode}).");

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);

        var buffer = new byte[1 << 16];
        long bytesRead = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;
            onProgress?.Invoke(bytesRead, totalBytes);
        }

        return tempPath;
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
