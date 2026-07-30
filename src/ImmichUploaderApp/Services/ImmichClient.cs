using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImmichUploaderApp.Models;

namespace ImmichUploaderApp.Services;

public sealed class ImmichApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public bool IsTransient => StatusCode is null || StatusCode == HttpStatusCode.TooManyRequests || (int)StatusCode >= 500;
    public ImmichApiException(string message, HttpStatusCode? statusCode = null) : base(message) => StatusCode = statusCode;
}

public sealed class ImmichClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tiff"] = "image/tiff",
        [".webp"] = "image/webp",
        [".dng"] = "image/x-adobe-dng",
        [".cr2"] = "image/x-canon-cr2",
        [".nef"] = "image/x-nikon-nef",
        [".arw"] = "image/x-sony-arw",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",
        [".wmv"] = "video/x-ms-wmv",
        [".m4v"] = "video/x-m4v",
        [".3gp"] = "video/3gpp",
    };

    public ImmichClient(string serverUrl, string apiKey, HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _ownsHttpClient = http is null;

        if (!IsValidServerUrl(serverUrl)) throw new ArgumentException("Server URL must be an absolute HTTP(S) URL.", nameof(serverUrl));
        var baseUrl = NormalizeServerUrl(serverUrl) + "/";
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromMinutes(30);
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static bool IsValidServerUrl(string serverUrl) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Ensures the server URL ends with "/api", appending it if the user omitted the API path.</summary>
    public static string NormalizeServerUrl(string serverUrl)
    {
        var trimmed = serverUrl.Trim().TrimEnd('/');
        return trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + "/api";
    }

    public static string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ContentTypes.TryGetValue(ext, out var contentType) ? contentType : "application/octet-stream";
    }

    private static string ToIso8601(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public async Task<UploadResponse> UploadAssetAsync(string filePath, string deviceId, string sha1,
        Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        var info = new FileInfo(filePath);
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20, useAsync: true);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(sha1), "deviceAssetId");
        content.Add(new StringContent(deviceId), "deviceId");
        content.Add(new StringContent(ToIso8601(info.CreationTimeUtc)), "fileCreatedAt");
        content.Add(new StringContent(ToIso8601(info.LastWriteTimeUtc)), "fileModifiedAt");
        content.Add(new StringContent("false"), "isFavorite");

        Stream uploadStream = onProgress is null
            ? fileStream
            : new ProgressStream(fileStream, fileStream.Length, onProgress);

        var streamContent = new StreamContent(uploadStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
        content.Add(streamContent, "assetData", Path.GetFileName(filePath));

        using var response = await _http.PostAsync("assets", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new ImmichApiException($"POST /assets epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<UploadResponse>(body, JsonOptions);
        if (result is null) throw new ImmichApiException("POST /assets: tyhja vastaus palvelimelta");
        return result;
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("albums", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ImmichApiException($"GET /albums epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);

        var albums = JsonSerializer.Deserialize<List<AlbumSummary>>(body, JsonOptions);
        return albums ?? new List<AlbumSummary>();
    }

    public async Task<string> CreateAlbumAsync(string albumName, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("albums", new { albumName }, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ImmichApiException($"POST /albums epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);

        var created = JsonSerializer.Deserialize<AlbumSummary>(body, JsonOptions);
        if (created is null || string.IsNullOrEmpty(created.Id))
            throw new ImmichApiException("POST /albums: vastauksesta puuttuu id");
        return created.Id;
    }

    /// The plain album list (GetAlbumsAsync) doesn't include membership, and - verified against
    /// a live server - GET /albums/{id} never includes an "assets" array either (only
    /// assetCount), regardless of a withoutAssets query param. The actual working way to list an
    /// album's assets is POST /search/metadata with an albumIds filter, paginated the same way
    /// as GetAssetsPageAsync.
    public async Task<IReadOnlyList<string>> GetAlbumAssetIdsAsync(string albumId, CancellationToken ct = default)
    {
        var ids = new List<string>();
        var page = 1;
        while (true)
        {
            using var response = await _http.PostAsJsonAsync("search/metadata",
                new { page, size = 1000, albumIds = new[] { albumId } }, JsonOptions, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new ImmichApiException($"POST /search/metadata (albumIds) epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);

            using var doc = JsonDocument.Parse(body);
            var assetsElement = doc.RootElement.GetProperty("assets");
            foreach (var item in assetsElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idElement) && idElement.GetString() is { } id) ids.Add(id);
            }

            var hasMore = assetsElement.TryGetProperty("nextPage", out var nextPageElement) && nextPageElement.ValueKind != JsonValueKind.Null;
            if (!hasMore) break;
            page++;
        }
        return ids;
    }

    public async Task AddAssetToAlbumAsync(string albumId, string assetId, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync($"albums/{albumId}/assets", new { ids = new[] { assetId } }, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ImmichApiException($"PUT /albums/{albumId}/assets epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);
    }

    public async Task<string> EnsureAlbumAsync(string albumName, CancellationToken ct = default)
    {
        var albums = await GetAlbumsAsync(ct);
        var existing = albums.FirstOrDefault(a => string.Equals(a.AlbumName, albumName, StringComparison.Ordinal));
        if (existing is not null) return existing.Id;
        return await CreateAlbumAsync(albumName, ct);
    }

    public async Task<ServerStorageStats> GetStorageStatsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("server/storage", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ImmichApiException($"GET /server/storage epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);

        var result = JsonSerializer.Deserialize<ServerStorageStats>(body, JsonOptions);
        if (result is null) throw new ImmichApiException("GET /server/storage: tyhja vastaus palvelimelta");
        return result;
    }

    /// <summary>Pages through the whole library (1-based page numbers). Immich replaced the old
    /// GET /assets listing with POST /search/metadata at some point - verified against a live
    /// v3.1.0 server, where GET /assets now 404s but this endpoint works.</summary>
    public async Task<(IReadOnlyList<AssetSummary> Items, bool HasMore)> GetAssetsPageAsync(int page, int pageSize, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("search/metadata", new { page, size = pageSize }, JsonOptions, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ImmichApiException($"POST /search/metadata epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var assetsElement = doc.RootElement.GetProperty("assets");
        var items = JsonSerializer.Deserialize<List<AssetSummary>>(assetsElement.GetProperty("items").GetRawText(), JsonOptions) ?? new List<AssetSummary>();
        var hasMore = assetsElement.TryGetProperty("nextPage", out var nextPageEl) && nextPageEl.ValueKind != JsonValueKind.Null;
        return (items, hasMore);
    }

    public Task DownloadOriginalAsync(string assetId, string destinationPath, CancellationToken ct = default) =>
        DownloadAssetFileAsync($"assets/{assetId}/original", destinationPath, ct);

    public Task DownloadThumbnailAsync(string assetId, string destinationPath, CancellationToken ct = default) =>
        DownloadAssetFileAsync($"assets/{assetId}/thumbnail", destinationPath, ct);

    /// <summary>In-memory variant used only to build the small activity-panel preview image for
    /// videos, whose downloaded original obviously can't be decoded as a still frame - Immich's
    /// thumbnail endpoint returns a real preview image for video assets too, we just don't want
    /// it as the synced file itself (see PhotoSyncService's effectiveMode).</summary>
    public async Task<byte[]> DownloadThumbnailBytesAsync(string assetId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"assets/{assetId}/thumbnail", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ImmichApiException($"GET /assets/{assetId}/thumbnail epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);
        }
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task DownloadAssetFileAsync(string requestUri, string destinationPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ImmichApiException($"GET /{requestUri} epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);
        }

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        await httpStream.CopyToAsync(fileStream, ct);
    }

    /// <summary>Moves assets to Immich's trash (recoverable). Never passes force=true, which would
    /// permanently delete them - that's deliberately not exposed here.</summary>
    public async Task TrashAssetsAsync(IEnumerable<string> assetIds, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "assets")
        {
            Content = JsonContent.Create(new { ids = assetIds.ToArray() }, options: JsonOptions),
        };
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ImmichApiException($"DELETE /assets epaonnistui ({(int)response.StatusCode} {response.StatusCode}): {body}", response.StatusCode);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }

    /// <summary>Wraps a seekable file stream to report cumulative bytes read while HttpClient streams the upload body.</summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _totalBytes;
        private readonly Action<long, long> _onProgress;
        private long _bytesRead;

        public ProgressStream(Stream inner, long totalBytes, Action<long, long> onProgress)
        {
            _inner = inner;
            _totalBytes = totalBytes;
            _onProgress = onProgress;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0) Report(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            if (read > 0) Report(read);
            return read;
        }

        private void Report(int read)
        {
            _bytesRead += read;
            _onProgress(_bytesRead, _totalBytes);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
