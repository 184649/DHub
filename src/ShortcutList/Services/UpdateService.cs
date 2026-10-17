using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using DHub.Updating;
using ShortcutList.Models;

namespace ShortcutList.Services;

public sealed class UpdateService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private bool _updateStarted;

    public string CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public bool IsPreviewBuild => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Contains("-preview", StringComparison.OrdinalIgnoreCase) == true;

    public async Task<VersionInfo?> CheckAsync(string versionUrl, CancellationToken cancellationToken = default)
    {
        if (IsPreviewBuild) throw new InvalidOperationException("検証版は自動更新の対象外です。このブランチから再ビルドして更新してください。");
        if (string.IsNullOrWhiteSpace(versionUrl)) return null;
        ValidateManifestUrl(versionUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await Client.GetAsync(versionUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 128 * 1024) throw new InvalidDataException("更新情報が大きすぎます。");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (data.Length + read > 128 * 1024) throw new InvalidDataException("更新情報が大きすぎます。");
            data.Write(buffer, 0, read);
        }
        var manifest = DeserializeManifest(data.ToArray());
        if (manifest is null || !Version.TryParse(manifest.Version, out _)) throw new InvalidDataException("version.json のバージョンが不正です。");
        return manifest;
    }

    public bool IsNewer(string remoteVersion) => Version.TryParse(remoteVersion, out var remote) &&
        Version.TryParse(CurrentVersion, out var current) && remote > current;

    public async Task<bool> StartUpdateAsync(VersionInfo info, string? dataDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!await _updateGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return false;
        string? workDirectory = null;
        var launched = false;
        try
        {
            if (_updateStarted) return false;
            if (IsPreviewBuild) throw new InvalidOperationException("検証版は自動更新の対象外です。このブランチから再ビルドして更新してください。");
            if (!IsNewer(info.Version)) throw new InvalidDataException("更新先は現在より新しいバージョンである必要があります。");
            UpdatePackageSafety.ValidatePackageUrl(info.DownloadUrl);
            UpdatePackageSafety.ValidatePackageUrl(info.UpdaterUrl);
            UpdatePackageSafety.ValidateHash(info.DownloadSha256);
            UpdatePackageSafety.ValidateHash(info.UpdaterSha256);
            var currentExe = Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException("実行中のアプリのパスが見つかりません。"));
            if (!Path.GetFileName(currentExe).Equals("DHub.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("自動更新は配布版 DHub.exe から実行してください。");
            var appDirectory = Path.GetDirectoryName(currentExe)!;
            if (File.Exists(Path.Combine(appDirectory, "DHub.dll")) || File.Exists(Path.Combine(appDirectory, "DHub.runtimeconfig.json")))
                throw new InvalidOperationException("自動更新は単一 EXE の配布版のみ対応しています。開発用ビルドは手動でビルドまたは更新してください。");
            dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHub"));
            var probe = Path.Combine(appDirectory, $".dhub-write-test-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            workDirectory = Path.Combine(Path.GetTempPath(), "DHubUpdates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            var updaterZip = Path.Combine(workDirectory, "DHubUpdater.zip");
            var appZip = Path.Combine(workDirectory, "DHub.zip");
            await UpdatePackageSafety.DownloadVerifiedAsync(info.UpdaterUrl, updaterZip, info.UpdaterSha256, cancellationToken).ConfigureAwait(false);
            await UpdatePackageSafety.DownloadVerifiedAsync(info.DownloadUrl, appZip, info.DownloadSha256, cancellationToken).ConfigureAwait(false);
            var updaterDirectory = Path.Combine(workDirectory, "updater");
            var updaterExe = await UpdatePackageSafety.ExtractExecutableAsync(updaterZip, updaterDirectory, "DHubUpdater.exe", cancellationToken).ConfigureAwait(false);
            await UpdatePackageSafety.ExtractExecutableAsync(appZip, Path.Combine(workDirectory, "app-check"), "DHub.exe", cancellationToken).ConfigureAwait(false);
            var readyFile = Path.Combine(workDirectory, "ready");
            var proceedFile = Path.Combine(workDirectory, "proceed");
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = CreateUpdaterStartInfo(updaterExe, Environment.ProcessId, currentExe, appZip, info.DownloadSha256, dataDirectory, readyFile, proceedFile);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("更新プログラムを起動できませんでした。");
            launched = true;
            // The old application remains open until the updater has verified and staged its replacement.
            await WaitUntilReadyAsync(process, readyFile, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            SignalProceed(proceedFile);
            // Commit point: cancellation after this signal must not report an aborted update.
            _updateStarted = true;
            return true;
        }
        finally
        {
            if (!launched && workDirectory is not null)
            {
                try { Directory.Delete(workDirectory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _updateGate.Release();
        }
    }

    internal static ProcessStartInfo CreateUpdaterStartInfo(string updaterExe, int processId, string currentExe, string appZip, string hash, string dataDirectory, string readyFile, string proceedFile)
    {
        var start = new ProcessStartInfo { FileName = updaterExe, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(updaterExe)! };
        foreach (var argument in new[] { "--processId", processId.ToString(), "--appPath", currentExe, "--appZipPath", appZip,
                     "--appSha256", hash, "--dataDirectory", dataDirectory, "--readyFile", readyFile, "--proceedFile", proceedFile, "--restart" }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void SignalProceed(string path) => UpdatePackageSafety.WriteSignal(path);

    internal static async Task WaitUntilReadyAsync(Process process, string readyFile, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException($"更新プログラムが準備中に終了しました (終了コード {process.ExitCode})。元のアプリは変更されていません。updater.log を確認してください。");
            if (UpdatePackageSafety.HasValidSignal(readyFile)) return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("更新プログラムの準備が時間内に完了しませんでした。元のアプリは終了していません。");
    }

    internal static Uri ValidateManifestUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            uri.Host != "raw.githubusercontent.com" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.AbsolutePath.StartsWith("/184649/DHub/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/Chairman-bits/DHub/", StringComparison.Ordinal)) ||
            !uri.AbsolutePath.EndsWith("/version.json", StringComparison.Ordinal))
            throw new InvalidDataException("更新情報 URL は DHub の公式 GitHub リポジトリにある HTTPS の version.json を指定してください。");
        return uri;
    }

    internal static VersionInfo? DeserializeManifest(byte[] bytes)
    {
        ReadOnlySpan<byte> json = bytes;
        if (json.Length >= 3 && json[0] == 0xef && json[1] == 0xbb && json[2] == 0xbf) json = json[3..];
        return JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions);
    }
}
