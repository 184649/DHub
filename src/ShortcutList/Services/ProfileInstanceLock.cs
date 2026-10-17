using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ShortcutList.Services;

// Keep this guard on the startup/UI thread until Application.OnExit. Named
// mutex ownership is thread-affine; hiding or closing a window must not release it.
internal sealed class ProfileInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private readonly FileStream _file;
    private bool _disposed;

    private ProfileInstanceLock(Mutex mutex, FileStream file) { _mutex = mutex; _file = file; }

    internal static ProfileInstanceLock? TryAcquire(string dataDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var profileId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())));
        var mutex = new Mutex(false, "Local\\DHub-" + profileId);
        var ownsMutex = false;
        try
        {
            try { ownsMutex = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) { mutex.Dispose(); return null; }
            Directory.CreateDirectory(directory);
            // File sharing also excludes aliases and other Windows sessions.
            // Retain this harmless empty sentinel after exit: deleting a lock
            // path while another process is opening it can split lock ownership.
            var file = new FileStream(Path.Combine(directory, ".instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            return new ProfileInstanceLock(mutex, file);
        }
        catch
        {
            if (ownsMutex) mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _file.Dispose(); }
        finally
        {
            try { _mutex.ReleaseMutex(); }
            finally { _mutex.Dispose(); }
        }
    }
}
