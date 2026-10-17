using System.Collections.Concurrent;
using System.IO;
using ShortcutList.Models;

namespace ShortcutList.Services;

public sealed record BookmarkEntry(string SourceId, string RelativePath, string FullPath, bool IsFolder, string Fingerprint)
{
    public string Name => Path.GetFileName(FullPath);
}
public sealed record BookmarkSnapshot(string SourceId, long Revision, IReadOnlyList<BookmarkEntry> Entries,
    string Status, bool CanLaunch, bool IsTruncated = false);

// Enumeration/metadata are exclusively background work. UI always reads a snapshot.
public sealed class BookmarkSourceService : IDisposable
{
    private sealed class State
    {
        public readonly object Gate = new();
        public long Revision;
        public string Path = string.Empty;
        public string HiddenKey = string.Empty;
        public BookmarkSnapshot? Snapshot;
        public Task<BookmarkSnapshot>? Pending;
        public FileSystemWatcher? Watcher;
        public bool WatcherHealthy;
        public long RootCreated;
        public bool Removed;
    }
    private readonly ConcurrentDictionary<string, State> _states = new();
    private volatile bool _disposed;
    private long _nextRevision;
    public BookmarkSnapshot GetSnapshot(string id)
    {
        if (!_states.TryGetValue(id, out var state)) return new(id, 0, Array.Empty<BookmarkEntry>(), "読込中", false);
        lock (state.Gate)
        {
            if (state.Snapshot is not { } snapshot) return new(id, 0, Array.Empty<BookmarkEntry>(), "読込中", false);
            return snapshot.Revision == state.Revision ? snapshot :
                snapshot with { CanLaunch = false, Status = "変更を検知しました。再読込してください" };
        }
    }

    public Task<BookmarkSnapshot> RefreshAsync(BookmarkSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var state = _states.GetOrAdd(source.Id, _ => new State());
        lock (state.Gate)
        {
            if (state.Pending is { IsCompleted: false }) return state.Pending;
            var path = source.FolderPath;
            if (!string.Equals(state.Path, path, StringComparison.Ordinal))
            {
                state.Watcher?.Dispose(); state.Watcher = null; state.WatcherHealthy = false; state.Snapshot = null;
            }
            var hidden = new HashSet<string>(source.HiddenPaths ?? new(), StringComparer.OrdinalIgnoreCase);
            var hiddenKey = string.Join("\n", hidden.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            if (state.Revision == 0 || state.Snapshot is null || state.HiddenKey != hiddenKey)
                state.Revision = Interlocked.Increment(ref _nextRevision);
            state.HiddenKey = hiddenKey;
            var revision = state.Revision;
            state.Path = path;
            state.Pending = Task.Run(() => Scan(source.Id, path, hidden, state, revision));
            return state.Pending;
        }
    }

    private BookmarkSnapshot Scan(string id, string path, HashSet<string> hidden, State state, long revision)
    {
        BookmarkSnapshot result;
        try
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists) throw new DirectoryNotFoundException("登録元を利用できません");
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンクされたフォルダーは登録元にできません");
            lock (state.Gate)
            {
                if (state.Watcher is not null && state.RootCreated != root.CreationTimeUtc.Ticks)
                {
                    state.Watcher.Dispose(); state.Watcher = null; state.WatcherHealthy = false;
                    Interlocked.Exchange(ref state.Revision, Interlocked.Increment(ref _nextRevision));
                }
                if (!state.Removed && state.Watcher is null)
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                        FileSystemEventHandler invalidate = (_, _) => Invalidate(state);
                        watcher.Changed += invalidate; watcher.Created += invalidate; watcher.Deleted += invalidate;
                        watcher.Renamed += (_, _) => Invalidate(state);
                        watcher.Error += (_, _) =>
                        {
                            lock (state.Gate)
                            {
                                if (!ReferenceEquals(state.Watcher, watcher)) return;
                                state.WatcherHealthy = false; state.Watcher = null;
                                Interlocked.Exchange(ref state.Revision, Interlocked.Increment(ref _nextRevision));
                            }
                            ThreadPool.QueueUserWorkItem(_ => watcher.Dispose());
                        };
                        state.Watcher = watcher;
                        state.RootCreated = root.CreationTimeUtc.Ticks;
                        watcher.EnableRaisingEvents = true;
                        state.WatcherHealthy = true;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { state.Watcher?.Dispose(); state.Watcher = null; state.WatcherHealthy = false; }
                }
            }
            var entries = new List<BookmarkEntry>();
            var pending = new Stack<(DirectoryInfo Folder, int Depth)>();
            pending.Push((root, 0));
            var truncated = false;
            var skipped = 0;
            var visited = 0;
            while (pending.Count > 0)
            {
                if (state.Removed || _disposed) return new(id, revision, Array.Empty<BookmarkEntry>(), "連携解除済み", false);
                var (folder, depth) = pending.Pop();
                IEnumerable<FileSystemInfo> children;
                try { children = folder.EnumerateFileSystemInfos(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
                foreach (var child in children)
                {
                    if (++visited > 20000) { truncated = true; break; }
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    var relative = Path.GetRelativePath(path, child.FullName);
                    if (hidden.Any(h => string.Equals(relative, h, StringComparison.OrdinalIgnoreCase) ||
                        relative.StartsWith(h + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                    var isFolder = child is DirectoryInfo;
                    var fingerprint = $"{child.CreationTimeUtc.Ticks}:{child.LastWriteTimeUtc.Ticks}:{(child is FileInfo f ? f.Length : 0)}:{isFolder}";
                    entries.Add(new(id, relative, child.FullName, isFolder, fingerprint));
                    if (entries.Count >= 10000) { truncated = true; break; }
                    if (isFolder && depth < 16) pending.Push(((DirectoryInfo)child, depth + 1));
                    else if (isFolder) truncated = true;
                }
                if (entries.Count >= 10000 || visited > 20000) break;
            }
            var ordered = entries.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            var status = ordered.Length == 0 ? (hidden.Count > 0 ? "空、または全項目が非表示" : "空のフォルダー") : $"{ordered.Length} 件";
            if (truncated) status += "（上限到達。登録元を開くと全件に到達できます）";
            if (skipped > 0) status += $" / {skipped} 件のリンク・読取不可を除外";
            if (!state.WatcherHealthy) status += " / 監視不可: 元フォルダーから開いてください";
            result = new(id, revision, ordered, status, state.WatcherHealthy, truncated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            lock (state.Gate) { state.Watcher?.Dispose(); state.Watcher = null; state.WatcherHealthy = false; }
            result = new(id, revision, Array.Empty<BookmarkEntry>(), "オフライン / 読取不可: " + ex.Message, false);
        }
        lock (state.Gate)
        {
            if (!state.Removed && revision == state.Revision)
            {
                if (state.Snapshot is { } previous &&
                    (previous.CanLaunch != result.CanLaunch || !previous.Entries.SequenceEqual(result.Entries)))
                    result = result with { Revision = state.Revision = Interlocked.Increment(ref _nextRevision) };
                state.Snapshot = result;
            }
            else
            {
                result = result with { CanLaunch = false, Status = "変更を検知しました。再読込してください" };
                if (!state.Removed) state.Snapshot = result;
            }
        }
        return result;
    }

    public bool IsCurrent(BookmarkSnapshot snapshot, BookmarkEntry entry)
    {
        if (!_states.TryGetValue(entry.SourceId, out var state)) return false;
        lock (state.Gate)
            return !state.Removed && state.WatcherHealthy && snapshot.CanLaunch && snapshot.Revision == state.Revision &&
                state.Snapshot?.Entries.Any(x => x.RelativePath == entry.RelativePath && x.Fingerprint == entry.Fingerprint) == true;
    }

    private void Invalidate(State state)
    {
        lock (state.Gate)
            if (!state.Removed) state.Revision = Interlocked.Increment(ref _nextRevision);
    }

    public void Remove(string id)
    {
        if (!_states.TryRemove(id, out var state)) return;
        lock (state.Gate) { state.Removed = true; state.Revision++; state.Watcher?.Dispose(); }
    }
    public void Dispose() { _disposed = true; foreach (var id in _states.Keys) Remove(id); }
}
