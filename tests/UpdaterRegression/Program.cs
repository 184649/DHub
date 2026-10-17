using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShortcutList.Services;
using ShortcutList.Models;
using DHub.Updating;
using Updater = DHubUpdater.Program;

namespace UpdaterRegression;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "DHub-StorageSafety-" + Guid.NewGuid().ToString("N"));
    private static int _failed;
    private static int _passed;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--exit-immediately") return 0;
        Directory.CreateDirectory(Root);
        // No production profile is used, even by tests which exercise default constructors.

        await PackageTests();
        await UpdaterTests();
        Console.WriteLine($"Storage safety: {_passed} passed, {_failed} failed.");
        if (args.Length > 0) File.WriteAllText(Path.GetFullPath(args[0]), JsonSerializer.Serialize(new { passed = _passed, failed = _failed, results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        try { Directory.Delete(Root, true); } catch (IOException) { }
        return _failed == 0 ? 0 : 1;
    }

    private static async Task Test(string name, Func<Task> action)
    {
        try
        {
            await action();
            _passed++;
            Results.Add(new { name, passed = true });
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Results.Add(new { name, passed = false, error = ex.ToString() });
            Console.WriteLine("FAIL " + name + ": " + ex);
        }
    }

    private static string Fixture(string name)
    {
        var path = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected [{expected}], actual [{actual}].");
    }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static Task Run(Action action) { action(); return Task.CompletedTask; }

    private static byte[] Executable(byte marker = 42)
    {
        // Minimal PE-shaped synthetic fixture, never executed by these tests.
        var bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x80;
        bytes[0x80] = 0x50; bytes[0x81] = 0x45; bytes[0x84] = 0x64; bytes[0x85] = 0x86;
        bytes[0x98] = 0x0b; bytes[0x99] = 0x02; bytes[0xf0] = marker; return bytes;
    }
    private static string Zip(string directory, IEnumerable<(string Name, byte[] Bytes, int Attributes)> entries)
    {
        var path = Path.Combine(directory, "package.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = zip.CreateEntry(item.Name); entry.ExternalAttributes = item.Attributes;
            using var output = entry.Open(); output.Write(item.Bytes);
        }
        return path;
    }

    private static async Task PackageTests()
    {
        await Test("Package verified SHA-256 and single executable extraction", async () =>
        {
            var directory = Fixture("valid-package"); var zip = Zip(directory, new[] { ("release/DHub.exe", Executable(), 0) });
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            await UpdatePackageSafety.VerifyFileHashAsync(zip, hash, CancellationToken.None);
            var exe = await UpdatePackageSafety.ExtractExecutableAsync(zip, Path.Combine(directory, "output"), "DHub.exe", CancellationToken.None);
            True(Executable().SequenceEqual(File.ReadAllBytes(exe)), "Extracted content differs.");
        });
        foreach (var badName in new[] { "../DHub.exe", "/DHub.exe", "C:/DHub.exe", "nested/../../DHub.exe", "..\\DHub.exe", "nested./DHub.exe", "nested /DHub.exe", "nested//DHub.exe", "folder/DHub.exe:stream", "folder/../DHub.exe" })
            await Test("Package rejects path " + badName, async () =>
            {
                var directory = Fixture("bad-path"); var zip = Zip(directory, new[] { (badName, Executable(), 0) });
                await Throws<InvalidDataException>(() => UpdatePackageSafety.ExtractExecutableAsync(zip, Path.Combine(directory, "output"), "DHub.exe", CancellationToken.None));
                True(!File.Exists(Path.Combine(directory, "DHub.exe")), "ZIP escaped its extraction directory.");
            });
        foreach (var scenario in new[] { "symlink", "reparse", "duplicate", "case-duplicate", "wrong-name", "extra-file", "not-pe", "bad-offset" })
            await Test("Package rejects " + scenario, async () =>
            {
                var directory = Fixture(scenario); var entries = new List<(string, byte[], int)> { ("DHub.exe", Executable(), 0) };
                if (scenario == "symlink") entries[0] = ("DHub.exe", Executable(), unchecked((int)0xA0000000));
                if (scenario == "reparse") entries[0] = ("DHub.exe", Executable(), 0x400);
                if (scenario == "duplicate") entries.Add(("DHub.exe", Executable(), 0));
                if (scenario == "case-duplicate") entries.Add(("dindex.EXE", Executable(), 0));
                if (scenario == "wrong-name") entries[0] = ("Other.exe", Executable(), 0);
                if (scenario == "extra-file") entries.Add(("payload.dll", Executable(), 0));
                if (scenario == "not-pe") entries[0] = ("DHub.exe", new byte[256], 0);
                if (scenario == "bad-offset") { var bytes = Executable(); bytes[0x3f] = 0x7f; entries[0] = ("DHub.exe", bytes, 0); }
                var zip = Zip(directory, entries);
                await Throws<InvalidDataException>(() => UpdatePackageSafety.ExtractExecutableAsync(zip, Path.Combine(directory, "output"), "DHub.exe", CancellationToken.None));
                True(!File.Exists(Path.Combine(directory, "output", "DHub.exe")), "Rejected executable remained on disk.");
            });
        await Test("Package refuses to overwrite or delete a preexisting output file", async () =>
        {
            var directory = Fixture("package-existing"); var zip = Zip(directory, new[] { ("DHub.exe", Executable(), 0) });
            var outputDirectory = Path.Combine(directory, "output"); Directory.CreateDirectory(outputDirectory);
            var output = Path.Combine(outputDirectory, "DHub.exe"); File.WriteAllText(output, "existing executable");
            await Throws<IOException>(() => UpdatePackageSafety.ExtractExecutableAsync(zip, outputDirectory, "DHub.exe", CancellationToken.None));
            Equal("existing executable", File.ReadAllText(output));
        });
        await Test("Manifest accepts Windows PowerShell UTF-8 BOM", () => Run(() =>
        {
            var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("{\"version\":\"1.2.3\"}")).ToArray();
            Equal("1.2.3", UpdateService.DeserializeManifest(bytes)!.Version);
        }));
        await Test("Package rejects changed hash and missing hash", async () =>
        {
            var path = Zip(Fixture("hash"), new[] { ("DHub.exe", Executable(), 0) });
            await Throws<InvalidDataException>(() => UpdatePackageSafety.VerifyFileHashAsync(path, new string('0', 64), CancellationToken.None));
            await Throws<InvalidDataException>(() => UpdatePackageSafety.VerifyFileHashAsync(path, "", CancellationToken.None));
        });
        await Test("Package cancellation does not extract files", async () =>
        {
            var directory = Fixture("package-cancel"); var path = Zip(directory, new[] { ("DHub.exe", Executable(), 0) });
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Throws<OperationCanceledException>(() => UpdatePackageSafety.ExtractExecutableAsync(path, Path.Combine(directory, "output"), "DHub.exe", cts.Token));
            True(!Directory.Exists(Path.Combine(directory, "output")), "Canceled extraction created files.");
        });
        foreach (var url in new[] { "http://raw.githubusercontent.com/Chairman-bits/DHub/main/DHub.zip", "https://evil.example/DHub.zip", "https://raw.githubusercontent.com/attacker/DHub/main/DHub.zip", "https://raw.githubusercontent.com@evil.example/DHub.zip", "https://github.com/Chairman-bits/DHub/other/DHub.zip", "https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHub.zip?redirect=1" })
            await Test("Package rejects untrusted URL " + url, () => Throws<InvalidDataException>(() => Run(() => UpdatePackageSafety.ValidatePackageUrl(url))));
        await Test("Package accepts official raw and release URLs", () => Run(() =>
        {
            UpdatePackageSafety.ValidatePackageUrl("https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHub.zip");
            UpdatePackageSafety.ValidatePackageUrl("https://github.com/Chairman-bits/DHub/releases/download/v1.0.1/DHub.zip");
        }));
        await Test("Update missing hashes stops before download with manual-update guidance", async () =>
        {
            var info = new VersionInfo
            {
                Version = "999.0.0", DownloadUrl = "https://raw.githubusercontent.com/184649/DHub/main/DHub.zip",
                UpdaterUrl = "https://raw.githubusercontent.com/184649/DHub/main/DHubUpdater.zip"
            };
            try { await new UpdateService().StartUpdateAsync(info); }
            catch (InvalidDataException exception) { True(exception.Message.Contains("手動"), "Manual-update guidance missing."); return; }
            throw new Exception("Missing hashes were accepted.");
        });
        foreach (var url in new[] { "https://evil.example/version.json", "http://raw.githubusercontent.com/184649/DHub/main/version.json", "https://raw.githubusercontent.com/184649/DHub/main/version.json?token=x", "https://raw.githubusercontent.com/184649/Other/main/version.json" })
            await Test("Manifest rejects untrusted URL " + url, () => Throws<InvalidDataException>(() => Run(() => UpdateService.ValidateManifestUrl(url))));
        await Test("Manifest accepts both verified repository aliases", () => Run(() =>
        {
            UpdateService.ValidateManifestUrl("https://raw.githubusercontent.com/184649/DHub/main/version.json");
            UpdateService.ValidateManifestUrl("https://raw.githubusercontent.com/Chairman-bits/DHub/main/version.json");
        }));

    }

    private static async Task UpdaterTests()
    {
        await Test("Updater atomic install retains backup and rollback restores original", () => Run(() =>
        {
            var directory = Fixture("install"); var app = Path.Combine(directory, "DHub.exe"); var staged = Path.Combine(directory, "new.exe");
            File.WriteAllBytes(app, Executable(1)); File.WriteAllBytes(staged, Executable(2));
            Updater.InstallVerifiedExecutable(staged, app);
            True(Executable(2).SequenceEqual(File.ReadAllBytes(app)), "New executable not installed.");
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app + ".bak")), "Original backup missing.");
            Updater.Rollback(app);
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app)), "Original not restored.");
            True(Executable(2).SequenceEqual(File.ReadAllBytes(app + ".failed")), "Failed update was not preserved.");
        }));
        await Test("Updater recovers Windows partial replacement failure", async () =>
        {
            var directory = Fixture("install-partial"); var app = Path.Combine(directory, "DHub.exe"); var staged = Path.Combine(directory, "new.exe");
            File.WriteAllBytes(app, Executable(1)); File.WriteAllBytes(staged, Executable(2));
            await Throws<IOException>(() => Run(() => Updater.InstallVerifiedExecutable(staged, app, (_, target, backup) =>
            {
                File.Move(target, backup); throw new IOException("Simulated ERROR_UNABLE_TO_MOVE_REPLACEMENT_2");
            })));
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app)), "Partial replacement did not restore the original executable.");
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app + ".bak")), "Partial replacement discarded backup.");
        });
        await Test("Updater recovers replacement failure after target changed", async () =>
        {
            var directory = Fixture("install-changed"); var app = Path.Combine(directory, "DHub.exe"); var staged = Path.Combine(directory, "new.exe");
            File.WriteAllBytes(app, Executable(1)); File.WriteAllBytes(staged, Executable(2));
            await Throws<IOException>(() => Run(() => Updater.InstallVerifiedExecutable(staged, app, (source, target, backup) =>
            {
                File.Copy(target, backup); File.Copy(source, target, true); throw new IOException("Simulated post-replacement failure");
            })));
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app)), "Changed target was not rolled back.");
            True(Executable(2).SequenceEqual(File.ReadAllBytes(app + ".failed")), "Failed new executable was not preserved.");
        });
        await Test("Updater rollback restores absent target and retains backup", () => Run(() =>
        {
            var directory = Fixture("rollback-missing"); var app = Path.Combine(directory, "DHub.exe"); File.WriteAllBytes(app + ".bak", Executable(1));
            Updater.Rollback(app);
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app)), "Absent target not restored.");
            True(File.Exists(app + ".bak"), "Rollback consumed the only backup.");
        }));
        await Test("Updater invalid executable never replaces original", async () =>
        {
            var directory = Fixture("install-invalid"); var app = Path.Combine(directory, "DHub.exe"); var staged = Path.Combine(directory, "new.exe");
            File.WriteAllBytes(app, Executable(1)); File.WriteAllText(staged, "not executable");
            await Throws<InvalidDataException>(() => Run(() => Updater.InstallVerifiedExecutable(staged, app)));
            True(Executable(1).SequenceEqual(File.ReadAllBytes(app)), "Invalid executable replaced original.");
        });
        await Test("Updater refuses duplicate/unknown/missing arguments", async () =>
        {
            await Throws<ArgumentException>(() => Run(() => Updater.ParseArgs(new[] { "--restart", "--restart" })));
            await Throws<ArgumentException>(() => Run(() => Updater.ParseArgs(new[] { "--unknown" })));
            await Throws<ArgumentException>(() => Run(() => Updater.ParseArgs(new[] { "--appPath" })));
            await Throws<ArgumentException>(() => Run(() => Updater.ParseArgs(new[] { "trailing" })));
        });
        await Test("Updater preserves selected profile and quoted argument boundaries", () => Run(() =>
        {
            var profile = Path.Combine(Root, "custom profile with spaces");
            var start = Updater.CreateRestartStartInfo(Path.Combine(Root, "DHub.exe"), profile);
            Equal(false, start.UseShellExecute); Equal("--data-dir", start.ArgumentList[0]); Equal(profile, start.ArgumentList[1]);
            var updaterStart = UpdateService.CreateUpdaterStartInfo(Path.Combine(Root, "DHubUpdater.exe"), 42, Path.Combine(Root, "DHub.exe"), Path.Combine(Root, "app.zip"), new string('a', 64), profile, Path.Combine(Root, "ready"), Path.Combine(Root, "proceed"));
            var parsed = Updater.ParseArgs(updaterStart.ArgumentList.ToArray()); Equal(profile, parsed["dataDirectory"]); Equal("42", parsed["processId"]);
        }));
        await Test("Updater handshake needs explicit proceed", async () =>
        {
            var directory = Fixture("handshake"); var ready = Path.Combine(directory, "ready"); var proceed = Path.Combine(directory, "proceed");
            Updater.SignalReady(ready);
            await UpdateService.WaitUntilReadyAsync(Process.GetCurrentProcess(), ready, TimeSpan.FromSeconds(1), CancellationToken.None);
            await Throws<TimeoutException>(() => Updater.WaitForProceedAsync(proceed, TimeSpan.FromMilliseconds(20), CancellationToken.None));
            UpdateService.SignalProceed(proceed);
            await Updater.WaitForProceedAsync(proceed, TimeSpan.FromSeconds(1), CancellationToken.None);
        });
        await Test("Updater canceled handshake never creates proceed signal", async () =>
        {
            var directory = Fixture("handshake-cancel"); var ready = Path.Combine(directory, "ready"); var proceed = Path.Combine(directory, "proceed");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Throws<OperationCanceledException>(() => UpdateService.WaitUntilReadyAsync(Process.GetCurrentProcess(), ready, TimeSpan.FromSeconds(1), cancellation.Token));
            await Throws<OperationCanceledException>(() => Updater.WaitForProceedAsync(proceed, TimeSpan.FromSeconds(1), cancellation.Token));
            True(!File.Exists(proceed), "Canceled handshake committed an update.");
        });
        await Test("Updater handshake rejects invalid signal and preserves existing files", async () =>
        {
            var signal = Path.Combine(Fixture("handshake-invalid"), "signal"); File.WriteAllText(signal, "existing");
            await Throws<InvalidDataException>(() => Updater.WaitForProceedAsync(signal, TimeSpan.FromSeconds(1), CancellationToken.None));
            await Throws<IOException>(() => Run(() => Updater.SignalReady(signal)));
            await Throws<IOException>(() => Run(() => UpdateService.SignalProceed(signal)));
            Equal("existing", File.ReadAllText(signal));
        });
        await Test("Updater readiness timeout leaves calling process running", async () =>
        {
            var ready = Path.Combine(Fixture("ready-timeout"), "ready");
            await Throws<TimeoutException>(() => UpdateService.WaitUntilReadyAsync(Process.GetCurrentProcess(), ready, TimeSpan.FromMilliseconds(20), CancellationToken.None));
            True(!Process.GetCurrentProcess().HasExited, "Calling application was terminated.");
        });
        await Test("Updater startup failure is detected before application shutdown", async () =>
        {
            var start = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add("--exit-immediately");
            using var exited = Process.Start(start)!; await exited.WaitForExitAsync();
            await Throws<InvalidOperationException>(() => UpdateService.WaitUntilReadyAsync(exited, Path.Combine(Root, "never-ready"), TimeSpan.FromSeconds(1), CancellationToken.None));
        });
        await Test("Updater wrong process is never killed", async () =>
        {
            await Throws<InvalidOperationException>(() => Updater.WaitForProcessExitAsync(Environment.ProcessId, Path.Combine(Root, "DHub.exe"), TimeSpan.FromMilliseconds(30)));
            True(!Process.GetCurrentProcess().HasExited, "Current test process was killed.");
        });
        await Test("Updater timeout never force-kills the application", async () =>
        {
            await Throws<TimeoutException>(() => Updater.WaitForProcessExitAsync(Environment.ProcessId, Environment.ProcessPath!, TimeSpan.FromMilliseconds(30)));
            True(!Process.GetCurrentProcess().HasExited, "Current test process was killed.");
        });
    }
}
