using System.Windows;
using ShortcutList.Services;
using ShortcutList.Views;

namespace ShortcutList;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private ProfileInstanceLock? _profileLock;
    private ProfileActivation? _activation;
    private volatile bool _stopping;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception is System.IO.IOException or System.IO.InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException ||
                (args.Exception is InvalidOperationException && args.Exception.StackTrace?.Contains("ShortcutStore") == true))
            {
                System.Windows.MessageBox.Show("操作を完了できませんでした。変更はまだ保存されていない可能性があります。\n" + args.Exception.Message,
                    "DHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            }
        };
        try
        {
            base.OnStartup(e);
            var isPreviewBuild = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .Any(a => a.InformationalVersion.Contains("-preview", StringComparison.OrdinalIgnoreCase));
            var options = AppProfile.Parse(e.Args, isPreviewBuild);
            var dataDirectory = options.DataDirectory;
            var pipeName = ProfileActivation.PipeName(dataDirectory,
                System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
                    ?? throw new InvalidOperationException("Windowsユーザーを確認できません。"),
                System.Diagnostics.Process.GetCurrentProcess().SessionId);
            // Acquire before MainWindow constructs/loads ShortcutStore or starts
            // background work. --data-dir and preview isolation remain unchanged.
            _profileLock = ProfileInstanceLock.TryAcquire(dataDirectory);
            if (_profileLock is null)
            {
                if (!await ProfileActivation.TrySendAsync(pipeName, options.Activation ?? ActivationCommand.Show))
                    System.Windows.MessageBox.Show("起動中のDHubに表示要求を届けられませんでした。タスクトレイから開いてください。起動直後なら少し待って再実行できます。別のWindowsセッションや保存先への別名は自動で切り替えません。", "DHub");
                Shutdown();
                return;
            }
            _mainWindow = new MainWindow(dataDirectory);
            MainWindow = _mainWindow;
            _activation = new ProfileActivation(pipeName, command =>
            {
                if (_stopping || Dispatcher.HasShutdownStarted) return false;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_stopping) _mainWindow?.ActivateFromRequest(command);
                }));
                return true;
            }, error =>
            {
                if (_stopping || Dispatcher.HasShutdownStarted) return;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_stopping) System.Windows.MessageBox.Show("再実行からの表示要求を受け取れなくなりました。トレイまたはホットキーは使用できます。再起動すると再試行します。\n" + error.Message, "DHub");
                }));
            });
            _mainWindow.ShowInitialWindow();
            if (options.Activation is { } command) _mainWindow.ActivateFromRequest(command);
        }
        catch (Exception ex)
        {
            try
            {
                System.Windows.MessageBox.Show($"DHubを起動できませんでした。保存先・権限・同じデータを使う別プロセスを確認してください。\n{ex.GetBaseException().Message}",
                    "DHub 起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { Shutdown(1); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopping = true;
        _activation?.Dispose();
        try { _profileLock?.Dispose(); }
        finally { base.OnExit(e); }
    }
}
