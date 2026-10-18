"""Static UI wiring checks only. Windows runtime acceptance is documented separately."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[2]

def read(path): return (ROOT/path).read_text()

class HiddenLauncherContract(unittest.TestCase):
    def test_startup_does_not_show_main_or_bar(self):
        main = read('src/ShortcutList/Views/MainWindow.xaml.cs')
        startup = main.split('public void ShowInitialWindow()', 1)[1].split('private bool EnsureEditingAllowed()', 1)[0]
        self.assertIn('EnsureHandle()', startup)
        self.assertNotIn('Show();', startup)
        self.assertIn('ShowInTaskbar = false', startup)
        self.assertNotIn('ShowInTaskbar = true', main)
        all_views = '\n'.join(p.read_text() for p in (ROOT/'src/ShortcutList/Views').glob('*.cs'))
        self.assertNotIn('_store.Settings.ShowLauncherBar', all_views)
        self.assertNotIn('_store.Settings.ShowHomeOnStartup', all_views)
        self.assertNotIn('ShowLauncherBar()', all_views)
    def test_tray_remains_visible_and_has_recovery(self):
        source = read('src/ShortcutList/Views/MainWindow.Launcher.cs')
        self.assertIn('Visible = true', source)
        self.assertIn('ShowHotkeySettings', source)
        self.assertIn('RequestExit', source)
    def test_popup_has_no_taskbar_and_has_keyboard_and_exit(self):
        source = read('src/ShortcutList/Views/QuickSearchWindow.xaml')
        root = ET.fromstring(source)
        self.assertEqual(root.get('ShowInTaskbar'), 'False')
        self.assertEqual(root.get('Deactivated'), 'Window_Deactivated')
        for handler in ['SettingsButton_Click', 'ManagementButton_Click', 'ExitButton_Click']:
            self.assertIn(handler, source)
    def test_retired_coordinates_are_still_persistable(self):
        source = read('src/ShortcutList/Models/AppSettings.cs')
        for name in ['ShowLauncherBar', 'LauncherBarLeft', 'LauncherBarTop', 'StartMinimizedToTray', 'ShowHomeOnStartup']:
            self.assertIn(name, source)
    def test_ipc_has_current_user_and_fixed_command_contract(self):
        source = read('src/ShortcutList/Services/ProfileActivation.cs')
        self.assertEqual(source.count('PipeOptions.CurrentUserOnly'), 2)
        self.assertIn('new byte[3]', source)
        self.assertIn('CancelAfter(ExchangeTimeoutMilliseconds)', source)
        self.assertIn('GetNamedPipeServerProcessId', source)
        self.assertNotIn('ASFW_ANY', source)
        self.assertIn('_server?.Dispose();', source)
    def test_no_autostart_or_shell_changes(self):
        for path in ['src/ShortcutList/Services/ProfileActivation.cs', 'src/ShortcutList/Views/MainWindow.Hotkeys.cs']:
            source = read(path)
            for forbidden in ['Registry.', 'Process.Start', 'SHAppBarMessage', 'SetWindowsHookEx']:
                self.assertNotIn(forbidden, source)
    def test_fixture_versions_do_not_follow_product_bump(self):
        self.assertIn("$PackageVersion = '1.1.4'", read('packaging/Build-Installer.ps1'))
        self.assertIn("PackageVersion = '1.1.4'", read('packaging/Build-Local.ps1'))
        self.assertIn("-PackageVersion '1.1.2'", read('packaging/Test-Installer.ps1'))

if __name__ == '__main__': unittest.main(verbosity=2)
