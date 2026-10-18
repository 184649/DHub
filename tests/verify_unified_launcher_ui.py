"""Static integration contracts; these do not substitute for Windows GUI acceptance."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET
ROOT = Path(__file__).resolve().parents[1]

def read(p): return (ROOT / p).read_text(encoding='utf-8')

class UnifiedLauncherUi(unittest.TestCase):
    def test_xaml_and_all_handlers_resolve(self):
        xml = read('src/ShortcutList/Views/QuickSearchWindow.xaml')
        code = read('src/ShortcutList/Views/QuickSearchWindow.xaml.cs')
        ET.fromstring(xml)
        events = ['Click', 'TextChanged', 'PreviewKeyDown', 'Deactivated', 'DragOver', 'Drop', 'MouseDoubleClick']
        for event in events:
            for handler in re.findall(r'\b' + event + r'="([^"]+)"', xml):
                self.assertIn(handler + '(', code)
    def test_one_entry_model_instead_of_shortcut_only(self):
        main = read('src/ShortcutList/Views/MainWindow.QuickLaunch.cs')
        for kind in ['QuickLaunchKind.Shortcut', 'QuickLaunchKind.LinkedBookmark', 'QuickLaunchKind.Workspace', 'QuickLaunchKind.SourceFolder']:
            self.assertIn(kind, main)
        self.assertIn('_bookmarkService.GetSnapshot', main)
        self.assertIn('const int linkedLimit = 20000', main)
        self.assertIn('QuickLaunchSearch.Query', read('src/ShortcutList/Views/QuickSearchWindow.xaml.cs'))
    def test_source_updates_preserve_recipe_and_do_not_reopen(self):
        ui = read('src/ShortcutList/Views/QuickSearchWindow.xaml.cs')
        method = ui.split('public void UpdateSource',1)[1].split('public void SetAddStatus',1)[0]
        self.assertNotIn('Show();', method)
        self.assertIn('preserveSelection: true', method)
        self.assertIn('x.Key == selected.Key && x.RecipeToken == selected.RecipeToken', ui)
        self.assertIn('generation == _quickGeneration', read('src/ShortcutList/Views/MainWindow.QuickLaunch.cs'))
    def test_capture_never_launches(self):
        main = read('src/ShortcutList/Views/MainWindow.QuickLaunch.cs')
        method = main.split('private async void QuickSearchWindow_AddRequested',1)[1]
        for forbidden in ['LaunchItem(', 'LaunchWorkspace(', 'Process.Start', 'ShortcutRunner.Run(']:
            self.assertNotIn(forbidden, method)
        self.assertIn('request.Targets.Count > 100', method)
        self.assertIn('_store.ContainsEquivalent(item)', method)
        self.assertIn('foreach (var item in added) _store.Items.Remove(item)', method)
    def test_batch_is_explicit_and_revalidated(self):
        main = read('src/ShortcutList/Views/MainWindow.QuickLaunch.cs')
        self.assertIn('ConfirmQuickWorkspace(workspace, items)', main)
        self.assertIn('QuickWorkspaceRecipe(workspace) != recipe', main)
        self.assertIn('_launchingWorkspaces.Add(workspace.Id)', main)
        self.assertIn('foreach (var item in items) list.Items.Add', main)
        confirmation = main.split('private bool ConfirmQuickWorkspace',1)[1].split('private async void LaunchQuickWorkspace',1)[0]
        self.assertIn('GetEffectiveOpenApplicationPath(item)', confirmation)
        self.assertIn('item.OpenApplicationArguments', confirmation)
        self.assertIn('item.Arguments', confirmation)
        self.assertNotIn('item.LaunchRecipeDisplay', confirmation)
        self.assertNotIn('form.AcceptButton =', main)
    def test_popup_has_no_dashboard_or_forced_activity_navigation(self):
        xml = read('src/ShortcutList/Views/QuickSearchWindow.xaml')
        for forbidden in ['Dashboard', 'ShowHome', '今日の作業', '作業を選ぶ', 'ログ']:
            self.assertNotIn(forbidden, xml)
        self.assertIn('ShowInTaskbar="False"', xml)
        self.assertIn('Window_Deactivated', xml)
        ui = read('src/ShortcutList/Views/QuickSearchWindow.xaml.cs')
        self.assertIn('if (AddPanel.Visibility != Visibility.Visible) Hide();', ui)
    def test_advanced_editor_is_collapsed(self):
        xml = ET.fromstring(read('src/ShortcutList/Views/ShortcutEditWindow.xaml'))
        expanded = [x for x in xml.iter() if x.get('Header') == '詳細設定']
        self.assertEqual(len(expanded), 1)
        self.assertEqual(expanded[0].get('IsExpanded'), 'False')

if __name__ == '__main__': unittest.main(verbosity=2)
