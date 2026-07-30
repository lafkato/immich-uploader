using System.Threading.Channels;
using ImmichUploaderApp.Models;
using Timer = System.Threading.Timer;

namespace ImmichUploaderApp.Services;

public sealed class UploadWatcherService : IDisposable
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tiff", ".webp", ".dng", ".cr2", ".nef", ".arw",
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".3gp",
    };

    private const int QueueCapacity = 500;
    private const int MaxRetryAttempts = 6;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SafetyPollInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StableFileDelay = TimeSpan.FromSeconds(3);
    private const int MaxRecentUploads = 25;
    private const int MaxRecentFailures = 10;

    private readonly UploadHistoryStore _history;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _retryAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueLock = new();
    private readonly object _debounceLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _activityLock = new();
    private readonly List<RecentUpload> _recentUploads = new();
    private readonly List<RecentFailure> _recentFailures = new();

    private Channel<string>? _queue;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private Timer? _safetyPollTimer;
    private AppConfig _config = new();
    private ImmichClient? _client;
    private string? _albumId;
    private volatile bool _paused;
    private int _safetyScanRunning;
    private string? _currentFileName;
    private double? _currentFileProgressPercent;
    private string _lastStatusText = Loc.T("status.stopped");

    /// history is shared with PhotoSyncService when provided, so files it downloads (marked
    /// known there) are never picked up here as "new" local files and re-uploaded - the same
    /// store, not just the same schema, is what makes that safe.
    public UploadWatcherService(UploadHistoryStore? history = null) => _history = history ?? new();

    public event Action<WatcherActivitySnapshot>? ActivityChanged;
    public bool IsRunning => _cts is { IsCancellationRequested: false };
    public bool IsPaused => _paused;
    public int UploadedCount => _history.Count;

    public WatcherActivitySnapshot GetCurrentSnapshot()
    {
        lock (_activityLock)
            return new(_lastStatusText, _currentFileName, _currentFileProgressPercent, GetQueueCount(), _recentUploads.ToList(), _recentFailures.ToList());
    }

    private void RaiseActivity(string statusText)
    {
        WatcherActivitySnapshot snapshot;
        lock (_activityLock)
        {
            _lastStatusText = statusText;
            snapshot = new WatcherActivitySnapshot(statusText, _currentFileName, _currentFileProgressPercent,
                GetQueueCount(), _recentUploads.ToList(), _recentFailures.ToList());
        }
        ActivityChanged?.Invoke(snapshot);
    }

    public async Task<ServerStorageStats?> TryGetStorageStatsAsync(CancellationToken ct)
    {
        if (_client is null) return null;
        try { return await _client.GetStorageStatsAsync(ct); }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: tallennustilan haku epaonnistui: {ex.Message}"); return null; }
    }

    public async Task StartAsync(AppConfig config)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _config = config;
            _cts = new CancellationTokenSource();
            _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
            _paused = false;
            _client = new ImmichClient(config.ServerUrl, config.ApiKey);

            try { _albumId = string.IsNullOrWhiteSpace(config.AlbumName) ? null : await _client.EnsureAlbumAsync(config.AlbumName, _cts.Token); }
            catch (Exception ex) { _albumId = null; AppLogger.Log($"VAROITUS: albumin haku/luonti epaonnistui: {ex.Message}"); }

            foreach (var dir in config.Directories) TryEnsureWatcher(dir);
            _consumerTask = Task.Run(() => ConsumeQueueAsync(_queue.Reader, _cts.Token));
            _safetyPollTimer = new Timer(_ => _ = SafetyPollTickAsync(), null, TimeSpan.Zero, SafetyPollInterval);
            _ = RetryPendingAlbumsAsync(_cts.Token);
            AppLogger.Log($"=== Kaynnistys === Tarkkaillaan: {string.Join(", ", config.Directories)}");
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

    private async Task StopCoreAsync()
    {
        _safetyPollTimer?.Dispose(); _safetyPollTimer = null;
        lock (_debounceLock) { foreach (var timer in _debounceTimers.Values) timer.Dispose(); _debounceTimers.Clear(); }
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
        _queue?.Writer.TryComplete();
        if (_cts is not null)
        {
            _cts.Cancel();
            if (_consumerTask is not null) try { await _consumerTask; } catch (OperationCanceledException) { }
            _cts.Dispose();
        }
        _cts = null; _consumerTask = null; _queue = null;
        lock (_queueLock) { _pendingPaths.Clear(); _retryAttempts.Clear(); }
        _client?.Dispose(); _client = null; _albumId = null;
    }

    public void Pause() { _paused = true; RaiseActivity(Loc.T("status.paused")); AppLogger.Log("Lataukset keskeytetty."); }
    public void Resume() { _paused = false; RaiseActivity(Loc.T("status.idle")); _ = SafetyPollTickAsync(); AppLogger.Log("Lataukset jatkuvat."); }
    public void ScanNow() => _ = SafetyPollTickAsync();

    private void TryEnsureWatcher(string dir)
    {
        if (!IsRunning || _watchers.ContainsKey(dir) || !Directory.Exists(dir)) return;
        try
        {
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024, EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => OnFileEvent(e.FullPath);
            watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
            watcher.Error += (_, e) => { AppLogger.Log($"VAROITUS: FileSystemWatcher '{dir}' menetti tapahtumia: {e.GetException()?.Message}"); RecordFailure(dir, "Kansiotarkkailu palautetaan täysskannauksella.", true); ScanNow(); };
            _watchers[dir] = watcher;
        }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: kansion '{dir}' tarkkailu epaonnistui: {ex.Message}"); }
    }

    private void OnFileEvent(string fullPath)
    {
        if (_paused || !IsRunning) return;
        lock (_debounceLock)
        {
            if (_debounceTimers.TryGetValue(fullPath, out var existing)) { existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan); return; }
            _debounceTimers[fullPath] = new Timer(_ => OnDebounceElapsed(fullPath), null, DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(string fullPath)
    {
        lock (_debounceLock) if (_debounceTimers.Remove(fullPath, out var timer)) timer.Dispose();
        Enqueue(fullPath);
    }

    private void Enqueue(string fullPath)
    {
        if (!IsRunning || !IsCandidate(fullPath) || _paused) return;
        var queue = _queue;
        if (queue is null) return;
        lock (_queueLock)
        {
            if (!_pendingPaths.Add(fullPath)) return;
            if (!queue.Writer.TryWrite(fullPath))
            {
                _pendingPaths.Remove(fullPath);
                AppLogger.Log($"VAROITUS: latausjono on täynnä, '{fullPath}' tarkistetaan seuraavassa skannauksessa.");
            }
        }
        RaiseActivity(Loc.T("status.queued", GetQueueCount()));
    }

    private bool IsCandidate(string fullPath) => AllowedExtensions.Contains(Path.GetExtension(fullPath)) && !IsExcluded(fullPath);
    private bool IsExcluded(string fullPath)
    {
        foreach (var exclude in _config.ExcludeDirectories)
        {
            var normalized = exclude.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!string.IsNullOrWhiteSpace(exclude) && fullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private async Task SafetyPollTickAsync()
    {
        if (_paused || !IsRunning || Interlocked.CompareExchange(ref _safetyScanRunning, 1, 0) != 0) return;
        var ct = _cts!.Token;
        try
        {
            foreach (var dir in _config.Directories)
            {
                ct.ThrowIfCancellationRequested(); TryEnsureWatcher(dir);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in EnumerateFilesPruningExcluded(dir)) { ct.ThrowIfCancellationRequested(); if (IsCandidate(file)) Enqueue(file); }
            }
            await RetryPendingAlbumsAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLogger.Log($"VIRHE turvapollauksessa: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _safetyScanRunning, 0); }
    }

    private IEnumerable<string> EnumerateFilesPruningExcluded(string root)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.TryPop(out var current))
        {
            IEnumerable<string> subDirs; IEnumerable<string> files;
            try { subDirs = Directory.EnumerateDirectories(current); files = Directory.EnumerateFiles(current); }
            catch { continue; }
            foreach (var file in files) yield return file;
            foreach (var subDir in subDirs) if (!IsExcluded(subDir + Path.DirectorySeparatorChar)) stack.Push(subDir);
        }
    }

    private async Task ConsumeQueueAsync(ChannelReader<string> reader, CancellationToken ct)
    {
        await foreach (var path in reader.ReadAllAsync(ct))
        {
            try { await ProcessFileAsync(path, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { HandleProcessingFailure(path, ex, ct); }
            finally { lock (_queueLock) _pendingPaths.Remove(path); }
        }
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        if (_client is null || !File.Exists(path) || _paused) return;
        var info = await WaitForStableFileAsync(path, ct);
        if (_history.TryGetUploadedHash(path, info, out _)) return;
        var sha1 = await UploadHistoryStore.ComputeSha1Async(path, ct);
        info.Refresh();
        if (!info.Exists || info.Length == 0) throw new FileNotReadyException();
        if (_history.IsUploaded(sha1)) { _history.MarkKnownFile(sha1, path, info); return; }
        var fileName = Path.GetFileName(path);
        lock (_activityLock) { _currentFileName = fileName; _currentFileProgressPercent = 0; }
        RaiseActivity(Loc.T("status.uploading", fileName));
        var lastRaisedPercent = -10d;
        var response = await _client.UploadAssetAsync(path, _config.DeviceId, sha1, (sent, total) =>
        {
            if (total <= 0) return;
            var percent = Math.Round(sent * 100d / total, 1);
            lock (_activityLock) _currentFileProgressPercent = percent;
            if (percent - lastRaisedPercent >= 5 || percent >= 100) { lastRaisedPercent = percent; RaiseActivity(Loc.T("status.uploading", fileName)); }
        }, ct);
        info.Refresh(); _history.MarkUploaded(sha1, path, info);
        if (_albumId is not null)
        {
            try { await _client.AddAssetToAlbumAsync(_albumId, response.Id, ct); }
            catch (Exception ex) { _history.AddPendingAlbum(_albumId, response.Id, path); RecordFailure(path, "Albumiin lisäys epäonnistui; yritetään uudelleen. " + SafeMessage(ex), true); }
        }
        // Guards against more than just a decode failure inside TryCreate's own try/catch: if the
        // imaging library's assembly can't be loaded at all (reproduced live - a broken publish
        // was missing SixLabors.ImageSharp.dll), the CLR throws at this call site, before
        // TryCreate's own body ever runs. Without this, that exception aborted the whole upload
        // record and silently emptied the "recent uploads" list, even though the real upload to
        // Immich (above) had already succeeded.
        byte[]? thumbnail;
        try { thumbnail = ThumbnailImaging.TryCreate(path); }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: esikatselukuvan luonti epaonnistui '{path}': {ex.Message}"); thumbnail = null; }
        lock (_activityLock)
        {
            _currentFileName = null; _currentFileProgressPercent = null;
            _recentUploads.Insert(0, new RecentUpload(fileName, DateTime.Now, info.Length, thumbnail));
            if (_recentUploads.Count > MaxRecentUploads) _recentUploads.RemoveRange(MaxRecentUploads, _recentUploads.Count - MaxRecentUploads);
        }
        lock (_queueLock) _retryAttempts.Remove(path);
        AppLogger.Log($"LADATTU ({response.Status}): {path} -> {response.Id}");
        RaiseActivity(GetQueueCount() > 0 ? Loc.T("status.queued", GetQueueCount()) : Loc.T("status.idle"));
    }

    private async Task<FileInfo> WaitForStableFileAsync(string path, CancellationToken ct)
    {
        var first = new FileInfo(path);
        if (!first.Exists || first.Length == 0) throw new FileNotReadyException();
        var length = first.Length; var lastWrite = first.LastWriteTimeUtc;
        await Task.Delay(StableFileDelay, ct);
        var second = new FileInfo(path);
        if (!second.Exists || second.Length == 0 || second.Length != length || second.LastWriteTimeUtc != lastWrite) throw new FileNotReadyException();
        try { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, useAsync: true); _ = stream.Length; }
        catch (IOException) { throw new FileNotReadyException(); }
        return second;
    }

    private void HandleProcessingFailure(string path, Exception ex, CancellationToken ct)
    {
        var transient = ex is FileNotReadyException || ex is IOException || ex is HttpRequestException || ex is TaskCanceledException || ex is ImmichApiException api && api.IsTransient;
        lock (_activityLock) { _currentFileName = null; _currentFileProgressPercent = null; }
        if (!transient) { RecordFailure(path, SafeMessage(ex), false); AppLogger.Log($"VIRHE kasittelyssa '{path}': {SafeMessage(ex)}"); RaiseActivity(Loc.T("status.idle")); return; }
        int attempt;
        lock (_queueLock) { attempt = _retryAttempts.TryGetValue(path, out var current) ? current + 1 : 1; _retryAttempts[path] = attempt; }
        if (attempt > MaxRetryAttempts) { RecordFailure(path, "Uudelleenyritysten enimmäismäärä saavutettu: " + SafeMessage(ex), false); return; }
        var delay = TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, attempt - 1)));
        RecordFailure(path, $"{SafeMessage(ex)} Uusi yritys {delay.TotalMinutes:0} min kuluttua.", true);
        _ = RetryLaterAsync(path, delay, ct);
    }

    private async Task RetryLaterAsync(string path, TimeSpan delay, CancellationToken ct) { try { await Task.Delay(delay, ct); Enqueue(path); } catch (OperationCanceledException) { } }
    private async Task RetryPendingAlbumsAsync(CancellationToken ct)
    {
        if (_client is null) return;
        foreach (var entry in _history.GetPendingAlbums())
        {
            ct.ThrowIfCancellationRequested();
            try { await _client.AddAssetToAlbumAsync(entry.AlbumId, entry.AssetId, ct); _history.RemovePendingAlbum(entry); }
            catch (Exception ex) { AppLogger.Log($"VAROITUS: albumin korjausyritys epaonnistui '{entry.FilePath}': {SafeMessage(ex)}"); }
        }
    }

    private void RecordFailure(string path, string message, bool willRetry)
    {
        lock (_activityLock)
        {
            _recentFailures.Insert(0, new RecentFailure(Path.GetFileName(path), message, DateTime.Now, willRetry));
            if (_recentFailures.Count > MaxRecentFailures) _recentFailures.RemoveRange(MaxRecentFailures, _recentFailures.Count - MaxRecentFailures);
        }
        RaiseActivity(willRetry ? Loc.T("status.queued", GetQueueCount()) : Loc.T("status.idle"));
    }

    private int GetQueueCount() => _queue?.Reader.Count ?? 0;
    private static string SafeMessage(Exception ex) => ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;

    public void Dispose() { StopAsync().GetAwaiter().GetResult(); _lifecycleGate.Dispose(); }
    private sealed class FileNotReadyException : Exception { }
}
