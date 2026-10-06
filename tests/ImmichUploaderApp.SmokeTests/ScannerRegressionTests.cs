using System.Reflection;
using System.Threading.Channels;
using ImmichUploaderApp.Services;
using ImmichUploaderApp.Models;

internal static class ScannerRegressionTests
{
    public static void Run(Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = Path.Combine(Path.GetTempPath(), "immich-scan-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var known = Path.Combine(root, "known.jpg"); File.WriteAllText(known, "already-uploaded");
            var history = new UploadHistoryStore(Path.Combine(root, "history.json"));
            history.MarkUploaded("known-hash", known, new FileInfo(known));
            using var watcher = new UploadWatcherService(history, Path.Combine(root, "recent.json"));
            var type = typeof(UploadWatcherService);
            var config = new AppConfig { Directories = new() { root } };
            type.GetField("_config", flags)!.SetValue(watcher, config);
            type.GetField("_cts", flags)!.SetValue(watcher, new CancellationTokenSource());
            var queue = Channel.CreateBounded<string>(5);
            type.GetField("_queue", flags)!.SetValue(watcher, queue);
            var enqueue = type.GetMethod("Enqueue", flags)!;
            enqueue.Invoke(watcher, new object[] { known });
            check(queue.Reader.Count == 0, "Already uploaded files do not occupy bounded upload queue");
            var fresh = Path.Combine(root, "fresh.jpg"); File.WriteAllText(fresh, "new-photo");
            enqueue.Invoke(watcher, new object[] { fresh });
            check(queue.Reader.Count == 1, "New photos still enter the upload queue");
            type.GetMethod("TryEnsureWatcher", flags)!.Invoke(watcher, new object[] { root });
            var debounceLock = type.GetField("_debounceLock", flags)!.GetValue(watcher)!;
            var timers = (System.Collections.IDictionary)type.GetField("_debounceTimers", flags)!.GetValue(watcher)!;
            File.AppendAllText(known, "-changed");
            var detected = SpinWait.SpinUntil(() => { lock (debounceLock) return timers.Contains(known); }, TimeSpan.FromSeconds(5));
            check(detected, "Editing an existing photo triggers immediate watcher processing");
            var tree = Path.Combine(root, "tree"); Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(tree, "root.jpg"), "root");
            foreach (var name in new[] { "a", "b" })
            {
                Directory.CreateDirectory(Path.Combine(tree, name));
                File.WriteAllText(Path.Combine(tree, name, "photo.jpg"), "photo");
            }
            var files = (IEnumerable<string>)type.GetMethod("EnumerateFilesPruningExcluded", flags)!.Invoke(watcher, new object[] { tree })!;
            using var iterator = files.GetEnumerator();
            check(iterator.MoveNext(), "Scanner enumerates root files");
            check(iterator.MoveNext(), "Scanner enumerates a nested folder");
            var currentFolder = Path.GetDirectoryName(iterator.Current)!;
            foreach (var folder in Directory.GetDirectories(tree)) if (folder != currentFolder) Directory.Delete(folder, recursive: true);
            var completed = true;
            try { while (iterator.MoveNext()) { } } catch (IOException) { completed = false; }
            check(completed, "A folder removed during scan does not abort the entire scan");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
