using System.Globalization;
using System.Reflection;
using ShortcutList.Models;
using ShortcutList.Services;

var passed = 0;
var skipped = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++; Console.WriteLine("PASS " + name);
}
void Skip(string name) { skipped++; Console.WriteLine("SKIP " + name); }
QuickLaunchEntry Entry(string key, string name, bool favorite = false, int order = 0,
    string metadata = "", QuickLaunchKind kind = QuickLaunchKind.Shortcut, bool canLaunch = true) =>
    new(key, name, "", "", "", favorite, order, metadata, kind, new object(), "recipe:" + key, canLaunch);
string[] Keys(QuickLaunchResults results) => results.Items.Select(entry => entry.Key).ToArray();

var ranked = new[]
{
    Entry("metadata", "Other", true, -100, "report"),
    Entry("contains", "Daily report", false, 10),
    Entry("prefix", "Report planning", false, 10),
    Entry("exact", "report", false, 100),
    Entry("unrelated", "Calendar", true, -200)
};
Check(Keys(QuickLaunchSearch.Query(ranked, "report")).SequenceEqual(new[] { "exact", "prefix", "contains", "metadata" }),
    "name exact > prefix > contains > metadata, regardless of favorite/manual order");
var favorites = new[]
{
    Entry("regular", "A", false, -100), Entry("fav-later", "A", true, 2),
    Entry("z", "Same", true, 1), Entry("a", "Same", true, 1), Entry("first", "Z", true, -1)
};
var emptyExpected = new[] { "first", "a", "z", "fav-later", "regular" };
Check(Keys(QuickLaunchSearch.Query(favorites, "")).SequenceEqual(emptyExpected),
    "empty query sorts favorites, manual order, name, stable key");
Check(Keys(QuickLaunchSearch.Query(favorites.Reverse().ToArray(), " \t\r\n　")).SequenceEqual(emptyExpected),
    "whitespace-only search preserves deterministic order independent of source enumeration");
var words = new[]
{
    Entry("both-name", "Quarterly sales report"), Entry("metadata", "Sales", metadata: "quarterly report"),
    Entry("missing", "Sales report"), Entry("spacing", "Quarterly　Sales\tReport")
};
Check(Keys(QuickLaunchSearch.Query(words, " \tQUARTERLY　sales\nreport ")).SequenceEqual(new[] { "both-name", "spacing", "metadata" }),
    "all whitespace-separated words are required, with normalized phrase ranking");
Check(QuickLaunchSearch.Query(words, "report sales quarterly").TotalCount == 3,
    "all-word matching allows words in a different order");
Check(QuickLaunchSearch.Query(new[] { Entry("path", "X") with { TargetPath = "folder/annual.csv" },
    Entry("detail", "Y") with { Detail = "annual review" } }, "annual").TotalCount == 2,
    "target paths and detail are searched without reading or resolving targets");
var international = new[] { Entry("accent", "Café"), Entry("decomposed", "Cafe\u0301"),
    Entry("width", "ＣＡＦＥ"), Entry("japanese", "日本語 ガイド"), Entry("unvoiced", "日本語 カイド"),
    Entry("emoji", "📁😀計画"), Entry("invalid", "bad\ud800name") };
Check(QuickLaunchSearch.Query(international, "cafe").TotalCount == 3 &&
    QuickLaunchSearch.Query(international, "CAFÉ").TotalCount == 3,
    "Latin accents, canonical decomposition, width, and case normalize consistently");
Check(Keys(QuickLaunchSearch.Query(international, "日本語 ｶﾞｲﾄﾞ")).SequenceEqual(new[] { "japanese" }),
    "Japanese width normalizes without erasing meaningful voiced marks");
Check(QuickLaunchSearch.Query(international, "📁😀").Items.Single().Key == "emoji" &&
    QuickLaunchSearch.Query(international, "bad\ud800").Items.Single().Key == "invalid",
    "non-BMP and malformed imported UTF-16 do not break search");
var oldCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
    Check(QuickLaunchSearch.Query(new[] { Entry("invariant", "FILE") }, "file").TotalCount == 1,
        "matching is independent of the machine's current culture");
}
finally { CultureInfo.CurrentCulture = oldCulture; }
var duplicate1 = Entry("one", "Same", metadata: "same target");
var duplicate2 = Entry("two", "Same", metadata: "same target");
var sameKey = Entry("one", "Same", metadata: "same target");
var duplicates = QuickLaunchSearch.Query(new[] { duplicate1, duplicate2, sameKey }, "same");
Check(duplicates.TotalCount == 3 && ReferenceEquals(duplicates.Items[0], duplicate1) &&
    ReferenceEquals(duplicates.Items[1], sameKey) && ReferenceEquals(duplicates.Items[2], duplicate2),
    "duplicate names/targets/keys retain individual row and payload identity");
Check(ReferenceEquals(duplicates.Items[0].Payload, duplicate1.Payload) && duplicates.Items[0].RecipeToken == "recipe:one",
    "search preserves the exact launch payload and recipe token");
var disabled = Entry("disabled", "Offline source", canLaunch: false);
Check(ReferenceEquals(QuickLaunchSearch.Query(new[] { disabled }, "offline").Items.Single(), disabled),
    "disabled rows remain searchable without becoming launchable");
Check(Entry("set", "Set", kind: QuickLaunchKind.Workspace).KindDisplay == "まとめて開く" &&
    Entry("single", "Single").KindDisplay == "開く", "set launches have an explicit distinct action label");
var many = Enumerable.Range(0, 1005).Select(i => Entry("row:" + i, "Item " + i, order: 1005 - i)).ToArray();
var limited = QuickLaunchSearch.Query(many, "item");
Check(limited.TotalCount == 1005 && limited.Items.Count == 100 && limited.Items[0].Key == "row:1004",
    "default rendering is bounded to 100 with full matching count");
Check(QuickLaunchSearch.Query(many, "item", 7).Items.Count == 7 &&
    QuickLaunchSearch.Query(many, "item", 0).TotalCount == 1005 &&
    QuickLaunchSearch.Query(many, "item", 0).Items.Count == 0 &&
    QuickLaunchSearch.Query(many, "item", -1).Items.Count == 0,
    "explicit positive, zero, and negative display limits remain safe");
Check(QuickLaunchSearch.Query(Array.Empty<QuickLaunchEntry>(), "anything").TotalCount == 0 &&
    QuickLaunchSearch.Query(many, "absent").Items.Count == 0, "empty and no-match inputs return empty results");
Check(many[0].Key == "row:0" && many[^1].Key == "row:1004", "search never reorders the source collection");

var root = Path.Combine(Path.GetTempPath(), "DHubUnifiedRegression_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var sourceRoot = Path.Combine(root, "source");
var alternateRoot = Path.Combine(root, "alternate");
Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(alternateRoot);
File.WriteAllText(Path.Combine(sourceRoot, "sample.lnk"), "synthetic link, never launched");
File.WriteAllText(Path.Combine(sourceRoot, "site.url"), "[InternetShortcut]\nURL=https://example.invalid/Case/");
File.WriteAllText(Path.Combine(alternateRoot, "different.txt"), "different source");
try
{
    using var service = new BookmarkSourceService();
    async Task<BookmarkSnapshot> Stable(BookmarkSource candidate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var snapshot = await service.RefreshAsync(candidate);
            await Task.Delay(30);
            if (snapshot.CanLaunch && snapshot.Revision == service.GetSnapshot(candidate.Id).Revision &&
                snapshot.Entries.All(entry => service.IsCurrent(snapshot, entry))) return snapshot;
        }
        throw new InvalidOperationException("Source did not reach a stable launchable generation: " + service.GetSnapshot(candidate.Id).Status);
    }
    async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new InvalidOperationException("Expected watcher invalidation was not observed");
    }
    var source = new BookmarkSource { FolderPath = sourceRoot, Name = "Source" };
    Check(!service.GetSnapshot(source.Id).CanLaunch, "unloaded linked sources fail closed");
    var original = await Stable(source);
    var link = original.Entries.Single(entry => entry.Name == "sample.lnk");
    var url = original.Entries.Single(entry => entry.Name == "site.url");
    Check(original.Entries.Count == 2 && link.FullPath.EndsWith("sample.lnk") && url.FullPath.EndsWith("site.url"),
        "linked shortcuts retain original shell files rather than resolving targets");
    await Stable(source);
    Check(service.IsCurrent(original, link), "unchanged refresh preserves a valid open selection");
    Check(!service.IsCurrent(original, link with { FullPath = Path.Combine(alternateRoot, "different.txt") }) &&
        !service.IsCurrent(original, link with { IsFolder = true }) &&
        !service.IsCurrent(original with { SourceId = "other" }, link),
        "launch validation binds source, full path, kind, fingerprint, and snapshot membership");

    // Saturate the real shared gate to deterministically queue scans. This tests
    // cancellation and edit races without timing assumptions or production hooks.
    var scanGate = (SemaphoreSlim)typeof(BookmarkSourceService).GetField("ScanGate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    async Task HoldScans() { for (var i = 0; i < 4; i++) await scanGate.WaitAsync(); }
    void ReleaseScans() => scanGate.Release(4);
    await HoldScans();
    Task<BookmarkSnapshot> oldPathTask;
    Task<BookmarkSnapshot> newPathTask;
    try
    {
        oldPathTask = service.RefreshAsync(source);
        Check(ReferenceEquals(oldPathTask, service.RefreshAsync(source)), "identical in-flight refreshes share one task");
        source.FolderPath = alternateRoot;
        newPathTask = service.RefreshAsync(source);
        Check(!ReferenceEquals(oldPathTask, newPathTask) && !service.IsCurrent(original, link),
            "path edit supersedes a queued scan and immediately invalidates old selections");
    }
    finally { ReleaseScans(); }
    var oldPathResult = await oldPathTask;
    var newPathResult = await newPathTask;
    Check(!oldPathResult.CanLaunch && newPathResult.Entries.All(entry => entry.FullPath.StartsWith(alternateRoot, StringComparison.Ordinal)),
        "out-of-order old-path work cannot publish or re-enable an obsolete source");
    var changed = await Stable(source);
    Check(changed.Entries.Single().Name == "different.txt" && service.GetSnapshot(source.Id).Entries.Single().Name == "different.txt",
        "new-path snapshot remains authoritative after old scan completion");

    await HoldScans();
    Task<BookmarkSnapshot> beforeHide;
    Task<BookmarkSnapshot> afterHide;
    try
    {
        beforeHide = service.RefreshAsync(source);
        source.HiddenPaths.Add("different.txt");
        afterHide = service.RefreshAsync(source);
        Check(!ReferenceEquals(beforeHide, afterHide) && !service.IsCurrent(changed, changed.Entries[0]),
            "hidden-rule changes supersede pending work and invalidate selected hidden rows");
    }
    finally { ReleaseScans(); }
    Check(!(await beforeHide).CanLaunch && (await afterHide).Entries.Count == 0,
        "old hidden-rule scan cannot republish hidden entries");
    Check((await Stable(source)).Status.Contains("非表示") && File.Exists(Path.Combine(alternateRoot, "different.txt")),
        "hiding is non-destructive and all-hidden status remains distinct from empty");
    source.HiddenPaths.Clear(); source.FolderPath = sourceRoot;
    original = await Stable(source); link = original.Entries.Single(entry => entry.Name == "sample.lnk");
    await HoldScans();
    Task<BookmarkSnapshot> removedTask;
    Task<BookmarkSnapshot> reconnectTask;
    try
    {
        removedTask = service.RefreshAsync(source);
        service.Remove(source.Id);
        reconnectTask = service.RefreshAsync(source);
        Check(!service.IsCurrent(original, link), "removal immediately blocks existing selections without deleting targets");
    }
    finally { ReleaseScans(); }
    Check(!(await removedTask).CanLaunch && (await reconnectTask).Revision != original.Revision,
        "queued removal/reconnect cannot resurrect an older generation");
    original = await Stable(source); link = original.Entries.Single(entry => entry.Name == "sample.lnk");
    File.AppendAllText(link.FullPath, " modified");
    await Until(() => !service.IsCurrent(original, link));
    Check(!service.GetSnapshot(source.Id).CanLaunch, "watcher changes invalidate cached rows before the next scan");
    var modified = await Stable(source);
    Check(!service.IsCurrent(original, link) && modified.Entries.Single(entry => entry.Name == "sample.lnk").Fingerprint != link.Fingerprint,
        "observed replacement rejects stale fingerprints after refresh");

    // A disposed watcher's delayed callback must not invalidate a newer path.
    object StateFor(string id)
    {
        var dictionary = typeof(BookmarkSourceService).GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        return dictionary.GetType().GetProperty("Item")!.GetValue(dictionary, new object[] { id })!;
    }
    var state = StateFor(source.Id);
    var watcherField = state.GetType().GetField("Watcher")!;
    var oldWatcher = (FileSystemWatcher)watcherField.GetValue(state)!;
    source.FolderPath = alternateRoot;
    var alternate = await Stable(source);
    typeof(BookmarkSourceService).GetMethod("Invalidate", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(service, new object[] { state, oldWatcher });
    Check(service.IsCurrent(alternate, alternate.Entries.Single()), "delayed events from a retired watcher cannot invalidate the replacement watcher");
    var watcher = (FileSystemWatcher)watcherField.GetValue(state)!;
    typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(watcher, new object[] { new ErrorEventArgs(new InternalBufferOverflowException("synthetic overflow")) });
    Check(!service.IsCurrent(alternate, alternate.Entries.Single()) && !service.GetSnapshot(source.Id).CanLaunch,
        "watcher overflow fails closed until a fresh watcher and scan succeed");
    await Stable(source);

    var missing = await service.RefreshAsync(new BookmarkSource { FolderPath = Path.Combine(root, "missing") });
    Check(!missing.CanLaunch && missing.Status.Contains("オフライン"), "missing roots are offline rather than empty");
    var emptyRoot = Path.Combine(root, "empty"); Directory.CreateDirectory(emptyRoot);
    var emptySource = new BookmarkSource { FolderPath = emptyRoot, HiddenPaths = new() { "old.txt" } };
    Check((await Stable(emptySource)).Status.Contains("空"), "stale hidden rules do not mislabel an actually empty folder");
    var nested = Path.Combine(sourceRoot, "nested"); Directory.CreateDirectory(nested);
    File.WriteAllText(Path.Combine(nested, "inside.txt"), "original");
    var nestedSource = new BookmarkSource { FolderPath = sourceRoot, HiddenPaths = new() { "nested" } };
    Check((await Stable(nestedSource)).Entries.All(entry => !entry.RelativePath.StartsWith("nested", StringComparison.Ordinal)) &&
        File.Exists(Path.Combine(nested, "inside.txt")), "hiding a folder hides descendants without deleting or reading through them");

    var symlinkRoot = Path.Combine(root, "links"); Directory.CreateDirectory(symlinkRoot);
    File.WriteAllText(Path.Combine(symlinkRoot, "ordinary.txt"), "ordinary");
    try
    {
        Directory.CreateSymbolicLink(Path.Combine(symlinkRoot, "outside"), alternateRoot);
        File.CreateSymbolicLink(Path.Combine(symlinkRoot, "shortcut.lnk"), link.FullPath);
        Directory.CreateSymbolicLink(Path.Combine(root, "root-link"), symlinkRoot);
        var linked = await Stable(new BookmarkSource { FolderPath = symlinkRoot });
        Check(linked.Entries.Count == 1 && linked.Entries.Single().Name == "ordinary.txt" && linked.Status.Contains("除外"),
            "file and directory reparse points are skipped without traversing their targets");
        var rejectedRoot = await service.RefreshAsync(new BookmarkSource { FolderPath = Path.Combine(root, "root-link") });
        Check(!rejectedRoot.CanLaunch && rejectedRoot.Entries.Count == 0, "a reparse-point source root is refused");
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException ||
        ex is IOException && OperatingSystem.IsWindows() && (ex.HResult & 0xFFFF) is 5 or 1314)
    { Skip("symlink creation is unavailable on this host: " + ex.GetType().Name); }

    var deepRoot = Path.Combine(root, "deep"); Directory.CreateDirectory(deepRoot);
    var deep = deepRoot;
    for (var i = 0; i < 18; i++) { deep = Path.Combine(deep, "d" + i); Directory.CreateDirectory(deep); }
    File.WriteAllText(Path.Combine(deep, "far.url"), "must not be reached");
    var depthBounded = await Stable(new BookmarkSource { FolderPath = deepRoot });
    Check(depthBounded.IsTruncated && depthBounded.Entries.Count == 17 && depthBounded.Entries.All(entry => entry.Name != "far.url"),
        "descent is bounded to depth 16 with explicit truncation");

    if (!OperatingSystem.IsWindows())
    {
        var unreadableRoot = Path.Combine(root, "permission"); Directory.CreateDirectory(unreadableRoot);
        var denied = Path.Combine(unreadableRoot, "denied"); Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "private.txt"), "unreadable");
        File.WriteAllText(Path.Combine(unreadableRoot, "readable.txt"), "readable");
        var permissions = File.GetUnixFileMode(denied);
        try
        {
            File.SetUnixFileMode(denied, UnixFileMode.None);
            var accessDenied = false;
            try { _ = Directory.EnumerateFileSystemEntries(denied).ToArray(); }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            if (accessDenied)
            {
                // A recursive OS watcher can itself reject an inaccessible child.
                // First assert that real behavior never permits launch. Then use a
                // deliberately unhealthy inert watcher to isolate deferred MoveNext
                // handling without racing an asynchronous OS watcher error.
                var permissionSource = new BookmarkSource { FolderPath = unreadableRoot };
                var firstRead = await service.RefreshAsync(permissionSource);
                Check(!firstRead.CanLaunch || firstRead.Entries.All(entry => entry.Name != "private.txt"),
                    "inaccessible descendants are never admitted as launchable entries");
                var permissionState = StateFor(permissionSource.Id);
                var permissionGate = permissionState.GetType().GetField("Gate")!.GetValue(permissionState)!;
                lock (permissionGate)
                {
                    typeof(BookmarkSourceService).GetMethod("StopWatcher", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, new[] { permissionState });
                    permissionState.GetType().GetField("Watcher")!.SetValue(permissionState, new FileSystemWatcher(unreadableRoot));
                    permissionState.GetType().GetField("RootCreated")!.SetValue(permissionState, new DirectoryInfo(unreadableRoot).CreationTimeUtc.Ticks);
                }
                var readable = await service.RefreshAsync(permissionSource);
                Check(!readable.CanLaunch && readable.Entries.Any(entry => entry.Name == "readable.txt") &&
                    readable.Entries.All(entry => entry.Name != "private.txt") && readable.Status.Contains("除外"),
                    "deferred nested enumeration failures retain readable siblings, disabled when watcher is unavailable");
                var deniedSource = new BookmarkSource { FolderPath = denied };
                var deniedRoot = await service.RefreshAsync(deniedSource);
                Check(!deniedRoot.CanLaunch && deniedRoot.Entries.Count == 0,
                    "inaccessible roots fail closed whether enumeration or the OS watcher detects denial first");
            }
            else Skip("host privileges bypass Unix permission fixture");
        }
        finally { File.SetUnixFileMode(denied, permissions); }
    }
    else Skip("Unix inaccessible-directory fixture; Windows ACL behavior requires native manual QA");

    var hugeRoot = Path.Combine(root, "huge"); Directory.CreateDirectory(hugeRoot);
    var hugeNames = Enumerable.Range(0, 20002).Select(i => $"item{i:D5}.txt").ToList();
    foreach (var name in hugeNames) File.WriteAllText(Path.Combine(hugeRoot, name), "");
    var hugeSource = new BookmarkSource { FolderPath = hugeRoot };
    var huge = await Stable(hugeSource);
    Check(huge.Entries.Count == 10000 && huge.IsTruncated && huge.Status.Contains("上限"),
        "huge linked folders stop at 10000 entries and expose the source-folder fallback");
    hugeSource.HiddenPaths = hugeNames;
    var hiddenHuge = await Stable(hugeSource);
    Check(hiddenHuge.Entries.Count == 0 && hiddenHuge.IsTruncated,
        "all-hidden huge folders also stop after the 20000-visit budget");
    Check(File.ReadAllText(url.FullPath).Contains("Case/"), "linked .url file contents remain untouched");

    await HoldScans();
    Task<BookmarkSnapshot> disposedTask;
    try { disposedTask = service.RefreshAsync(source); service.Dispose(); }
    finally { ReleaseScans(); }
    Check(!(await disposedTask).CanLaunch && !service.IsCurrent(alternate, alternate.Entries.Single()),
        "disposing cancels queued work and invalidates launch selections");
    var disposedRejected = false;
    try { await service.RefreshAsync(source); } catch (ObjectDisposedException) { disposedRejected = true; }
    Check(disposedRejected && File.Exists(url.FullPath), "disposed service rejects new work while preserving originals");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"RESULT: {passed} passed, {skipped} skipped");
