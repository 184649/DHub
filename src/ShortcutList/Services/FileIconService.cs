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
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetIcon(ShortcutItem item)
    {
        if (!IsEnabled) return null;
        try
        {
            var key = BuildCacheKey(item);
            if (string.IsNullOrWhiteSpace(key)) return null;
            return Cache.GetOrAdd(key, _ => LoadIcon(item));
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCacheKey(ShortcutItem item)
    {
        if (item.ShortcutType == ShortcutType.Url)
        {
            var app = item.OpenApplicationPath;
            if (!string.IsNullOrWhiteSpace(app)) return "url:" + app;
            return "url";
        }

        if (item.ShortcutType == ShortcutType.Folder)
        {
            return "folder";
        }

        var path = item.TargetPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        var ext = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(ext) ? "file" : "ext:" + ext;
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
