using System.Net;
using System.Reflection;
using System.Text;
using ImmichUploaderApp;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

internal static class RegressionTests
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static async Task InvokeAsync(object target, string name, params object[] args) => await (Task)target.GetType().GetMethod(name, Private)!.Invoke(target, args)!;

    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "immich-regression-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var config = new AppConfig { ServerUrl = "https://example.test/api", ApiKey = "account-one", SyncEnabled = true,
                SyncPhotoFolder = Path.Combine(root, "photos"), SyncVideoFolder = Path.Combine(root, "videos"), SyncMode = "Original" };
            Directory.CreateDirectory(config.SyncPhotoFolder);
            Directory.CreateDirectory(config.SyncVideoFolder);
            var state = Path.Combine(root, "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "sync-manifest.json"), "{}");
            var firstScope = ConfigService.GetScopedStatePath(config, "sync-manifest.json", state);
            var other = new AppConfig { ServerUrl = config.ServerUrl, ApiKey = "account-two" };
            var otherScope = ConfigService.GetScopedStatePath(other, "sync-manifest.json", state);
            check(firstScope != otherScope && !File.Exists(otherScope), "Account change isolates state without inheriting legacy manifest");
            other.ServerUrl = "https://another.test"; other.ApiKey = config.ApiKey;
            check(ConfigService.GetScopedStatePath(other, "upload-history.json", state) != ConfigService.GetScopedStatePath(config, "upload-history.json", state), "Server change isolates upload history");
            var history = new UploadHistoryStore(Path.Combine(root, "history.json"));
            var manifestPath = Path.Combine(root, "manifest.json");
            var manifest = new SyncManifestStore(manifestPath);
            var handler = new FakeHandler();
            using var http = new HttpClient(handler);
            using var client = new ImmichClient(config.ServerUrl, config.ApiKey, http);
            using var sync = new PhotoSyncService(history, manifest, Path.Combine(root, "recent.json"));
            Set(sync, "_config", config); Set(sync, "_client", client);
            var path = Path.Combine(config.SyncPhotoFolder, "missing.jpg");
            manifest.Set("missing", new(new() { path }, "Original", DateTime.UtcNow));
            config.SyncDeleteRemoteOnLocalDelete = true;
            Directory.Move(config.SyncPhotoFolder, config.SyncPhotoFolder + "-offline");
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            check(handler.DeleteCalls == 0 && manifest.TryGet("missing", out var offline) && !offline.LocallyDeleted, "Unavailable root never trashes remote assets");
            await InvokeAsync(sync, "SyncAssetAsync", new AssetSummary { Id = "offline-new", OriginalFileName = "offline.jpg", Type = "IMAGE", FileCreatedAt = DateTime.Now }, CancellationToken.None);
            check(!Directory.Exists(config.SyncPhotoFolder) && handler.DownloadCalls == 0, "Download scan does not recreate an unavailable sync root");
            Directory.Move(config.SyncPhotoFolder + "-offline", config.SyncPhotoFolder);
            handler.FailTrash = true;
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            check(handler.DeleteCalls == 1 && manifest.TryGet("missing", out var pending) && pending.PendingRemoteTrash, "Failed remote trash remains pending");
            handler.FailTrash = false;
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            check(handler.DeleteCalls == 2 && manifest.TryGet("missing", out var done) && done.LocallyDeleted && !done.PendingRemoteTrash, "Pending remote trash is retried successfully");
            config.SyncDeleteRemoteOnLocalDelete = false;
            var asset = new AssetSummary { Id = "12345678-asset", OriginalFileName = "local.jpg", Type = "IMAGE", FileCreatedAt = new DateTime(2026, 1, 1) };
            var destinations = (List<string>)typeof(PhotoSyncService).GetMethod("BuildDestinationPaths", Private)!.Invoke(sync, new object[] { asset })!;
            manifest.Set(asset.Id, new(destinations, "Original", DateTime.UtcNow));
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            await InvokeAsync(sync, "SyncAssetAsync", asset, CancellationToken.None);
            check(!File.Exists(destinations[0]) && handler.DownloadCalls == 0, "Locally deleted asset is not resurrected when remote trash is disabled");
            check(new SyncManifestStore(manifestPath).TryGet(asset.Id, out var persisted) && persisted.LocallyDeleted, "Local deletion survives restart");
            var kept = Path.Combine(config.SyncPhotoFolder, "kept.jpg"); File.WriteAllText(kept, "kept");
            var removed = Path.Combine(config.SyncPhotoFolder, "removed.jpg");
            manifest.Set("partial", new(new() { kept, removed }, "Original", DateTime.UtcNow));
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            check(manifest.TryGet("partial", out var partial) && partial.LocalPaths.SequenceEqual(new[] { kept }) && partial.SuppressedPaths.Contains(removed), "Deleted album copy remains suppressed while another survives");

            var partialAsset = new AssetSummary { Id = "album-copy", OriginalFileName = "album.jpg", Type = "IMAGE", FileCreatedAt = DateTime.Now };
            config.SyncOrganizeByAlbum = true;
            Set(sync, "_albumNamesByAssetId", new Dictionary<string, List<string>> { [partialAsset.Id] = new() { "a", "b" } });
            var albumPaths = (List<string>)typeof(PhotoSyncService).GetMethod("BuildDestinationPaths", Private)!.Invoke(sync, new object[] { partialAsset })!;
            Directory.CreateDirectory(Path.GetDirectoryName(albumPaths[0])!); File.WriteAllText(albumPaths[0], "existing-photo");
            manifest.Set(partialAsset.Id, new(albumPaths, "Original", DateTime.UtcNow));
            await InvokeAsync(sync, "ReconcileLocalDeletionsAsync", CancellationToken.None);
            await InvokeAsync(sync, "SyncAssetAsync", partialAsset, CancellationToken.None);
            check(!File.Exists(albumPaths[1]) && handler.DownloadCalls == 0, "Next album scan does not restore a deleted album copy");
            config.SyncOrganizeByAlbum = false;

            var blocking = new FakeHandler { BlockSearch = true };
            using var blockedHttp = new HttpClient(blocking);
            using var lifecycle = new PhotoSyncService(history, new SyncManifestStore(Path.Combine(root, "lifecycle.json")), Path.Combine(root, "lifecycle-recent.json"), c => new ImmichClient(c.ServerUrl, c.ApiKey, blockedHttp));
            await lifecycle.StartAsync(config);
            await blocking.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await lifecycle.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            check(blocking.SearchCancelled && !lifecycle.IsRunning, "Stop waits for in-flight scan cancellation");

            var albumHandler = new FakeHandler { FailAlbumsOnce = true };
            using var albumHttp = new HttpClient(albumHandler);
            using var albumClient = new ImmichClient(config.ServerUrl, config.ApiKey, albumHttp);
            using var watcher = new UploadWatcherService(new UploadHistoryStore(Path.Combine(root, "album-history.json")), Path.Combine(root, "album-recent.json"));
            config.AlbumName = "Backup";
            Set(watcher, "_config", config); Set(watcher, "_client", albumClient);
            var upload = Path.Combine(root, "upload.jpg"); File.WriteAllText(upload, "test-photo");
            try { await InvokeAsync(watcher, "ProcessFileAsync", upload, CancellationToken.None); } catch (ImmichApiException) { }
            check(albumHandler.UploadCalls == 0, "Failed album resolution does not upload outside selected album");
            await InvokeAsync(watcher, "ProcessFileAsync", upload, CancellationToken.None);
            check(albumHandler.UploadCalls == 1 && albumHandler.AlbumAddCalls == 1, "Album resolution retries and adds uploaded asset");

            Exception? uiError = null;
            var uiThread = new Thread(() =>
            {
                try
                {
                    var watched = Path.Combine(root, "watched");
                    var deep = Path.Combine(watched, "a", "b", "private"); Directory.CreateDirectory(deep);
                    using var form = new SettingsForm(new AppConfig { Directories = new() { watched }, ExcludeDirectories = new() { deep } });
                    var exclusions = (List<string>)typeof(SettingsForm).GetMethod("BuildExclusions", Private)!.Invoke(form, null)!;
                    check(exclusions.Contains(deep), "Saving unexpanded exclusion tree preserves deep exclusions");
                }
                catch (Exception ex) { uiError = ex; }
            });
            uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start(); uiThread.Join();
            if (uiError is not null) throw uiError;

            using var updateHttp = new HttpClient(new FakeHandler());
            var update = await new UpdateService(updateHttp).CheckForUpdateAsync();
            check(update.TagName == "v1.2.3" && update.DownloadUrl!.EndsWith("Setup-1.2.3.exe") && UpdateService.IsNewer(update.LatestVersion, new Version(1, 2, 2)), "Published installer is recognized as newer than v1.2.2");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public int DeleteCalls, DownloadCalls, UploadCalls, AlbumAddCalls;
        public bool FailTrash, BlockSearch, SearchCancelled, FailAlbumsOnce;
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("search/metadata") && BlockSearch)
            {
                SearchStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { SearchCancelled = true; throw; }
            }
            if (request.Method == HttpMethod.Delete) { DeleteCalls++; return Json(FailTrash ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, "{}"); }
            if (path.EndsWith("/original") || path.EndsWith("/thumbnail")) { DownloadCalls++; return Json(HttpStatusCode.OK, "photo"); }
            if (path.EndsWith("/albums") && request.Method == HttpMethod.Get)
            {
                if (FailAlbumsOnce) { FailAlbumsOnce = false; return Json(HttpStatusCode.ServiceUnavailable, "{}"); }
                return Json(HttpStatusCode.OK, "[{\"id\":\"album\",\"albumName\":\"Backup\"}]");
            }
            if (path.EndsWith("/albums/album/assets")) { AlbumAddCalls++; return Json(HttpStatusCode.OK, "[]"); }
            if (path.EndsWith("/assets") && request.Method == HttpMethod.Post) { UploadCalls++; return Json(HttpStatusCode.OK, "{\"id\":\"uploaded\",\"status\":\"created\"}"); }
            if (path.EndsWith("/releases/latest")) return Json(HttpStatusCode.OK, "{\"tag_name\":\"v1.2.3\",\"html_url\":\"https://github.com/lafkato/immich-uploader/releases/tag/v1.2.3\",\"assets\":[{\"name\":\"Setup-1.2.3.exe\",\"browser_download_url\":\"https://example.test/Setup-1.2.3.exe\"}]}");
            return Json(HttpStatusCode.OK, "{\"assets\":{\"items\":[],\"nextPage\":null}}");
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
