using System.Windows;
using ShortcutList.Services;
using ShortcutList.Views;

namespace ShortcutList;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private ProfileInstanceLock? _profileLock;

    protected override void OnStartup(StartupEventArgs e)
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
            if (e.Args.Length != 0 && (e.Args.Length != 2 || e.Args[0] != "--data-dir" || string.IsNullOrWhiteSpace(e.Args[1])))
            {
                System.Windows.MessageBox.Show(AppProfile.UsageMessage, "DHub");
                Shutdown(2);
                return;
            }
            var isPreviewBuild = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .Any(a => a.InformationalVersion.Contains("-preview", StringComparison.OrdinalIgnoreCase));
            var dataDirectory = AppProfile.ResolveDataDirectory(e.Args, isPreviewBuild);
            // Acquire before MainWindow constructs/loads ShortcutStore or starts
            // background work. --data-dir and preview isolation remain unchanged.
            _profileLock = ProfileInstanceLock.TryAcquire(dataDirectory);
            if (_profileLock is null)
            {
                System.Windows.MessageBox.Show("このデータフォルダーを使うDHubは既に起動しています。タスクトレイから開いてください。", "DHub");
                Shutdown();
                return;
            }
            _mainWindow = new MainWindow(dataDirectory);
            MainWindow = _mainWindow;
            _mainWindow.ShowInitialWindow();
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
        try { _profileLock?.Dispose(); }
        finally { base.OnExit(e); }
    }
}
