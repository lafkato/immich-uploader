using System.Security.Cryptography;
using System.Text.Json;

namespace ImmichUploaderApp.Services;

public sealed class UploadHistoryStore
{
    private readonly HashSet<string> _uploadedHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingAlbumEntry> _pendingAlbumEntries = new();
    private readonly object _lock = new();

    public UploadHistoryStore() => Load();

    public static async Task<string> ComputeSha1Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);
        using var sha1 = SHA1.Create();
        return Convert.ToHexString(await sha1.ComputeHashAsync(stream, ct)).ToLowerInvariant();
    }

    public bool TryGetUploadedHash(string filePath, FileInfo info, out string hash)
    {
        lock (_lock)
        {
            if (_files.TryGetValue(filePath, out var entry) && entry.Length == info.Length && entry.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            {
                hash = entry.Hash;
                return _uploadedHashes.Contains(hash);
            }
            hash = string.Empty;
            return false;
        }
    }

    public bool IsUploaded(string hash)
    {
        lock (_lock) return _uploadedHashes.Contains(hash);
    }

    public void MarkUploaded(string hash, string filePath, FileInfo info)
    {
        lock (_lock)
        {
            _uploadedHashes.Add(hash);
            _files[filePath] = new FileEntry(hash, info.Length, info.LastWriteTimeUtc.Ticks);
            SaveLocked();
        }
    }

    public void MarkKnownFile(string hash, string filePath, FileInfo info)
    {
        lock (_lock)
        {
            _files[filePath] = new FileEntry(hash, info.Length, info.LastWriteTimeUtc.Ticks);
            SaveLocked();
        }
    }

    public void AddPendingAlbum(string albumId, string assetId, string filePath)
    {
        lock (_lock)
        {
            if (_pendingAlbumEntries.Any(x => x.AlbumId == albumId && x.AssetId == assetId)) return;
            _pendingAlbumEntries.Add(new PendingAlbumEntry(albumId, assetId, filePath));
            SaveLocked();
        }
    }

    public IReadOnlyList<PendingAlbumEntry> GetPendingAlbums()
    {
        lock (_lock) return _pendingAlbumEntries.ToList();
    }

    public void RemovePendingAlbum(PendingAlbumEntry entry)
    {
        lock (_lock)
        {
            _pendingAlbumEntries.RemoveAll(x => x.AlbumId == entry.AlbumId && x.AssetId == entry.AssetId);
            SaveLocked();
        }
    }

    public int Count { get { lock (_lock) return _uploadedHashes.Count; } }

    private void Load()
    {
        try
        {
            if (File.Exists(ConfigService.UploadHistoryPath))
            {
                var data = JsonSerializer.Deserialize<HistoryData>(File.ReadAllText(ConfigService.UploadHistoryPath)) ?? new HistoryData();
                foreach (var hash in data.UploadedHashes) _uploadedHashes.Add(hash);
                foreach (var entry in data.Files) _files[entry.Path] = new FileEntry(entry.Hash, entry.Length, entry.LastWriteUtcTicks);
                _pendingAlbumEntries.AddRange(data.PendingAlbums);
                return;
            }

            // One-time migration of the old append-only log.
            if (File.Exists(ConfigService.UploadedLogPath))
                foreach (var line in File.ReadLines(ConfigService.UploadedLogPath))
                {
                    var hash = line.Split('\t')[0].Trim();
                    if (hash.Length > 0) _uploadedHashes.Add(hash);
                }
            if (_uploadedHashes.Count > 0) lock (_lock) SaveLocked();
        }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: lataushistorian luku epaonnistui: {ex.Message}"); }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigService.UploadHistoryPath)!);
        var data = new HistoryData
        {
            UploadedHashes = _uploadedHashes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Files = _files.Select(x => new StoredFileEntry(x.Key, x.Value.Hash, x.Value.Length, x.Value.LastWriteUtcTicks)).ToList(),
            PendingAlbums = _pendingAlbumEntries.ToList(),
        };
        var tempPath = ConfigService.UploadHistoryPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(data));
        File.Move(tempPath, ConfigService.UploadHistoryPath, overwrite: true);
    }

    private sealed class HistoryData
    {
        public List<string> UploadedHashes { get; set; } = new();
        public List<StoredFileEntry> Files { get; set; } = new();
        public List<PendingAlbumEntry> PendingAlbums { get; set; } = new();
    }

    private sealed record FileEntry(string Hash, long Length, long LastWriteUtcTicks);
    private sealed record StoredFileEntry(string Path, string Hash, long Length, long LastWriteUtcTicks);
}

public sealed record PendingAlbumEntry(string AlbumId, string AssetId, string FilePath);
