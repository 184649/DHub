using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using ShortcutList.Models;
using ShortcutList.Services;

var count = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL " + message);
    count++; Console.WriteLine("PASS " + message);
}
var fresh = AppProfile.Parse(Array.Empty<string>(), true);
Check(fresh.Activation is null && fresh.DataDirectory.EndsWith("DHub-Preview"), "ordinary preview startup is hidden and profile-isolated");
Check(AppProfile.Parse(new[] { "--show" }, false).Activation == ActivationCommand.Show, "explicit show activates list");
Check(AppProfile.Parse(new[] { "--settings" }, true).Activation == ActivationCommand.Settings, "explicit settings activates recovery");
var profile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DHub hidden profile"));
Check(AppProfile.Parse(new[] { "--settings", "--data-dir", profile }, true) ==
    AppProfile.Parse(new[] { "--data-dir", profile, "--settings" }, true), "profile option order is unambiguous");
foreach (var options in new[] { new[] { "--show", "--settings" }, new[] { "--show", "--show" }, new[] { "--data-dir", "--show" },
    new[] { "--data-dir", " " }, new[] { "--data-dir", profile, "--data-dir", profile }, new[] { "--launch", "evil" }, new[] { "--exit" } })
{
    var rejected = false;
    try { _ = AppProfile.Parse(options, true); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "reject ambiguous or non-whitelisted startup: " + string.Join(' ', options));
}
var old = JsonSerializer.Deserialize<AppSettings>("{\"ShowLauncherBar\":true,\"LauncherBarLeft\":-1700,\"LauncherBarTop\":800,\"StartMinimizedToTray\":false,\"ShowHomeOnStartup\":true}")!;
Check(old.QuickSearchHotkey == HotkeyBinding.DefaultName && old.ShowLauncherBar && old.LauncherBarLeft == -1700 && old.ShowHomeOnStartup,
    "new hotkey default preserves legacy presentation fields without rewriting them");
old.QuickSearchHotkey = "Ctrl+Alt+Space";
var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old))!;
Check(copy.QuickSearchHotkey == "Ctrl+Alt+Space" && copy.LauncherBarTop == 800 && copy.ShowLauncherBar, "selected key and legacy positions roundtrip");
Check(HotkeyBinding.Resolve("untrusted command").Name == HotkeyBinding.DefaultName && HotkeyBinding.Resolve(null).Name == HotkeyBinding.DefaultName,
    "unknown hotkey values safely resolve to fixed default");
var registrations = new List<(int Id, uint Modifiers, uint Key)>();
var releases = new List<int>();
var available = new HashSet<int> { HotkeyRegistration.ManagementId };
using (var keys = new HotkeyRegistration((id, modifiers, key) => { registrations.Add((id, modifiers, key)); return available.Contains(id); }, releases.Add))
{
    var failed = keys.Apply(true, HotkeyBinding.DefaultName);
    Check(failed.HasFailure && !failed.SearchRegistered && failed.ManagementRegistered, "search collision is explicit and independent management registration survives");
    Check(registrations.All(x => (x.Modifiers & 0x4000) != 0), "key auto-repeat cannot repeatedly reopen launcher");
    available.Add(HotkeyRegistration.SearchId);
    var repaired = keys.Apply(true, "Ctrl+Alt+Space");
    Check(!repaired.HasFailure && repaired.SearchRegistered && releases.SequenceEqual(new[] { HotkeyRegistration.ManagementId }), "retry releases only keys owned by this process");
    Check(registrations[^2].Key == 0x20 && (registrations[^2].Modifiers & 0x0001) != 0, "alternate hotkey reaches registration adapter");
    var disabled = keys.Apply(false, "Ctrl+Alt+Space");
    Check(!disabled.Enabled && !disabled.HasFailure && releases.Count == 3, "disable releases both owned keys");
    keys.Dispose(); keys.Dispose();
    Check(releases.Count == 3, "repeated dispose does not release another application's keys");
}
var name = ProfileActivation.PipeName(profile, "test-user", 1);
Check(name == ProfileActivation.PipeName(Path.Combine(profile, "child", ".."), "test-user", 1), "normalized profile aliases share activation identity");
Check(name != ProfileActivation.PipeName(profile + "-other", "test-user", 1) &&
    name != ProfileActivation.PipeName(profile, "another-user", 1) && name != ProfileActivation.PipeName(profile, "test-user", 2),
    "profile, user and session boundaries isolate activation endpoints");
Check(!name.Contains(profile) && name.Length < 120, "pipe name contains no path disclosure and stays bounded");
Check(ProfileActivation.TryDecode(new byte[] { 0xD4, 1, 1 }, out var show) && show == ActivationCommand.Show &&
    ProfileActivation.TryDecode(new byte[] { 0xD4, 1, 2 }, out var settings) && settings == ActivationCommand.Settings, "protocol accepts only two fixed activation commands");
foreach (var frame in new[] { Array.Empty<byte>(), new byte[] { 0xD4, 1 }, new byte[] { 0xD4, 1, 3 }, new byte[] { 0xD4, 2, 1 }, new byte[] { 0xD4, 1, 1, 0 }, new byte[] { 0, 0, 0 } })
    Check(!ProfileActivation.TryDecode(frame, out _), "protocol rejects invalid frame " + Convert.ToHexString(frame));
if (args.Contains("--logic-only", StringComparer.Ordinal))
{
    Console.WriteLine($"All {count} pure-logic checks passed. SKIP live named-pipe tests: explicit logic-only mode; run on Windows CI.");
    return;
}
var endpoint = name + "." + Guid.NewGuid().ToString("N");
var received = new System.Collections.Concurrent.ConcurrentQueue<ActivationCommand>();
var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
var server = new ProfileActivation(endpoint, command => { received.Enqueue(command); return true; }, errors.Enqueue);
try
{
    Check(await ProfileActivation.TrySendAsync(endpoint, ActivationCommand.Show), "same-user list request is acknowledged");
    Check(await ProfileActivation.TrySendAsync(endpoint, ActivationCommand.Settings), "same-user settings request is acknowledged");
    Check(received.SequenceEqual(new[] { ActivationCommand.Show, ActivationCommand.Settings }), "activation receiver receives exact requested actions");
    Check(!await ProfileActivation.TrySendAsync(endpoint + ".other", ActivationCommand.Show, 150), "different profile endpoint cannot activate current instance");
    using (var malformed = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await malformed.ConnectAsync(2000);
        await malformed.WriteAsync(new byte[] { 0xD4, 1, 0xFF });
        var reply = new byte[1]; await malformed.ReadExactlyAsync(reply);
        Check(reply[0] == 0 && received.Count == 2, "unknown IPC request is rejected without callback");
    }
    using (var stalled = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
    {
        await stalled.ConnectAsync(2000);
        await stalled.WriteAsync(new byte[] { 0xD4 });
        await Task.Delay(1750);
        Check(await ProfileActivation.TrySendAsync(endpoint, ActivationCommand.Show), "bounded partial client timeout permits the next legitimate activation");
    }
    Check(errors.IsEmpty, "malformed and disconnected clients do not kill activation listener");
}
finally { server.Dispose(); await server.Completion.WaitAsync(TimeSpan.FromSeconds(3)); }
var afterStop = received.Count;
Check(!await ProfileActivation.TrySendAsync(endpoint, ActivationCommand.Show, 150) && received.Count == afterStop, "shutdown cancels listener without further callbacks");
var waiting = ProfileActivation.TrySendAsync(endpoint, ActivationCommand.Show);
await Task.Delay(200);
using (var restarted = new ProfileActivation(endpoint, _ => true, errors.Enqueue))
    Check(await waiting, "second process waits briefly for owner startup and endpoint can be reused after exit");
Check(errors.IsEmpty, "endpoint shutdown and restart have no fatal listener errors");
Console.WriteLine($"All {count} hidden launcher regression checks passed.");
