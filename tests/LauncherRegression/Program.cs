using ShortcutList.Models;
using ShortcutList.Services;

var total = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    total++; Console.WriteLine("PASS " + name);
}
var ordering = LauncherBarLayout.OrderedIds(new[]
{
    new LauncherPin("z", "Same", 7), new LauncherPin("a", "Same", 7),
    new LauncherPin("first", "Z", -2), new LauncherPin("last", "A", 99)
});
Check(ordering.SequenceEqual(new[] { "first", "a", "z", "last" }), "pins preserve SortOrder with deterministic duplicate-name ties");
var reordered = LauncherBarLayout.Move(ordering, "z", -1);
Check(reordered.SequenceEqual(new[] { "first", "z", "a", "last" }) && ordering[1] == "a", "move-left swaps adjacent pins without mutating input");
Check(LauncherBarLayout.Move(reordered, "z", 1).SequenceEqual(ordering), "move-right restores exact pin order");
Check(LauncherBarLayout.Move(ordering, "first", -1).SequenceEqual(ordering) &&
    LauncherBarLayout.Move(ordering, "last", 1).SequenceEqual(ordering), "boundary moves do not wrap");
Check(LauncherBarLayout.Move(ordering, "missing", 1).SequenceEqual(ordering) &&
    LauncherBarLayout.Move(ordering, "a", 20).SequenceEqual(ordering), "stale selections and unsupported moves are no-ops");
Check(LauncherBarLayout.Move(Array.Empty<string>(), "missing", -1).Count == 0, "empty pin list is safe");
var area = new LauncherBounds(0, 0, 1920, 1040);
var position = LauncherBarLayout.Place(area, 500, 48);
Check(position == new LauncherPosition(8, 984), "first bar placement stays above taskbar work area");
Check(LauncherBarLayout.Place(area, 500, 48, 300, 400) == new LauncherPosition(300, 400), "saved visible position is retained");
Check(LauncherBarLayout.Place(area, 500, 48, 9000, -999) == new LauncherPosition(1420, 0), "offscreen position is clamped after display changes");
Check(LauncherBarLayout.Place(new(-1920, -100, 1920, 1080), 500, 48, -5000, 2000) == new LauncherPosition(-1920, 932), "negative-coordinate secondary monitors are supported");
Check(LauncherBarLayout.Place(new(20, 30, 100, 80), 500, 200, 999, 999) == new LauncherPosition(20, 30), "oversized bar has safe origin in a small work area");
Check(LauncherBarLayout.Place(area, 500, 48, double.NaN, double.PositiveInfinity) == position, "nonfinite positions fall back safely");
Check(LauncherBarLayout.VisiblePinCount(30, 1920) == 8 && LauncherBarLayout.VisiblePinCount(30, 800) == 4,
    "visible pins are capped with room for overflow controls");
Check(LauncherBarLayout.VisiblePinCount(3, 1920) == 3 && LauncherBarLayout.VisiblePinCount(30, 200) == 0 &&
    LauncherBarLayout.VisiblePinCount(0, 1920) == 0, "empty and narrow bars never lose overflow access");
var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"SchemaVersion\":4,\"ShowLauncherBar\":false}")!;
Check(settings.LauncherBarLeft is null && settings.LauncherBarTop is null && !settings.ShowLauncherBar,
    "older settings keep visibility preference and first-use placement");
settings.LauncherBarLeft = -1400; settings.LauncherBarTop = 802;
var settingsCopy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
Check(settingsCopy.LauncherBarLeft == -1400 && settingsCopy.LauncherBarTop == 802, "bar position roundtrips with existing settings schema");

var recipe = new LauncherPinRecipe("https://example.invalid/Case", "", "", "", 1);
Check(recipe == new LauncherPinRecipe("https://example.invalid/Case", "", "", "", 1), "equivalent drop recipes can reuse an existing registration");
Check(recipe != recipe with { Arguments = "--profile work" } && recipe != recipe with { OpenApplicationPath = "browser.exe" } &&
    recipe != recipe with { OpenApplicationArguments = "--private" }, "different arguments and application recipes remain distinct");
Check(recipe != recipe with { TargetPath = "https://example.invalid/case" } && recipe != recipe with { ShortcutType = 2 },
    "target case and original shell file type are preserved");
var mutation = 0; var pinSaves = 0; var rollbacks = 0;
var editError = LauncherBarLayout.SaveEdit(() => mutation = 1, () => pinSaves++, () => { mutation = 0; rollbacks++; });
Check(editError is null && mutation == 1 && pinSaves == 1 && rollbacks == 0, "pin save succeeds once without rollback");
editError = LauncherBarLayout.SaveEdit(() => mutation = 2, () => { pinSaves++; throw new IOException("disk full"); }, () => { mutation = 1; rollbacks++; });
Check(editError is IOException && mutation == 1 && pinSaves == 2 && rollbacks == 1, "failed pin save restores prior state and never retries");
editError = LauncherBarLayout.SaveEdit(() => { mutation = 3; throw new InvalidOperationException("stale selection"); },
    () => pinSaves++, () => { mutation = 1; rollbacks++; });
Check(editError is InvalidOperationException && mutation == 1 && pinSaves == 2 && rollbacks == 2, "failed edit restores prior state without saving partial mutation");
Check(LauncherBarLayout.VisiblePinCount(50, double.MaxValue) == 8, "huge finite width cannot overflow visible-pin arithmetic");

var boundPin = new LauncherPin("one", "Unchanged", 0);
var boundPins = new[] { boundPin };
Check(LauncherBarLayout.SameInstances(boundPins, new[] { boundPin }), "history-only changes retain live bindings and focus");
Check(!LauncherBarLayout.SameInstances(boundPins, new[] { new LauncherPin("one", "Unchanged", 0) }),
    "reload with equal pin values requires fresh callbacks to new instances");
Check(!LauncherBarLayout.SameInstances(boundPins, Array.Empty<LauncherPin>()), "removed pins invalidate live bindings");

var collidingLabels = new[]
{
    new LauncherPin("a", "prefix-middle-one-suffix", 0), new LauncherPin("b", "prefix-middle-two-suffix", 1)
};
Check(LauncherBarLayout.Label("a", collidingLabels[0].Name, 13, collidingLabels) == "[1] prefix…suffix" &&
    LauncherBarLayout.Label("b", collidingLabels[1].Name, 13, collidingLabels) == "[2] prefix…suffix",
    "different long names with identical compact labels have visible prefix discriminators");
var sameLabels = new[] { new LauncherPin("b", "日本語の同名ファイル", 0), new LauncherPin("a", "日本語の同名ファイル", 1) };
Check(LauncherBarLayout.Label("a", sameLabels[1].Name, 13, sameLabels).StartsWith("[1] "),
    "exact duplicate names put their stable discriminator before any clipped text");
Check(LauncherBarLayout.Label("a", "unique", 13, new[] { new LauncherPin("a", "unique", 0) }) == "unique",
    "unique short names need no extra label clutter");
Check(LauncherBarLayout.CompactName("😀😀😀middle😀😀😀", 7) == "😀😀😀…😀😀😀", "compact names never split surrogate pairs");
Check(LauncherBarLayout.VisiblePinCount(20, 743) == 3 && LauncherBarLayout.VisiblePinCount(20, 744) == 4,
    "visible pin boundary reserves all 248 DIP of actual controls");

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
    source.HiddenPaths.Add("previously-hidden.txt");
    var emptyWithOldHidden = await Stable(source);
    Check(emptyWithOldHidden.Status.Contains("空") && !emptyWithOldHidden.Status.Contains("非表示"), "stale hidden rule does not mislabel an empty source");
    source.HiddenPaths.Clear();
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
    Check(service.IsCurrent(snapshot, link), "unchanged refresh preserves open snapshot selection");
    Check(service.IsCurrent(next, next.Entries.Single(x => x.Name == "sample.lnk")), "new generation can launch");
    source.HiddenPaths.Add("分類");
    var hidden = await Stable(source);
    Check(hidden.Entries.Count == 1 && File.Exists(url.FullPath), "hiding classification is non-destructive and hides descendants");
    source.HiddenPaths.Add("sample.lnk");
    var allHidden = await Stable(source);
    Check(allHidden.Entries.Count == 0 && allHidden.Status.Contains("非表示") && !allHidden.Status.Contains("空"), "all hidden status is explicit");
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

