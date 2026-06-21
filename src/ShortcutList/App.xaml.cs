using System.Windows;
using ShortcutList.Views;

namespace ShortcutList;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初回起動時はウィンドウを表示せず、タスクトレイだけで常駐します。
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
    }
}
