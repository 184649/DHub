using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ShortcutList.Models;

namespace ShortcutList.Services;

public class UpdateService
{
    private readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<VersionInfo?> CheckAsync(string versionUrl)
    {
        if (string.IsNullOrWhiteSpace(versionUrl))
        {
            return null;
        }

        var json = (await _client.GetStringAsync(versionUrl)).TrimStart('\uFEFF');

        return JsonSerializer.Deserialize<VersionInfo>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public bool IsNewer(string remoteVersion)
    {
        if (!Version.TryParse(remoteVersion, out var remote))
        {
            return false;
        }

        if (!Version.TryParse(CurrentVersion, out var current))
        {
            current = new Version(1, 0, 0);
        }

        return remote > current;
    }

    public async Task<string> DownloadAppZipAsync(string downloadUrl)
    {
        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"DHub_Update_{Guid.NewGuid():N}.zip");

        var bytes = await _client.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(tempZip, bytes);

        return tempZip;
    }

    public async Task<string?> DownloadAndExtractUpdaterAsync(string updaterUrl)
    {
        if (string.IsNullOrWhiteSpace(updaterUrl))
        {
            return null;
        }

        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"DHubUpdater_{Guid.NewGuid():N}.zip");

        var extractDir = Path.Combine(
            Path.GetTempPath(),
            $"DHubUpdater_{Guid.NewGuid():N}");

        var bytes = await _client.GetByteArrayAsync(updaterUrl);
        await File.WriteAllBytesAsync(tempZip, bytes);

        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);

        var updaterExe = Directory
            .EnumerateFiles(extractDir, "DHubUpdater.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        return updaterExe;
    }

    public void ReplaceCurrentExeAndRestartFromZip(string downloadedAppZip, string? downloadedUpdater = null)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            return;
        }

        var updaterPath = downloadedUpdater;

        if (string.IsNullOrWhiteSpace(updaterPath) || !File.Exists(updaterPath))
        {
            updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DHubUpdater.exe");
        }

        if (!File.Exists(updaterPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = "\"" + downloadedAppZip + "\" \"" + currentExe + "\"",
            UseShellExecute = true
        });

        System.Windows.Application.Current.Shutdown();
    }
}
