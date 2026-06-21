using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

internal static class Program
{
    private const string AppExeName = "DHub.exe";
    private const string UpdaterExeName = "DHubUpdater.exe";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                return 1;
            }

            var appZipPath = args[0];
            var targetExePath = args[1];

            if (!File.Exists(appZipPath))
            {
                return 2;
            }

            var targetDir = Path.GetDirectoryName(targetExePath);
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                return 3;
            }

            var extractDir = Path.Combine(
                Path.GetTempPath(),
                "DHub_Update_Extract_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(appZipPath, extractDir, overwriteFiles: true);

            var newExePath = Directory
                .EnumerateFiles(extractDir, AppExeName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(newExePath) || !File.Exists(newExePath))
            {
                return 4;
            }

            var sourceDir = Path.GetDirectoryName(newExePath);
            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                return 5;
            }

            WaitForTargetToUnlock(targetExePath, TimeSpan.FromSeconds(60));

            var backupDir = Path.Combine(
                Path.GetTempPath(),
                "DHub_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDir);

            BackupExistingFiles(targetDir, backupDir);
            CopyDirectory(sourceDir, targetDir);

            Process.Start(new ProcessStartInfo
            {
                FileName = targetExePath,
                UseShellExecute = true
            });

            TryDeleteDirectory(extractDir);
            TryDeleteFile(appZipPath);

            return 0;
        }
        catch
        {
            return 9;
        }
    }

    private static void WaitForTargetToUnlock(string targetExePath, TimeSpan timeout)
    {
        var start = DateTime.Now;

        while (DateTime.Now - start < timeout)
        {
            try
            {
                if (!File.Exists(targetExePath))
                {
                    return;
                }

                using var stream = new FileStream(targetExePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void BackupExistingFiles(string targetDir, string backupDir)
    {
        foreach (var file in Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(targetDir, file);
            var destination = Path.Combine(backupDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(file, destination, overwrite: true);
            }
            catch
            {
                // バックアップできないファイルがあっても更新自体は継続します。
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);

            // 実行中のアップデーター自身は、別途ダウンロードされた一時フォルダから動いているため、
            // アプリ配布ZIPに含まれていても上書き失敗の原因にならないようにスキップします。
            if (string.Equals(fileName, UpdaterExeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
        }
    }
}
