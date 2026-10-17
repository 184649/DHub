using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShortcutList.Models;
using ShortcutList.Services;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ShortcutList.Views;

public partial class MainWindow
{
    private readonly BookmarkSourceService _bookmarkService = new();
    private Window? _launcherBar;
    private readonly PopupOwner _popupOwner = new();
    private IntPtr _popupPreviousWindow;
    private bool _popupActive;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private sealed class PopupOwner : Forms.NativeWindow, IDisposable
    {
        public PopupOwner() => CreateHandle(new Forms.CreateParams { Caption = "DHub popup owner" });
        public void Dispose() => DestroyHandle();
    }
    private void ShowLauncherPopup()
    {
        RefreshTrayMenu();
        var menu = _notifyIcon?.ContextMenuStrip;
        if (menu is null) return;
        _popupPreviousWindow = GetForegroundWindow();
        _popupActive = true;
        SetForegroundWindow(_popupOwner.Handle);
        menu.Show(Forms.Cursor.Position);
    }

    private bool _historySavePending;
    private bool _trayRefreshPending;

    private void InitializeTrayIcon()
    {
        _trayIcon = LoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon { Icon = _trayIcon, Text = "DHub", Visible = true };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatcher.Invoke(ShowLauncherPopup);
        };
        RefreshTrayMenu();
        _ = RefreshBookmarkSourcesAsync();
        if (_store.Settings.ShowLauncherBar) ShowLauncherBar();
    }

    private static string MenuText(string value) => value.Replace("&", "&&");
    private static string Recipe(ShortcutItem item) =>
        System.Text.Json.JsonSerializer.Serialize(new { item.Id, item.TargetPath, item.Arguments,
            item.OpenApplicationPath, item.OpenApplicationArguments, item.ShortcutType });

    private Forms.ToolStripMenuItem CreateLaunchMenuItem(ShortcutItem item)
    {
        var recipe = Recipe(item);
        var row = new Forms.ToolStripMenuItem(MenuText($"{item.IconText} {LauncherLabel(item, 32)}"))
        { ToolTipText = item.LaunchRecipeTooltip };
        row.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            if (Recipe(item) != recipe) { ShowInfo("この登録の起動方法は変更されました。メニューを開き直してください。"); return; }
            LaunchItem(item);
        });
        return row;
    }

    private void RefreshTrayMenu()
    {
        RefreshLauncherBar();
        if (_notifyIcon is null) return;
        if (_notifyIcon.ContextMenuStrip?.Visible == true) { _trayRefreshPending = true; return; }
        var menu = new Forms.ContextMenuStrip { ShowItemToolTips = true };
        var favorites = OrderedLauncherPins();
        if (favorites.Count == 0)
            menu.Items.Add(new Forms.ToolStripMenuItem("お気に入りを登録するとここから開けます") { Enabled = false });
        foreach (var item in favorites.Take(20)) menu.Items.Add(CreateLaunchMenuItem(item));
        if (favorites.Count > 20)
            AddTrayAction(menu.Items, $"お気に入りを全件表示 / 検索（{favorites.Count} 件）", () => ShowFavoriteSearch(favorites));
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var source in _store.BookmarkSources)
        {
            var snapshot = _bookmarkService.GetSnapshot(source.Id);
            var children = snapshot.Entries.ToLookup(x => Path.GetDirectoryName(x.RelativePath) ?? "", StringComparer.OrdinalIgnoreCase);
            var sourceMenu = new Forms.ToolStripMenuItem(MenuText("📁 " + source.Name));
            AddTrayAction(sourceMenu.DropDownItems, "この登録元フォルダーを開く", () => OpenBookmarkRoot(source));
            AddTrayAction(sourceMenu.DropDownItems, "全件表示 / 検索", () => ShowBookmarkBrowser(source));
            sourceMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(MenuText(snapshot.Status)) { Enabled = false });
            foreach (var entry in snapshot.Entries.Where(x => !x.RelativePath.Contains(Path.DirectorySeparatorChar)).Take(30))
                AddBookmarkMenuEntry(sourceMenu.DropDownItems, source, snapshot, entry, children);
            if (snapshot.Entries.Count > 30)
                AddTrayAction(sourceMenu.DropDownItems, "続きを全件表示 / 検索", () => ShowBookmarkBrowser(source));
            menu.Items.Add(sourceMenu);
        }
        AddTrayAction(menu.Items, "全登録を検索", ShowQuickSearch);
        var recent = new Forms.ToolStripMenuItem("最近使った項目");
        foreach (var item in _store.Items.Where(x => x.LastLaunchedAt.HasValue).OrderByDescending(x => x.LastLaunchedAt).Take(15))
            recent.DropDownItems.Add(CreateLaunchMenuItem(item));
        recent.Enabled = recent.DropDownItems.Count > 0;
        menu.Items.Add(recent);
        var more = new Forms.ToolStripMenuItem("ワークスペース / コマンド / グループ");
        var workspaces = new Forms.ToolStripMenuItem("ワークスペース");
        foreach (var workspace in _store.Workspaces.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Take(30))
        {
            var recipe = string.Join("\\n", workspace.ShortcutIds) + ":" + workspace.DelaySeconds;
            AddTrayAction(workspaces.DropDownItems, MenuText(workspace.Name), () =>
            {
                if (!_store.Workspaces.Contains(workspace) || recipe != string.Join("\\n", workspace.ShortcutIds) + ":" + workspace.DelaySeconds)
                    { ShowInfo("作業セットが変更されました。メニューを開き直してください。"); return; }
                LaunchWorkspace(workspace);
            });
        }
        AddTrayAction(workspaces.DropDownItems, $"全件 / 管理（{_store.Workspaces.Count} 件）", ShowWorkspaceManager);
        more.DropDownItems.Add(workspaces);
        var commands = new Forms.ToolStripMenuItem("コマンド");
        foreach (var command in _store.Commands.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Name).Take(30))
        {
            var recipe = System.Text.Json.JsonSerializer.Serialize(command);
            AddTrayAction(commands.DropDownItems, MenuText(command.Name), () =>
            {
                if (System.Text.Json.JsonSerializer.Serialize(command) != recipe)
                    { ShowInfo("コマンドが変更されました。メニューを開き直してください。"); return; }
                RunCommand(command);
            });
        }
        AddTrayAction(commands.DropDownItems, $"全件 / 管理（{_store.Commands.Count} 件）", ShowCommandManager);
        more.DropDownItems.Add(commands);
        var groups = new Forms.ToolStripMenuItem("グループ");
        foreach (var group in _store.Items.GroupBy(x => x.GroupDisplay).OrderBy(x => x.Key).Take(30))
        {
            var groupMenu = new Forms.ToolStripMenuItem(MenuText(group.Key));
            var entries = group.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();
            foreach (var item in entries.Take(30)) groupMenu.DropDownItems.Add(CreateLaunchMenuItem(item));
            AddTrayAction(groupMenu.DropDownItems, $"全件表示 / 検索（{entries.Count} 件）", () => ShowFavoriteSearch(entries));
            groups.DropDownItems.Add(groupMenu);
        }
        AddTrayAction(groups.DropDownItems, "すべてのグループを検索", ShowQuickSearch);
        more.DropDownItems.Add(groups);
        menu.Items.Add(more);
        var manage = new Forms.ToolStripMenuItem("管理");
        AddTrayAction(manage.DropDownItems, "管理画面を開く", ShowMainWindowFromTray);
        AddTrayAction(manage.DropDownItems, "ホーム", ShowHomeDashboard);
        AddTrayAction(manage.DropDownItems, "追加", OpenAddDialog);
        AddTrayAction(manage.DropDownItems, "登録元フォルダーの管理", ShowBookmarkSourceManager);
        AddTrayAction(manage.DropDownItems, "登録元を再読込", () => _ = RefreshBookmarkSourcesAsync());
        AddTrayAction(manage.DropDownItems, "統合検索 / コマンドパレット", ShowCommandPalette);
        AddTrayAction(manage.DropDownItems, "ワークスペース管理", ShowWorkspaceManager);
        AddTrayAction(manage.DropDownItems, "コマンド管理", ShowCommandManager);
        AddTrayAction(manage.DropDownItems, "設定", ShowSettingsScreen);
        AddTrayAction(manage.DropDownItems, "操作ログ", ShowOperationLogWindow);
        AddTrayAction(manage.DropDownItems, "ショートカットをエクスポート", ExportShortcuts);
        AddTrayAction(manage.DropDownItems, "ショートカットをインポート", ImportShortcuts);
        AddTrayAction(manage.DropDownItems, "設定をエクスポート", ExportSettings);
        AddTrayAction(manage.DropDownItems, "設定をインポート", ImportSettings);
        AddTrayAction(manage.DropDownItems, "データ保存場所を開く", OpenStorageFolder);
        AddTrayAction(manage.DropDownItems, "保存データの確認 / 復旧", ShowStorageRecovery);
        AddTrayAction(manage.DropDownItems, "未保存の履歴を再保存", () =>
        {
            if (_store.IsReadOnly) { ShowStorageRecovery(); return; }
            try { _store.Save(); _historySavePending = false; ShowInfo("履歴を保存しました。起動要求は再送していません。"); }
            catch (Exception ex) { ShowInfo("保存できませんでした。\n" + ex.Message); }
        }).Enabled = _historySavePending;
        AddTrayAction(manage.DropDownItems, "固定バーを表示 / 非表示", () => SetLauncherBarVisible(_launcherBar?.IsVisible != true));
        var topmost = AddTrayAction(manage.DropDownItems, "固定バーを最前面に表示（今回のみ）", () =>
        {
            ShowLauncherBar();
            _launcherBar!.Topmost = !_launcherBar.Topmost;
        });
        topmost.Checked = _launcherBar?.Topmost == true;
        AddTrayAction(manage.DropDownItems, "更新を確認", () => _ = CheckUpdateAsync(false));
        menu.Items.Add(manage);
        AddTrayAction(menu.Items, "終了", () => { _isExitRequested = true; System.Windows.Application.Current.Shutdown(); });
        menu.Closed += (_, args) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_popupActive)
            {
                _popupActive = false;
                PostMessage(_popupOwner.Handle, 0, IntPtr.Zero, IntPtr.Zero);
                if (args.CloseReason == Forms.ToolStripDropDownCloseReason.Keyboard && _popupPreviousWindow != IntPtr.Zero)
                    SetForegroundWindow(_popupPreviousWindow);
            }
            if (_trayRefreshPending) { _trayRefreshPending = false; RefreshTrayMenu(); }
            _ = RefreshBookmarkSourcesAsync();
        }));
        var old = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private Forms.ToolStripMenuItem AddTrayAction(Forms.ToolStripItemCollection items, string label, Action action)
    {
        var row = new Forms.ToolStripMenuItem(label);
        row.Click += (_, _) => Dispatcher.Invoke(action);
        items.Add(row);
        return row;
    }

    private void ShowFavoriteSearch(IReadOnlyList<ShortcutItem> favorites)
    {
        EnsureQuickSearchWindow();
        _quickSearchWindow!.LaunchRequested -= QuickSearchWindow_LaunchRequested;
        _quickSearchWindow.LaunchRequested += QuickSearchWindow_LaunchRequested;
        _quickSearchWindow.ShowSearch(favorites);
    }

    private async Task RefreshBookmarkSourcesAsync()
    {
        var sources = _store.BookmarkSources.ToArray();
        try { await Task.WhenAll(sources.Select(s => _bookmarkService.RefreshAsync(s))); }
        catch (ObjectDisposedException) { return; }
        if (!_isExitRequested) RefreshTrayMenu();
    }

    private void AddBookmarkMenuEntry(Forms.ToolStripItemCollection rows, BookmarkSource source,
        BookmarkSnapshot snapshot, BookmarkEntry entry, ILookup<string, BookmarkEntry> children)
    {
        var row = new Forms.ToolStripMenuItem(MenuText((entry.IsFolder ? "📁 " : "📄 ") + entry.Name)) { ToolTipText = entry.FullPath };
        if (!entry.IsFolder) row.Click += (_, _) => Dispatcher.Invoke(() => LaunchBookmark(snapshot, entry));
        else
        {
            row.DropDownItems.Add(new Forms.ToolStripMenuItem("読込中") { Enabled = false });
            row.DropDownOpening += (_, _) =>
            {
                row.DropDownItems.Clear();
                AddTrayAction(row.DropDownItems, "このフォルダーを開く", () => LaunchBookmark(snapshot, entry));
                foreach (var child in children[entry.RelativePath].Take(25))
                    AddBookmarkMenuEntry(row.DropDownItems, source, snapshot, child, children);
                AddTrayAction(row.DropDownItems, "全件表示 / 検索", () => ShowBookmarkBrowser(source, entry.RelativePath));
            };
        }
        rows.Add(row);
    }

    private void LaunchBookmark(BookmarkSnapshot snapshot, BookmarkEntry entry)
    {
        var source = _store.BookmarkSources.FirstOrDefault(s => s.Id == entry.SourceId);
        if (source is null ||
            !string.Equals(Path.GetFullPath(Path.Combine(source.FolderPath, entry.RelativePath)), entry.FullPath, StringComparison.OrdinalIgnoreCase) ||
            source.HiddenPaths.Any(h => string.Equals(h, entry.RelativePath, StringComparison.OrdinalIgnoreCase) || entry.RelativePath.StartsWith(h + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
            !_bookmarkService.IsCurrent(snapshot, entry))
        { ShowInfo("登録元の変更を検知しました。再読込後に選び直してください。"); return; }
        try
        {
            // Preserve .lnk/.url files and their shell recipe instead of resolving their targets.
            ShortcutRunner.Run(new ShortcutItem { TargetPath = entry.FullPath, Name = entry.Name,
                ShortcutType = entry.IsFolder ? ShortcutType.Folder : ShortcutType.File });
        }
        catch (Exception ex) { ShowInfo("起動要求に失敗しました。\n" + ex.Message); }
    }

    private void OpenBookmarkRoot(BookmarkSource source)
    {
        if (!_store.BookmarkSources.Contains(source)) { ShowInfo("この登録元は変更または解除されました。"); return; }
        try { ShortcutRunner.Run(new ShortcutItem { TargetPath = source.FolderPath, ShortcutType = ShortcutType.Folder }); }
        catch (Exception ex) { ShowInfo("登録元を開けませんでした。\n" + ex.Message); }
    }

    private void ShowBookmarkSourceManager()
    {
        using var form = new Forms.Form { Text = "登録元フォルダー（元ファイルは変更しません）", Width = 760, Height = 420,
            StartPosition = Forms.FormStartPosition.CenterScreen };
        var list = new Forms.ListBox { Dock = Forms.DockStyle.Fill };
        var actions = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 48 };
        void Refresh() { list.Items.Clear(); foreach (var source in _store.BookmarkSources) list.Items.Add(source); }
        void Button(string text, Action action) { var b = new Forms.Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); actions.Controls.Add(b); }
        Button("フォルダーを連携", () =>
        {
            if (!EnsureEditingAllowed()) return;
            using var picker = new Forms.FolderBrowserDialog { Description = "中身をメニュー表示するフォルダーを選択" };
            if (picker.ShowDialog() != Forms.DialogResult.OK) return;
            if (_store.BookmarkSources.Any(s => string.Equals(s.FolderPath, picker.SelectedPath, StringComparison.OrdinalIgnoreCase))) return;
            var source = new BookmarkSource { FolderPath = picker.SelectedPath, Name = Path.GetFileName(picker.SelectedPath.TrimEnd('\\')) };
            if (string.IsNullOrWhiteSpace(source.Name)) source.Name = picker.SelectedPath;
            _store.BookmarkSources.Add(source); _store.Save(); Refresh(); _ = RefreshBookmarkSourcesAsync();
        });
        Button("連携を解除", () =>
        {
            if (!EnsureEditingAllowed()) return;
            if (list.SelectedItem is not BookmarkSource source || AskYesNo($"「{source.Name}」の連携を解除しますか？\n元フォルダーやファイルは削除しません。") != true) return;
            _store.BookmarkSources.Remove(source); _store.Save(); _bookmarkService.Remove(source.Id); Refresh(); RefreshTrayMenu();
        });
        Button("非表示をすべて戻す", () =>
        {
            if (list.SelectedItem is not BookmarkSource source) return;
            if (!EnsureEditingAllowed()) return;
            source.HiddenPaths.Clear(); _store.Save(); _ = RefreshBookmarkSourcesAsync();
        });
        Button("全件 / 検索", () => { if (list.SelectedItem is BookmarkSource source) ShowBookmarkBrowser(source); });
        form.Controls.Add(list); form.Controls.Add(actions); Refresh(); form.ShowDialog();
    }

    private void ShowBookmarkBrowser(BookmarkSource source, string prefix = "")
    {
        using var form = new Forms.Form { Text = source.Name + " / 全件・検索", Width = 900, Height = 620,
            StartPosition = Forms.FormStartPosition.CenterScreen };
        var search = new Forms.TextBox { Dock = Forms.DockStyle.Top };
        var status = new Forms.Label { Dock = Forms.DockStyle.Bottom, Height = 44 };
        var list = new Forms.ListBox { Dock = Forms.DockStyle.Fill, HorizontalScrollbar = true, DisplayMember = nameof(BookmarkEntry.RelativePath) };
        var actions = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 42 };
        var snapshot = _bookmarkService.GetSnapshot(source.Id);
        void Filter()
        {
            list.Items.Clear();
            foreach (var entry in snapshot.Entries.Where(x => (prefix.Length == 0 || x.RelativePath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) &&
                x.RelativePath.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
                list.Items.Add(entry);
            status.Text = $"{snapshot.Status} / 表示 {list.Items.Count} 件" + (list.Items.Count == 0 && search.Text.Length > 0 ? "（検索結果なし）" : "");
        }
        void Button(string text, Action action) { var b = new Forms.Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); actions.Controls.Add(b); }
        Button("開く", () => { if (list.SelectedItem is BookmarkEntry entry) LaunchBookmark(snapshot, entry); });
        Button("元フォルダーを開く", () => OpenBookmarkRoot(source));
        Button("非表示", () =>
        {
            if (list.SelectedItem is not BookmarkEntry entry) return;
            if (!EnsureEditingAllowed()) return;
            source.HiddenPaths.Add(entry.RelativePath); _store.Save();
            status.Text = "非表示を保存しました。再読込してください。元ファイルは変更していません。";
            _bookmarkService.Remove(source.Id); _ = RefreshBookmarkSourcesAsync();
        });
        var refresh = new Forms.Button { Text = "再読込", AutoSize = true };
        refresh.Click += async (_, _) =>
        {
            refresh.Enabled = false; status.Text = "読込中";
            try { var next = await _bookmarkService.RefreshAsync(source); if (form.IsDisposed) return; snapshot = next; Filter(); }
            finally { if (!form.IsDisposed) refresh.Enabled = true; }
        };
        actions.Controls.Add(refresh);
        search.TextChanged += (_, _) => Filter();
        list.DoubleClick += (_, _) => { if (list.SelectedItem is BookmarkEntry entry) LaunchBookmark(snapshot, entry); };
        list.KeyDown += (_, e) => { if (e.KeyCode == Forms.Keys.Enter && list.SelectedItem is BookmarkEntry entry) { e.Handled = true; LaunchBookmark(snapshot, entry); } };
        form.Controls.Add(list); form.Controls.Add(search); form.Controls.Add(actions); form.Controls.Add(status);
        form.Shown += async (_, _) =>
        {
            refresh.Enabled = false; status.Text = "読込中";
            try { var next = await _bookmarkService.RefreshAsync(source); if (form.IsDisposed) return; snapshot = next; Filter(); }
            finally { if (!form.IsDisposed) refresh.Enabled = true; }
        };
        Filter(); form.ShowDialog();
    }

    private void ShowStorageRecovery()
    {
        using var form = new Forms.Form { Text = "保存データの確認 / 復旧", Width = 760, Height = 480,
            StartPosition = Forms.FormStartPosition.CenterScreen };
        var preview = new Forms.TextBox { Dock = Forms.DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = Forms.ScrollBars.Both, Text = _store.StorageConflictSummary + "\n" + _store.LastLoadMessage };

        var buttons = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 48 };
        void Add(string label, bool enabled, bool separated)
        {
            var button = new Forms.Button { Text = label, AutoSize = true, Enabled = enabled };
            button.Click += (_, _) =>
            {
                if (AskYesNo(label + "を採用しますか？\n現在の原ファイル一式を専用保全フォルダーへコピーしてから保存します。未保存の編集・履歴は選択した状態へ戻ります。") != true) return;
                try { _store.ResolveSeparatedDataConflict(separated); ApplyFilter(); RefreshTrayMenu(); form.Close(); ShowInfo("選択したデータを保存しました。原ファイルの保全先: " + Path.Combine(_store.StorageFolder, "PreservedSnapshots")); }
                catch (Exception ex) { ShowInfo("復旧できませんでした。\n" + ex.Message); }
            };
            buttons.Controls.Add(button);
        }
        Add("本体データ", _store.CanUseCombinedData, false); Add("分離データ", _store.CanUseSeparatedData, true);
        var reload = new Forms.Button { Text = "ディスクから再読込", AutoSize = true };
        reload.Click += (_, _) =>
        {
            if (AskYesNo("ディスクの保存データを再読込しますか？未保存の編集・履歴は失われる可能性があります。必要なら先に全データをエクスポートしてください。") != true) return;
            _store.Load(); ApplyFilter(); RefreshTrayMenu(); form.Close(); ShowStorageRecovery();
        };
        buttons.Controls.Add(reload);
        var export = new Forms.Button { Text = "現在の内容をエクスポート", AutoSize = true };
        export.Click += (_, _) => ExportFullBackup(); buttons.Controls.Add(export);
        form.Controls.Add(preview); form.Controls.Add(buttons); form.ShowDialog();
    }

}
