using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ShortcutList.Services;

// Only the lock owner may create this endpoint. It accepts UI activation, never
// paths, shell commands, data writes, shutdown requests, or arbitrary payloads.
internal sealed class ProfileActivation : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<ActivationCommand, bool> _activate;
    private readonly Action<Exception> _onFailure;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listener;
    private int _disposed;
    private readonly object _gate = new();
    private NamedPipeServerStream? _server;
    internal Task Completion => _listener;
    private const int ExchangeTimeoutMilliseconds = 1500;

    internal static string PipeName(string directory, string userIdentity, int sessionId)
    {
        var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        // Hash the structured identity instead of concatenating ambiguous path separators.
        var identity = System.Text.Json.JsonSerializer.Serialize(new[] { userIdentity, sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture), profile });
        return "DHub.Activation.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    internal ProfileActivation(string pipeName, Func<ActivationCommand, bool> activate, Action<Exception> onFailure)
    {
        _pipeName = pipeName;
        _activate = activate;
        _onFailure = onFailure;
        // Create synchronously so endpoint/ACL errors are visible during startup.
        var first = CreateServer();
        _listener = ListenAsync(first);
    }

    private NamedPipeServerStream CreateServer()
    {
        lock (_gate)
        {
            _stop.Token.ThrowIfCancellationRequested();
            return _server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 16, 16);
        }
    }

    internal static bool TryDecode(ReadOnlySpan<byte> frame, out ActivationCommand command)
    {
        command = default;
        if (frame.Length != 3 || frame[0] != 0xD4 || frame[1] != 1 || frame[2] is not (1 or 2)) return false;
        command = (ActivationCommand)frame[2];
        return true;
    }

    private async Task ListenAsync(NamedPipeServerStream server)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using (server)
                {
                    await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(ExchangeTimeoutMilliseconds);
                    try
                    {
                        var frame = new byte[3];
                        await server.ReadExactlyAsync(frame, deadline.Token).ConfigureAwait(false);
                        var accepted = TryDecode(frame, out var command) && !_stop.IsCancellationRequested && _activate(command);
                        await server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, deadline.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException)
                    {
                        // Broken, partial, or stalled peers cannot stop the next activation.
                    }
                }
                if (!_stop.IsCancellationRequested) server = CreateServer();
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or IOException) { }
        catch (Exception ex) { if (!_stop.IsCancellationRequested) _onFailure(ex); }
        finally { server.Dispose(); }
    }

    internal static async Task<bool> TrySendAsync(string pipeName, ActivationCommand command, int timeoutMilliseconds = 4000)
    {
        if (command is not (ActivationCommand.Show or ActivationCommand.Settings)) return false;
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(250, deadline.Token).ConfigureAwait(false);
                // Permit only the OS-verified receiving process to take foreground focus.
                if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
                    _ = AllowSetForegroundWindow(processId);
                await pipe.WriteAsync(new byte[] { 0xD4, 1, (byte)command }, deadline.Token).ConfigureAwait(false);
                var reply = new byte[1];
                await pipe.ReadExactlyAsync(reply, deadline.Token).ConfigureAwait(false);
                return reply[0] == 1;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return false; }
            catch (OperationCanceledException) { return false; }
            try { await Task.Delay(75, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            _stop.Cancel();
            _server?.Dispose();
        }
        // Never synchronously join on the UI thread. No callback is queued after cancellation.
        _ = _listener.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
