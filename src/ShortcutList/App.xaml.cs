using System.Windows;
using ShortcutList.Views;

namespace ShortcutList;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

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
        base.OnStartup(e);

        string? dataDirectory = null;
        if (e.Args.Length > 0)
        {
            if (e.Args.Length != 2 || e.Args[0] != "--data-dir" || string.IsNullOrWhiteSpace(e.Args[1]))
            {
                System.Windows.MessageBox.Show("使用方法: DHub.exe [--data-dir 保存先フォルダー]", "DHub");
                Shutdown(2);
                return;
            }
            dataDirectory = System.IO.Path.GetFullPath(e.Args[1]);
        }
        else if (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Any(a => a.InformationalVersion.Contains("-preview", StringComparison.OrdinalIgnoreCase)))
        {
            dataDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHub-Preview");
        }
        _mainWindow = new MainWindow(dataDirectory);
        MainWindow = _mainWindow;
        _mainWindow.ShowInitialWindow();
    }
}

