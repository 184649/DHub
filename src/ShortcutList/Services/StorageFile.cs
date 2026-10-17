using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ShortcutList.Services;

/// <summary>Single-file commit primitive. This does not claim multi-file or power-loss atomicity.</summary>
internal static class StorageFile
{
    public static bool Exists(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0) throw new IOException($"保存ファイルの場所がフォルダーです: {path}");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public static string? Fingerprint(string path) => Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    public static string FingerprintText(string text) => Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));

    public static void AtomicWrite<T>(string path, string json, Func<string, T> validate, bool backupValidatedDestination)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("保存先にフォルダーが必要です。", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Keep failed temporary files as recovery evidence. A later successful save never
        // reuses or auto-promotes an uncommitted temporary file.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = new UTF8Encoding(false).GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        _ = validate(File.ReadAllText(temporary, Encoding.UTF8));
        if (Exists(path))
            File.Replace(temporary, path, backupValidatedDestination ? path + ".bak" : null);
        else
            File.Move(temporary, path); // First creation: same-directory rename, no Delete -> Move window.
    }
}
