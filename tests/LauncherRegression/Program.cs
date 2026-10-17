using ShortcutList.Models;
using ShortcutList.Services;

var total = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    total++; Console.WriteLine("PASS " + name);
}
var requests = 0; var saves = 0;
var outcome = LaunchCoordinator.Execute(() => requests++, () => saves++);
Check(outcome.Requested && requests == 1 && saves == 1 && outcome.HistoryError is null, "successful request records history once");
outcome = LaunchCoordinator.Execute(() => throw new IOException("shell"), () => saves++);
Check(!outcome.Requested && outcome.RequestError is IOException && saves == 1, "failed request never records successful history");
outcome = LaunchCoordinator.Execute(() => requests++, () => throw new IOException("disk full"));
Check(outcome.Requested && outcome.HistoryError is IOException && outcome.RequestError is null && requests == 2, "history failure preserves request success");
saves++; Check(requests == 2, "history-only retry issues no launch");
outcome = LaunchCoordinator.Execute(() => requests++, () => throw new UnauthorizedAccessException());
Check(outcome.Requested && outcome.HistoryError is UnauthorizedAccessException && requests == 3, "denied history save never repeats shell");

var root = Path.Combine(Path.GetTempPath(), "DHubLauncherRegression_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using var service = new BookmarkSourceService();
    async Task<BookmarkSnapshot> Stable(BookmarkSource candidate)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(50); // Watcher notifications are asynchronous; observe a quiescent generation.
            var value = await service.RefreshAsync(candidate);
            await Task.Delay(25);
            if (value.CanLaunch && value.Entries.All(e => service.IsCurrent(value, e))) return value;
        }
        throw new InvalidOperationException("Source never reached a stable snapshot");
    }
    var source = new BookmarkSource { Name = "Fixtures", FolderPath = root };
    Check(!service.GetSnapshot(source.Id).CanLaunch, "unloaded source is non-launchable");
    var empty = await Stable(source);
    Check(empty.CanLaunch && empty.Entries.Count == 0 && empty.Status.Contains("空"), "empty source distinct from offline");
    var folder = Path.Combine(root, "分類"); Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(root, "sample.lnk"), "synthetic, never launched");
    File.WriteAllText(Path.Combine(folder, "site.url"), "[InternetShortcut]\nURL=https://example.invalid/Case/");
    var snapshot = await Stable(source);
    Check(snapshot.Entries.Count == 3, "files and classification folders enumerated");
    Check(snapshot.Entries.Any(x => x.IsFolder && x.RelativePath == "分類"), "folder has independent open target");
    var link = snapshot.Entries.Single(x => x.Name == "sample.lnk");
    Check(link.FullPath.EndsWith("sample.lnk"), "lnk recipe remains original file");
    var url = snapshot.Entries.Single(x => x.Name == "site.url");
    Check(url.FullPath.EndsWith("site.url"), "url file remains original file");
    Check(service.IsCurrent(snapshot, link), "current snapshot can launch");
    var next = await Stable(source);
    Check(!service.IsCurrent(snapshot, link), "old generation cannot launch after refresh");
    Check(service.IsCurrent(next, next.Entries.Single(x => x.Name == "sample.lnk")), "new generation can launch");
    source.HiddenPaths.Add("分類");
    var hidden = await Stable(source);
    Check(hidden.Entries.Count == 1 && File.Exists(url.FullPath), "hiding classification is non-destructive and hides descendants");
    source.HiddenPaths.Add("sample.lnk");
    var allHidden = await Stable(source);
    Check(allHidden.Entries.Count == 0 && allHidden.Status.Contains("非表示"), "all hidden status is explicit");
    source.HiddenPaths.Clear();
    var beforeRemove = await Stable(source);
    var savedLink = beforeRemove.Entries.Single(x => x.Name == "sample.lnk");
    service.Remove(source.Id);
    Check(!service.IsCurrent(beforeRemove, savedLink) && File.Exists(savedLink.FullPath), "disconnect blocks old snapshot and preserves originals");
    var reconnected = await Stable(source);
    Check(!service.IsCurrent(beforeRemove, savedLink), "reconnect never reuses old source generation");
    File.Delete(Path.Combine(root, "sample.lnk"));
    var deleted = await Stable(source);
    Check(!service.IsCurrent(reconnected, reconnected.Entries.Single(x => x.Name == "sample.lnk")) && !deleted.Entries.Any(x => x.Name == "sample.lnk"), "observed deletion cannot launch stale row");
    var missing = new BookmarkSource { FolderPath = Path.Combine(root, "missing") };
    var offline = await service.RefreshAsync(missing);
    Check(!offline.CanLaunch && offline.Status.Contains("オフライン"), "unavailable source distinct from empty");
    Check(File.ReadAllText(url.FullPath).Contains("Case/"), "source url contents unchanged");
    var largeFolder = Path.Combine(root, "large"); Directory.CreateDirectory(largeFolder);
    for (var i = 0; i < 10001; i++) File.WriteAllText(Path.Combine(largeFolder, $"item{i:D5}.txt"), "");
    var largeSource = new BookmarkSource { FolderPath = largeFolder };
    var large = await Stable(largeSource);
    Check(large.Entries.Count == 10000 && large.IsTruncated && large.Status.Contains("上限"), "overflow is explicit with original-folder route");
    service.Dispose();
    Check(File.Exists(url.FullPath), "dispose leaves original files");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"RESULT: {total} passed");

