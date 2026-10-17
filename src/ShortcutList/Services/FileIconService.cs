using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShortcutList.Models;
using Drawing = System.Drawing;

namespace ShortcutList.Services;

public static class FileIconService
{
    public static bool IsEnabled { get; set; } = true;
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(4, 4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetIcon(ShortcutItem item)
    {
        if (!IsEnabled) return null;
        // Cache keys and binding getters never probe the filesystem.
        var key = $"{item.ShortcutType}|{item.TargetPath}|{item.OpenApplicationPath}";
        if (Cache.TryGetValue(key, out var cached))
        {
            if (!cached.IsCompleted) _ = NotifyWhenReadyAsync(cached, item);
            return cached.IsCompletedSuccessfully ? cached.Result : null;
        }
        if (Cache.Count >= 2048) return null;
        var snapshot = new ShortcutItem { TargetPath = item.TargetPath, ShortcutType = item.ShortcutType,
            OpenApplicationPath = item.OpenApplicationPath };
        var task = Cache.GetOrAdd(key, _ => LoadIconAsync(snapshot));
        _ = NotifyWhenReadyAsync(task, item);
        return task.IsCompletedSuccessfully ? task.Result : null;
    }

    private static async Task<ImageSource?> LoadIconAsync(ShortcutItem item)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(() => LoadIcon(item)).ConfigureAwait(false); }
        catch { return null; }
        finally { Gate.Release(); }
    }

    private static async Task NotifyWhenReadyAsync(Task<ImageSource?> task, ShortcutItem item)
    {
        await task.ConfigureAwait(false);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            _ = dispatcher.BeginInvoke(new Action(item.RefreshIcon));
    }

    private static ImageSource? LoadIcon(ShortcutItem item)
    {
        string? iconSourcePath = null;

        if (item.ShortcutType == ShortcutType.File && File.Exists(item.TargetPath))
        {
            iconSourcePath = item.TargetPath;
        }
        else if (!string.IsNullOrWhiteSpace(item.OpenApplicationPath) && File.Exists(item.OpenApplicationPath))
        {
            iconSourcePath = item.OpenApplicationPath;
        }
        else if (item.ShortcutType == ShortcutType.Folder)
        {
            iconSourcePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        }

        if (string.IsNullOrWhiteSpace(iconSourcePath) || !File.Exists(iconSourcePath))
        {
            return null;
        }

        var icon = Drawing.Icon.ExtractAssociatedIcon(iconSourcePath);
        if (icon is null) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            source.Freeze();
            return source;
        }
        finally
        {
            icon.Dispose();
        }
    }
}

