using System.Text.Json;

namespace ImmichUploaderApp.Services;

/// LocalPaths can hold more than one entry when organizing by album and an asset belongs to
/// several albums (it gets mirrored into each album's folder).
public sealed record SyncManifestEntry(List<string> LocalPaths, string Mode, DateTime DownloadedAtUtc);

/// Persists which Immich assets have already been synced to disk (and where, and in which mode),
/// so PhotoSyncService can detect new/changed/removed remote assets and locally-deleted files
/// without re-fetching bytes on every scan. Mirrors UploadHistoryStore's storage pattern.
public sealed class SyncManifestStore
{
    private readonly Dictionary<string, SyncManifestEntry> _entries = new();
    private readonly object _lock = new();

    public SyncManifestStore() => Load();

    public bool TryGet(string assetId, out SyncManifestEntry entry)
    {
        lock (_lock) return _entries.TryGetValue(assetId, out entry!);
    }

    public void Set(string assetId, SyncManifestEntry entry)
    {
        lock (_lock) { _entries[assetId] = entry; SaveLocked(); }
    }

    public void Remove(string assetId)
    {
        lock (_lock) { if (_entries.Remove(assetId)) SaveLocked(); }
    }

    public List<KeyValuePair<string, SyncManifestEntry>> GetAll()
    {
        lock (_lock) return _entries.ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(ConfigService.SyncManifestPath)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, SyncManifestEntry>>(File.ReadAllText(ConfigService.SyncManifestPath));
            if (data is null) return;
            foreach (var (assetId, entry) in data) _entries[assetId] = entry;
        }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: synkronointimanifestin luku epaonnistui: {ex.Message}"); }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigService.SyncManifestPath)!);
        var tempPath = ConfigService.SyncManifestPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_entries));
        File.Move(tempPath, ConfigService.SyncManifestPath, overwrite: true);
    }
}
