using System.Globalization;
using ImmichUploaderApp.Models;
using Timer = System.Threading.Timer;

namespace ImmichUploaderApp.Services;

/// Mirrors Immich assets down to a local folder - the reverse direction of UploadWatcherService.
/// Pull-only and timer-driven (no FileSystemWatcher side, since there's nothing local to watch):
/// each tick pages through every remote asset, downloads what's missing/stale, and reconciles
/// deletions in both directions. See Services/UploadWatcherService.cs for the sibling upload path.
public sealed class PhotoSyncService : IDisposable
{
    private const int PageSize = 500;
    private const int MaxRecentDownloads = 25;
    private const int MaxRecentDeletions = 25;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);
    // Deliberately independent of, and much faster than, the full scan: a locally deleted file
    // shouldn't have to wait behind a slow album-membership-heavy scan to get trashed in Immich
    // too. See ReconcileLocalDeletionsAsync.
    private static readonly TimeSpan LocalDeletionCheckInterval = TimeSpan.FromMinutes(1);

    private static readonly Dictionary<string, CultureInfo> MonthCultures = new()
    {
        ["fi"] = new CultureInfo("fi-FI"),
        ["en"] = new CultureInfo("en-US"),
        ["sv"] = new CultureInfo("sv-SE"),
        ["de"] = new CultureInfo("de-DE"),
    };

    private readonly SyncManifestStore _manifest = new();
    private readonly UploadHistoryStore _uploadHistory;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _activityLock = new();
    private readonly List<RecentDownload> _recentDownloads = new();
    private readonly List<RecentDeletion> _recentDeletions = new();

    private Timer? _timer;
    private Timer? _localDeletionTimer;
    private CancellationTokenSource? _cts;
    private ImmichClient? _client;
    private AppConfig _config = new();
    private int _scanRunning;
    private int _localCheckRunning;
    private string _lastStatusText = Loc.T("status.stopped");
    private Dictionary<string, List<string>> _albumNamesByAssetId = new();

    /// uploadHistory is shared with UploadWatcherService when provided: every file downloaded
    /// here gets marked "known" in it, so it's never re-uploaded even if the sync and watched
    /// folders overlap - see UploadWatcherService's matching constructor comment.
    public PhotoSyncService(UploadHistoryStore? uploadHistory = null) => _uploadHistory = uploadHistory ?? new();

    public event Action<PhotoSyncActivitySnapshot>? ActivityChanged;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public PhotoSyncActivitySnapshot GetCurrentSnapshot()
    {
        lock (_activityLock) return new(_lastStatusText, _recentDownloads.ToList(), _recentDeletions.ToList());
    }

    private void RaiseActivity(string statusText)
    {
        PhotoSyncActivitySnapshot snapshot;
        lock (_activityLock)
        {
            _lastStatusText = statusText;
            snapshot = new PhotoSyncActivitySnapshot(statusText, _recentDownloads.ToList(), _recentDeletions.ToList());
        }
        ActivityChanged?.Invoke(snapshot);
    }

    private void RecordDeletion(string fileName, string reason)
    {
        lock (_activityLock)
        {
            _recentDeletions.Insert(0, new RecentDeletion(fileName, DateTime.Now, reason));
            if (_recentDeletions.Count > MaxRecentDeletions) _recentDeletions.RemoveRange(MaxRecentDeletions, _recentDeletions.Count - MaxRecentDeletions);
        }
    }

    public async Task StartAsync(AppConfig config)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _config = config;
            if (!config.SyncEnabled) return;
            if (string.IsNullOrWhiteSpace(config.SyncPhotoFolder) && string.IsNullOrWhiteSpace(config.SyncVideoFolder)) return;

            _cts = new CancellationTokenSource();
            _client = new ImmichClient(config.ServerUrl, config.ApiKey);
            _timer = new Timer(_ => _ = ScanTickAsync(), null, TimeSpan.Zero, ScanInterval);
            // Staggered start (not TimeSpan.Zero) so it doesn't immediately duplicate work the
            // full scan's own first tick is already about to do.
            _localDeletionTimer = new Timer(_ => _ = LocalDeletionCheckTickAsync(), null, LocalDeletionCheckInterval, LocalDeletionCheckInterval);
            RaiseActivity(Loc.T("status.idle"));
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycleGate.Release(); }
    }

    private Task StopCoreAsync()
    {
        _timer?.Dispose(); _timer = null;
        _localDeletionTimer?.Dispose(); _localDeletionTimer = null;
        _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        _client?.Dispose(); _client = null;
        return Task.CompletedTask;
    }

    public void ScanNow() => _ = ScanTickAsync();

    private async Task LocalDeletionCheckTickAsync()
    {
        if (!IsRunning || _client is null || Interlocked.CompareExchange(ref _localCheckRunning, 1, 0) != 0) return;
        try { await ReconcileLocalDeletionsAsync(_cts!.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Log($"VIRHE paikallisten poistojen tarkistuksessa: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _localCheckRunning, 0); }
    }

    private async Task ScanTickAsync()
    {
        if (!IsRunning || _client is null || Interlocked.CompareExchange(ref _scanRunning, 1, 0) != 0) return;
        var ct = _cts!.Token;
        try
        {
            RaiseActivity(Loc.T("sync.scanning"));
            _albumNamesByAssetId = _config.SyncOrganizeByAlbum ? await BuildAlbumMembershipAsync(ct) : new Dictionary<string, List<string>>();

            var remoteIds = new HashSet<string>();
            int pageNumber = 1, downloaded = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var (items, hasMore) = await _client.GetAssetsPageAsync(pageNumber, PageSize, ct);

                foreach (var asset in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (asset.Visibility == "hidden")
                    {
                        // iPhone Live Photo motion clips (and any other hidden companion asset)
                        // come back as their own VIDEO entry, but Immich itself keeps them out of
                        // the main library view - mirror that instead of dropping a stray ~2s
                        // clip into the video folder for every Live Photo. Leaving the id out of
                        // remoteIds also lets ReconcileRemoteDeletions clean up anything a previous
                        // scan already downloaded for it.
                        continue;
                    }
                    remoteIds.Add(asset.Id);
                    try
                    {
                        if (await SyncAssetAsync(asset, ct)) downloaded++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // One asset failing (e.g. its thumbnail not generated yet server-side)
                        // shouldn't abort the whole scan - same tolerance as the upload side's
                        // per-file handling in UploadWatcherService.
                        AppLogger.Log($"VAROITUS: kuvan '{asset.OriginalFileName}' synkronointi epaonnistui: {ex.Message}");
                    }
                }

                if (!hasMore || items.Count == 0) break;
                pageNumber++;
            }

            ReconcileRemoteDeletions(remoteIds);
            AppLogger.Log($"Kuvasynkronointi valmis: {downloaded} uutta/paivitettya tiedostoa, {remoteIds.Count} kuvaa Immichissa.");
            RaiseActivity(Loc.T("sync.idleWithCount", remoteIds.Count));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Log($"VIRHE kuvasynkronoinnissa: {ex.Message}");
            RaiseActivity(Loc.T("sync.scanFailed", ex.Message));
        }
        finally { Interlocked.Exchange(ref _scanRunning, 0); }
    }

    /// Builds an assetId -> album names lookup by fetching every album's full asset list. Only
    /// done when organizing by album, since it's an extra round trip per album on top of the
    /// regular asset paging. A failure here just falls back to no album info for this scan
    /// (assets land in the "no album" folder) rather than aborting the whole sync.
    private async Task<Dictionary<string, List<string>>> BuildAlbumMembershipAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, List<string>>();
        IReadOnlyList<AlbumSummary> albums;
        try { albums = await _client!.GetAlbumsAsync(ct); }
        catch (Exception ex)
        {
            AppLogger.Log($"VAROITUS: albumien haku epaonnistui: {ex.Message}");
            return map;
        }

        foreach (var album in albums)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var assetId in await _client!.GetAlbumAssetIdsAsync(album.Id, ct))
                {
                    if (!map.TryGetValue(assetId, out var names)) map[assetId] = names = new List<string>();
                    names.Add(album.AlbumName);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"VAROITUS: albumin '{album.AlbumName}' sisallon haku epaonnistui: {ex.Message}");
            }
        }
        return map;
    }

    /// Returns true if at least one new local copy was downloaded/placed for this asset.
    private async Task<bool> SyncAssetAsync(AssetSummary asset, CancellationToken ct)
    {
        var wantedPaths = BuildDestinationPaths(asset);
        if (wantedPaths.Count == 0) return false; // no folder configured for this asset's type

        // Immich's "thumbnail" is always a static preview image (webp), even for videos - there's
        // no such thing as a lightweight video. Downloading a video "thumbnail" would silently
        // hand back a still image with a video's filename, which is exactly backwards from what
        // the Kevyt/Original toggle is supposed to mean. So videos always sync at full size;
        // the toggle only meaningfully applies to photos.
        var effectiveMode = asset.Type == "VIDEO" ? "Original" : _config.SyncMode;

        var hasEntry = _manifest.TryGet(asset.Id, out var entry);
        var previousPaths = hasEntry ? entry.LocalPaths : new List<string>();

        if (hasEntry && entry.Mode != effectiveMode)
        {
            // Mode switched (Thumbnail <-> Original) - every existing copy is stale regardless
            // of location, so drop them all and treat this as a fresh asset below. Removing the
            // manifest entry immediately (not after the fresh download completes) matters now
            // that ReconcileLocalDeletionsAsync runs on its own fast timer, independent of this
            // scan: without it, there'd be a window where the manifest still claims these
            // now-deleted paths should exist, which that independent check would misread as the
            // user having deleted the file and (if enabled) wrongly trash the asset in Immich.
            foreach (var stale in previousPaths) TryDeleteFile(stale);
            previousPaths = new List<string>();
            _manifest.Remove(asset.Id);
        }
        else
        {
            // If the user deleted a copy we previously placed somewhere we still want it, that's
            // a deletion signal, not something to silently undo by re-downloading - otherwise a
            // locally deleted file would just reappear before ReconcileLocalDeletionsAsync's own,
            // independent timer ever saw it gone. Leave the whole asset alone this scan;
            // reconciliation decides what happens next (keep remaining copies, or trash remotely
            // if none are left).
            var stillWantedButMissing = previousPaths
                .Intersect(wantedPaths, StringComparer.OrdinalIgnoreCase)
                .Any(p => !File.Exists(p));
            if (stillWantedButMissing) return false;
        }

        var survivingPaths = previousPaths.Where(File.Exists).ToList();
        var newPaths = wantedPaths.Where(p => !survivingPaths.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        if (newPaths.Count == 0)
        {
            if (survivingPaths.Count != previousPaths.Count)
                _manifest.Set(asset.Id, new SyncManifestEntry(survivingPaths, effectiveMode, entry?.DownloadedAtUtc ?? DateTime.UtcNow));
            return false;
        }

        var firstPath = newPaths[0];
        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);

        // Download under a temp extension UploadWatcherService's IsCandidate filter never matches
        // (it only enqueues known photo/video extensions), hash it and register the hash as a
        // known upload, and only THEN rename into the real, watched path. Doing the download and
        // the final-name placement in the opposite order leaves a real window - not just a
        // testing artifact, this was hit live during this session - where the watcher can see and
        // re-upload the freshly synced file as a brand new asset before its hash is recorded,
        // creating a duplicate on the server. Overlapping sync/watch folders are only actually
        // safe with this ordering.
        var tempPath = firstPath + ".immichsync-tmp";
        if (effectiveMode == "Original") await _client!.DownloadOriginalAsync(asset.Id, tempPath, ct);
        else await _client!.DownloadThumbnailAsync(asset.Id, tempPath, ct);
        await MarkAsKnownUploadAsync(tempPath, ct);
        File.Move(tempPath, firstPath, overwrite: true);

        var finalPaths = new List<string>(survivingPaths) { firstPath };
        foreach (var extra in newPaths.Skip(1))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(extra)!);
                File.Copy(firstPath, extra, overwrite: true);
                finalPaths.Add(extra);
            }
            catch (Exception ex) { AppLogger.Log($"VAROITUS: kopiointi '{extra}' epaonnistui: {ex.Message}"); }
        }

        _manifest.Set(asset.Id, new SyncManifestEntry(finalPaths, effectiveMode, DateTime.UtcNow));
        await RecordDownloadAsync(firstPath, asset, ct);
        return true;
    }

    /// Registers the just-downloaded file in the shared upload-history store so
    /// UploadWatcherService recognizes it as already handled and never re-uploads it, even if
    /// the sync and watched folders overlap - this is what makes that overlap actually safe,
    /// not just the Settings-dialog warning.
    private async Task MarkAsKnownUploadAsync(string path, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            var sha1 = await UploadHistoryStore.ComputeSha1Async(path, ct);
            _uploadHistory.MarkUploaded(sha1, path, info);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"VAROITUS: tiedoston '{path}' merkitseminen tunnetuksi epaonnistui: {ex.Message}");
        }
    }

    /// Videos can't be decoded into a still-frame preview from the downloaded file itself (it's
    /// the original video container), so their activity-panel thumbnail is instead fetched
    /// on-demand from Immich's own thumbnail endpoint - purely for display, never written to
    /// disk. Photos decode their already-downloaded file directly (cheap, no extra request).
    private async Task RecordDownloadAsync(string path, AssetSummary asset, CancellationToken ct)
    {
        var info = new FileInfo(path);
        byte[]? thumbnail;
        if (asset.Type == "VIDEO")
        {
            try { thumbnail = ThumbnailImaging.TryCreate(await _client!.DownloadThumbnailBytesAsync(asset.Id, ct)); }
            catch (Exception ex)
            {
                AppLogger.Log($"VAROITUS: esikatselukuvan haku epaonnistui '{asset.OriginalFileName}': {ex.Message}");
                thumbnail = null;
            }
        }
        else
        {
            // Guards against more than just a decode failure inside TryCreate's own try/catch: if
            // the imaging library's assembly can't be loaded at all (reproduced live - a broken
            // publish was missing SixLabors.ImageSharp.dll), the CLR throws at this call site,
            // before TryCreate's own body ever runs. Without this, that exception propagated all
            // the way out of SyncAssetAsync - the file was already downloaded and the manifest
            // already updated by that point, but the asset never made it into "recent downloads",
            // and every single photo hit this on every scan, drowning the log and slowing scans.
            try { thumbnail = ThumbnailImaging.TryCreate(path); }
            catch (Exception ex)
            {
                AppLogger.Log($"VAROITUS: esikatselukuvan luonti epaonnistui '{path}': {ex.Message}");
                thumbnail = null;
            }
        }

        lock (_activityLock)
        {
            _recentDownloads.Insert(0, new RecentDownload(Path.GetFileName(path), DateTime.Now, info.Exists ? info.Length : 0, thumbnail));
            if (_recentDownloads.Count > MaxRecentDownloads) _recentDownloads.RemoveRange(MaxRecentDownloads, _recentDownloads.Count - MaxRecentDownloads);
        }
    }

    /// Photos and videos go to their own independently configured root folders, then either a
    /// "yyyy/MM Kuukausi"-style date folder or one folder per album the asset belongs to
    /// (mirrored into each if it's in several; a "no album" folder if it's in none).
    private List<string> BuildDestinationPaths(AssetSummary asset)
    {
        var root = asset.Type == "VIDEO" ? _config.SyncVideoFolder : _config.SyncPhotoFolder;
        if (string.IsNullOrWhiteSpace(root)) return new List<string>();

        // OriginalFileName is server-supplied metadata, not something this app controls - it can
        // come from a shared album contributed by another Immich user, or any client that never
        // validated what it sent. SanitizeFileName strips path separators and rejects a bare "."
        // or ".." so Path.Combine below can never resolve outside the configured sync folder.
        var baseName = SanitizeFileName(asset.OriginalFileName, fallback: asset.Id);
        // Immich's thumbnail endpoint always returns image/webp regardless of the original file
        // type (verified against a live server) - keep the extension honest so Explorer and image
        // viewers open it correctly, instead of e.g. a .MOV filename holding webp image bytes.
        // Videos always sync as originals (see SyncAssetAsync's effectiveMode), so this only
        // actually changes the extension for photos in Thumbnail mode.
        var usesThumbnail = asset.Type != "VIDEO" && _config.SyncMode != "Original";
        var fileName = usesThumbnail ? Path.ChangeExtension(baseName, ".webp") : baseName;

        List<string> subFolders;
        if (_config.SyncOrganizeByAlbum)
        {
            subFolders = _albumNamesByAssetId.TryGetValue(asset.Id, out var albums) && albums.Count > 0
                ? albums.Select(SanitizeFolderName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { Loc.T("sync.noAlbum") };
        }
        else
        {
            subFolders = new List<string> { Path.Combine(asset.FileCreatedAt.ToString("yyyy"), GetMonthFolderName(asset.FileCreatedAt.Month)) };
        }

        var paths = new List<string>();
        foreach (var subFolder in subFolders)
        {
            var folder = Path.Combine(root, subFolder);
            var path = Path.Combine(folder, fileName);

            // Collision: a different asset already occupies this exact path (e.g. same camera
            // filename on the same day/album) - disambiguate with a short id suffix.
            if (File.Exists(path) && !IsSameAssetAtPath(asset.Id, path))
            {
                var ext = Path.GetExtension(fileName);
                var stem = Path.GetFileNameWithoutExtension(fileName);
                path = Path.Combine(folder, $"{stem}_{asset.Id[..8]}{ext}");
            }
            paths.Add(path);
        }
        return paths;
    }

    private bool IsSameAssetAtPath(string assetId, string path) =>
        _manifest.TryGet(assetId, out var entry) && entry.LocalPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

    private string GetMonthFolderName(int month)
    {
        var culture = MonthCultures.TryGetValue(_config.Language, out var c) ? c : CultureInfo.InvariantCulture;
        var name = culture.DateTimeFormat.GetMonthName(month);
        if (name.Length > 0) name = char.ToUpper(name[0], culture) + name[1..];
        return $"{month:D2} {name}".TrimEnd();
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        // GetInvalidFileNameChars() already strips \ and / (so multi-segment traversal like
        // "..\..\evil" collapses into a harmless single segment), but a name that sanitizes down
        // to exactly "." or ".." is still a valid, meaningful path segment to Path.Combine and
        // would walk one directory level up/stay put instead of creating a real subfolder.
        return sanitized.Length == 0 || sanitized is "." or ".." ? Loc.T("sync.noAlbum") : sanitized;
    }

    /// Same traversal concern as SanitizeFolderName, applied to the actual downloaded file's name.
    private static string SanitizeFileName(string name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return sanitized.Length == 0 || sanitized is "." or ".." ? fallback : sanitized;
    }

    /// Handles only the "deleted on the Immich side" direction - needs to know the current
    /// remote asset list, so it's necessarily tied to the full scan that just paged it. The other
    /// direction (a local delete triggering a remote trash) is handled independently by
    /// ReconcileLocalDeletionsAsync, on its own fast timer - see StartAsync.
    private void ReconcileRemoteDeletions(HashSet<string> remoteIds)
    {
        foreach (var (assetId, entry) in _manifest.GetAll())
        {
            if (remoteIds.Contains(assetId)) continue;

            // No longer on the Immich side (deleted there, or trashed by us on a previous scan)
            // - drop every local copy too.
            foreach (var path in entry.LocalPaths)
            {
                TryDeleteFile(path);
                RecordDeletion(Path.GetFileName(path), Loc.T("sync.deletedFromImmich"));
            }
            _manifest.Remove(assetId);
        }
    }

    /// Local-delete -> remote-trash detection, split out from the main scan and run on its own
    /// fast timer (see StartAsync) so it isn't stuck waiting behind the slow, album-membership-
    /// heavy full scan - a user deleting a file locally shouldn't have to wait up to 15 minutes to
    /// see it trashed in Immich too. Purely a local file-existence check against the manifest, no
    /// network calls except the trash request itself, so it's cheap enough to run every minute.
    private async Task ReconcileLocalDeletionsAsync(CancellationToken ct)
    {
        var toTrashRemotely = new List<(string AssetId, string FileName)>();
        foreach (var (assetId, entry) in _manifest.GetAll())
        {
            var survivingPaths = entry.LocalPaths.Where(File.Exists).ToList();
            if (survivingPaths.Count == entry.LocalPaths.Count) continue; // nothing missing

            if (survivingPaths.Count == 0)
            {
                // The user deleted every mirrored copy of this asset.
                _manifest.Remove(assetId);
                var fileName = Path.GetFileName(entry.LocalPaths[0]);
                if (_config.SyncDeleteRemoteOnLocalDelete) toTrashRemotely.Add((assetId, fileName));
                else RecordDeletion(fileName, Loc.T("sync.deletedLocallyOnly"));
            }
            else
            {
                // Deleted from some locations (e.g. one album folder) but not others - keep
                // tracking what's left, don't resurrect the deleted copy, don't trash remotely
                // since a copy still exists.
                _manifest.Set(assetId, entry with { LocalPaths = survivingPaths });
            }
        }

        if (toTrashRemotely.Count == 0) return;
        try
        {
            await _client!.TrashAssetsAsync(toTrashRemotely.Select(x => x.AssetId), ct);
            foreach (var (_, fileName) in toTrashRemotely) RecordDeletion(fileName, Loc.T("sync.deletedLocallyAndTrashed"));
        }
        catch (Exception ex)
        {
            AppLogger.Log($"VAROITUS: roskakoriin siirto epaonnistui: {ex.Message}");
            foreach (var (_, fileName) in toTrashRemotely) RecordDeletion(fileName, Loc.T("sync.deletedLocallyTrashFailed"));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: tiedoston '{path}' poisto epaonnistui: {ex.Message}"); }
    }

    public void Dispose() { StopAsync().GetAwaiter().GetResult(); _lifecycleGate.Dispose(); }
}
