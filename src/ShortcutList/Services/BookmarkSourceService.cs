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
    private const int EntryLimit = 10000;
    private const int VisitLimit = 20000;
    private const int DepthLimit = 16;
    private const string ChangedStatus = "変更を検知しました。再読込してください";
    private static readonly SemaphoreSlim ScanGate = new(4, 4);
    private sealed class State
    {
        public readonly object Gate = new();
        public long Revision;
        public long ScanNumber;
        public string Path = string.Empty;
        public HashSet<string> HiddenPaths = new(StringComparer.OrdinalIgnoreCase);
        public BookmarkSnapshot? Snapshot;
        public Task<BookmarkSnapshot>? Pending;
        public CancellationTokenSource? Cancellation;
        public FileSystemWatcher? Watcher;
        public bool WatcherHealthy;
        public long RootCreated;
        public volatile bool Removed;
    }
    private readonly ConcurrentDictionary<string, State> _states = new();
    private readonly object _lifetimeGate = new();
    private volatile bool _disposed;
    private long _nextRevision;

    public BookmarkSnapshot GetSnapshot(string id)
    {
        if (!_states.TryGetValue(id, out var state)) return new(id, 0, Array.Empty<BookmarkEntry>(), "読込中", false);
        lock (state.Gate)
        {
            if (state.Removed || _disposed) return new(id, 0, Array.Empty<BookmarkEntry>(), "連携解除済み", false);
            if (state.Snapshot is not { } snapshot) return new(id, 0, Array.Empty<BookmarkEntry>(), "読込中", false);
            return snapshot.Revision == state.Revision && state.WatcherHealthy ? snapshot :
                snapshot with { CanLaunch = false, Status = snapshot.CanLaunch ? ChangedStatus : snapshot.Status };
        }
    }

    public Task<BookmarkSnapshot> RefreshAsync(BookmarkSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Capture the configuration before dispatching; a mutable source must not
        // change the meaning of an already-running scan or its eventual result.
        var id = source.Id;
        var path = source.FolderPath;
        var hidden = new HashSet<string>(source.HiddenPaths ?? new(), StringComparer.OrdinalIgnoreCase);
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var state = _states.GetOrAdd(id, _ => new State());
            lock (state.Gate)
            {
                var pathChanged = !string.Equals(state.Path, path, StringComparison.Ordinal);
                var configurationChanged = pathChanged || !state.HiddenPaths.SetEquals(hidden);
                if (configurationChanged)
                {
                    state.Cancellation?.Cancel();
                    state.Revision = NextRevision();
                    if (pathChanged) { StopWatcher(state); state.Snapshot = null; }
                    state.Path = path;
                    state.HiddenPaths = hidden;
                }
                // Coalesce only the same, still-current request. A path or hidden
                // rule edit must never get the previous configuration's task.
                if (!configurationChanged && state.Pending is { IsCompleted: false } &&
                    state.Cancellation is { IsCancellationRequested: false }) return state.Pending;
                state.Cancellation?.Cancel();
                if (state.Revision == 0) state.Revision = NextRevision();
                var revision = state.Revision;
                var scanNumber = ++state.ScanNumber;
                var cancellation = new CancellationTokenSource();
                state.Cancellation = cancellation;
                state.Pending = Task.Run(() => ScanAsync(id, path, hidden, state, revision, scanNumber, cancellation));
                return state.Pending;
            }
        }
    }

    private async Task<BookmarkSnapshot> ScanAsync(string id, string path, HashSet<string> hidden, State state,
        long revision, long scanNumber, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var entered = false;
        try
        {
            await ScanGate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            return Scan(id, path, hidden, state, revision, scanNumber, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Superseded(id, revision, state); }
        finally
        {
            if (entered) ScanGate.Release();
            lock (state.Gate)
                if (ReferenceEquals(state.Cancellation, cancellation)) state.Cancellation = null;
            cancellation.Dispose();
        }
    }

    private BookmarkSnapshot Scan(string id, string path, HashSet<string> hidden, State state,
        long revision, long scanNumber, CancellationToken token)
    {
        BookmarkSnapshot result;
        try
        {
            token.ThrowIfCancellationRequested();
            var root = new DirectoryInfo(path);
            if (!root.Exists) throw new DirectoryNotFoundException("登録元を利用できません");
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンクされたフォルダーは登録元にできません");
            lock (state.Gate)
            {
                if (!IsActive(state, revision, scanNumber, token)) return Superseded(id, revision, state);
                if (state.Watcher is not null && state.RootCreated != root.CreationTimeUtc.Ticks)
                {
                    StopWatcher(state);
                    state.Revision = NextRevision();
                    return Superseded(id, revision, state);
                }
                if (state.Watcher is null) StartWatcher(path, root.CreationTimeUtc.Ticks, state);
            }
            var entries = new List<BookmarkEntry>();
            var pending = new Stack<(DirectoryInfo Folder, int Depth)>();
            pending.Push((root, 0));
            var truncated = false;
            var skipped = 0;
            var visited = 0;
            var hiddenCount = 0;
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (folder, depth) = pending.Pop();
                try
                {
                    // A queued directory may have been replaced since discovery.
                    // Recheck before descending; never intentionally follow links.
                    folder.Refresh();
                    if (!folder.Exists) { skipped++; continue; }
                    if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    // MoveNext performs deferred IO, so the catch must surround
                    // enumeration itself rather than only constructing IEnumerable.
                    foreach (var child in folder.EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > VisitLimit) { truncated = true; break; }
                        try
                        {
                            if ((child.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                            var relative = Path.GetRelativePath(root.FullName, child.FullName);
                            if (IsHidden(relative, hidden)) { hiddenCount++; continue; }
                            var isFolder = child is DirectoryInfo;
                            var fingerprint = $"{child.CreationTimeUtc.Ticks}:{child.LastWriteTimeUtc.Ticks}:{(child is FileInfo f ? f.Length : 0)}:{isFolder}";
                            entries.Add(new(id, relative, child.FullName, isFolder, fingerprint));
                            if (entries.Count >= EntryLimit) { truncated = true; break; }
                            if (isFolder && depth < DepthLimit) pending.Push(((DirectoryInfo)child, depth + 1));
                            else if (isFolder) truncated = true;
                        }
                        catch (Exception ex) when (IsReadFailure(ex)) { skipped++; }
                    }
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    if (depth == 0) throw; // An inaccessible root is offline, not an empty usable source.
                    skipped++;
                }
                if (entries.Count >= EntryLimit || visited > VisitLimit) break;
            }
            var ordered = Array.AsReadOnly(entries.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RelativePath, StringComparer.Ordinal).ToArray());
            var status = ordered.Count > 0 ? $"{ordered.Count} 件" :
                hiddenCount > 0 ? "表示対象はすべて非表示" :
                skipped > 0 ? "表示できる項目がありません" : "空のフォルダー";
            if (truncated) status += "（上限到達。登録元を開くと全件に到達できます）";
            if (skipped > 0) status += $" / {skipped} 件のリンク・読取不可を除外";
            lock (state.Gate)
            {
                if (!IsActive(state, revision, scanNumber, token)) return Superseded(id, revision, state);
                if (!state.WatcherHealthy) status += " / 監視不可: 元フォルダーから開いてください";
                result = new(id, revision, ordered, status, state.WatcherHealthy, truncated);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Superseded(id, revision, state); }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            lock (state.Gate)
            {
                if (!IsActive(state, revision, scanNumber, token)) return Superseded(id, revision, state);
                StopWatcher(state);
            }
            result = new(id, revision, Array.Empty<BookmarkEntry>(), "オフライン / 読取不可: " + ex.Message, false);
        }
        lock (state.Gate)
        {
            // Superseded work cannot overwrite a newer path, filter, scan, or
            // removal, even when its filesystem call completed out of order.
            if (!IsActive(state, revision, scanNumber, token)) return result with { CanLaunch = false, Status = ChangedStatus };
            if (state.Snapshot is { } previous &&
                (previous.CanLaunch != result.CanLaunch || !previous.Entries.SequenceEqual(result.Entries)))
                result = result with { Revision = state.Revision = NextRevision() };
            state.Snapshot = result;
        }
        return result;
    }

    private void StartWatcher(string path, long rootCreated, State state)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                    NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime
            };
            FileSystemEventHandler invalidate = (_, _) => Invalidate(state, watcher);
            watcher.Changed += invalidate; watcher.Created += invalidate; watcher.Deleted += invalidate;
            watcher.Renamed += (_, _) => Invalidate(state, watcher);
            watcher.Error += (_, _) =>
            {
                lock (state.Gate)
                {
                    if (!ReferenceEquals(state.Watcher, watcher) || state.Removed) return;
                    state.WatcherHealthy = false; state.Watcher = null;
                    state.Revision = NextRevision();
                    state.Cancellation?.Cancel();
                }
                ThreadPool.QueueUserWorkItem(_ => watcher.Dispose());
            };
            state.Watcher = watcher;
            state.RootCreated = rootCreated;
            watcher.EnableRaisingEvents = true;
            state.WatcherHealthy = true;
        }
        catch (Exception ex) when (IsReadFailure(ex)) { StopWatcher(state); }
    }

    public bool IsCurrent(BookmarkSnapshot snapshot, BookmarkEntry entry)
    {
        if (_disposed || snapshot.SourceId != entry.SourceId || !_states.TryGetValue(entry.SourceId, out var state)) return false;
        lock (state.Gate)
            return !state.Removed && state.WatcherHealthy && snapshot.CanLaunch && snapshot.Revision == state.Revision &&
                state.Snapshot is { CanLaunch: true } current && current.Revision == state.Revision &&
                snapshot.Entries.Contains(entry) && current.Entries.Contains(entry);
    }

    private bool IsActive(State state, long revision, long scanNumber, CancellationToken token) =>
        !_disposed && !state.Removed && !token.IsCancellationRequested &&
        scanNumber == state.ScanNumber && revision == state.Revision;

    private BookmarkSnapshot Superseded(string id, long revision, State state) =>
        new(id, revision, Array.Empty<BookmarkEntry>(), state.Removed || _disposed ? "連携解除済み" : ChangedStatus, false);

    private long NextRevision() => Interlocked.Increment(ref _nextRevision);
    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException;

    private static bool IsHidden(string relative, HashSet<string> hidden)
    {
        if (hidden.Contains(relative)) return true;
        var parent = Path.GetDirectoryName(relative);
        while (!string.IsNullOrEmpty(parent))
        {
            if (hidden.Contains(parent)) return true;
            parent = Path.GetDirectoryName(parent);
        }
        return false;
    }

    private static void StopWatcher(State state)
    {
        var watcher = state.Watcher;
        state.Watcher = null;
        state.WatcherHealthy = false;
        watcher?.Dispose();
    }

    private void Invalidate(State state, FileSystemWatcher watcher)
    {
        lock (state.Gate)
        {
            if (state.Removed || !ReferenceEquals(state.Watcher, watcher)) return;
            state.Revision = NextRevision();
            state.Cancellation?.Cancel();
        }
    }

    public void Remove(string id)
    {
        lock (_lifetimeGate)
        {
            if (!_states.TryRemove(id, out var state)) return;
            lock (state.Gate)
            {
                state.Removed = true;
                state.Revision = NextRevision();
                state.Cancellation?.Cancel();
                StopWatcher(state);
            }
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            _disposed = true;
            foreach (var id in _states.Keys) Remove(id);
        }
    }
}
