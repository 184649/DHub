using System.IO;
using System.Windows;
using ShortcutList.Models;
using ShortcutList.Services;
using Forms = System.Windows.Forms;

namespace ShortcutList.Views;

public partial class MainWindow
{
    private sealed record LinkedLaunch(BookmarkSnapshot Snapshot, BookmarkEntry Entry);
    private IReadOnlyList<ShortcutItem>? _quickScope;
    private int _quickGeneration;
    private bool _quickAddPending;
    private readonly HashSet<string> _confirmedQuickSets = new(StringComparer.Ordinal);

    private void EnsureQuickSearchWindow()
    {
        if (_quickSearchWindow is not null) return;
        _quickSearchWindow = new QuickSearchWindow();
        _quickSearchWindow.Closed += (_, _) => { _quickGeneration++; _quickSearchWindow = null; };
        _quickSearchWindow.LaunchRequested += QuickSearchWindow_LaunchRequested;
        _quickSearchWindow.AddRequested += QuickSearchWindow_AddRequested;
        _quickSearchWindow.RefreshRequested += (_, _) => RefreshQuickSources();
        _quickSearchWindow.WorkspaceRequested += (_, _) => CreateQuickWorkspace();
        _quickSearchWindow.SettingsRequested += (_, _) => ShowHotkeySettings();
        _quickSearchWindow.ManagementRequested += (_, _) => ShowMainWindowFromTray();
        _quickSearchWindow.ExitRequested += (_, _) => RequestExit();
    }

    private void ShowQuickSearch() => ShowQuickSearch(null);

    private void ShowQuickSearch(IReadOnlyList<ShortcutItem>? scope)
    {
        if (_isExitRequested || ActivateExistingDialog()) return;
        _notifyIcon?.ContextMenuStrip?.Close();
        EnsureQuickSearchWindow();
        _quickScope = scope;
        _quickGeneration++;
        var (entries, status) = BuildQuickEntries();
        _quickSearchWindow!.ShowSearch(entries, status);
        RefreshQuickSources();
    }

    private (IReadOnlyList<QuickLaunchEntry> Entries, string Status) BuildQuickEntries()
    {
        var entries = new List<QuickLaunchEntry>();
        var notices = new List<string>();
        var direct = _quickScope ?? _store.Items.ToArray();
        foreach (var item in direct.Where(x => _store.Items.Contains(x)))
            entries.Add(new("item:" + item.Id, item.Name, item.TargetPath,
                item.IsBroken ? "見つからない対象です。場所を確認してください" : item.LaunchRecipeDisplay,
                item.IconText, item.IsFavorite, item.SortOrder, item.SearchText,
                QuickLaunchKind.Shortcut, item, Recipe(item)));
        if (_quickScope is not null) return (entries, "選んだ一覧内を検索中。全件は呼出キーから。");

        foreach (var workspace in _store.Workspaces)
        {
            var items = _store.GetWorkspaceItems(workspace).ToArray();
            entries.Add(new("set:" + workspace.Id, workspace.Name, string.Join(" / ", items.Select(x => x.Name).Take(4)),
                $"まとめて開く · {items.Length} 件 · 最初の実行時に対象を確認", "▦", false, int.MaxValue - 2,
                workspace.SearchText + " " + string.Join(" ", items.Select(x => x.Name)),
                QuickLaunchKind.Workspace, workspace, QuickWorkspaceRecipe(workspace), items.Length > 0));
        }

        const int linkedLimit = 20000;
        var linkedCount = 0;
        foreach (var source in _store.BookmarkSources)
        {
            var snapshot = _bookmarkService.GetSnapshot(source.Id);
            entries.Add(new("source:" + source.Id, source.Name + "（登録元）", source.FolderPath,
                snapshot.Status, "📁", false, int.MaxValue - 1, source.Name + " " + source.FolderPath,
                QuickLaunchKind.SourceFolder, source, source.FolderPath));
            foreach (var item in snapshot.Entries)
            {
                if (linkedCount >= linkedLimit) break;
                linkedCount++;
                var name = Path.GetExtension(item.Name).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(item.Name).Equals(".url", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(item.Name) : item.Name;
                entries.Add(new("linked:" + source.Id + ":" + item.RelativePath, name, item.FullPath,
                    "連携: " + source.Name + (snapshot.CanLaunch ? "" : " · " + snapshot.Status), item.IsFolder ? "📁" : "↗",
                    false, int.MaxValue, name + " " + item.RelativePath + " " + source.Name,
                    QuickLaunchKind.LinkedBookmark, new LinkedLaunch(snapshot, item),
                    snapshot.Revision + ":" + item.Fingerprint, snapshot.CanLaunch));
            }
            if (!snapshot.CanLaunch || snapshot.IsTruncated) notices.Add(source.Name + ": " + snapshot.Status);
        }
        if (linkedCount >= linkedLimit) notices.Add("連携一覧は2万件まで。登録元フォルダーから全件を確認できます");
        return (entries, string.Join(" / ", notices));
    }

    private void RefreshQuickEntriesIfVisible()
    {
        if (_isExitRequested || _quickSearchWindow?.IsVisible != true) return;
        var (entries, status) = BuildQuickEntries();
        _quickSearchWindow.UpdateSource(entries, status);
    }

    private async void RefreshQuickSources()
    {
        var generation = _quickGeneration;
        var sources = _store.BookmarkSources.ToArray();
        try
        {
            // Display direct registrations immediately; each completed source becomes available independently.
            await Task.WhenAll(sources.Select(async source =>
            {
                await _bookmarkService.RefreshAsync(source);
                if (!_isExitRequested && generation == _quickGeneration) RefreshQuickEntriesIfVisible();
            }));
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (!_isExitRequested && generation == _quickGeneration && _quickSearchWindow?.IsVisible == true)
                _quickSearchWindow.SetAddStatus(_quickAddPending, "連携の読込に失敗しました: " + ex.Message);
        }
    }

    private string QuickWorkspaceRecipe(WorkspaceItem workspace) => System.Text.Json.JsonSerializer.Serialize(new
    {
        workspace.Id, workspace.DelaySeconds, workspace.ShortcutIds,
        Items = _store.GetWorkspaceItems(workspace).Select(Recipe).ToArray()
    });

    private void QuickSearchWindow_LaunchRequested(object? sender, QuickLaunchEntry entry)
    {
        if (_isExitRequested) return;
        switch (entry.Payload)
        {
            case ShortcutItem item when entry.Kind == QuickLaunchKind.Shortcut:
                if (!_store.Items.Contains(item) || Recipe(item) != entry.RecipeToken) { ShowInfo("登録が変わりました。一覧を開き直してください。"); return; }
                LaunchItem(item);
                break;
            case LinkedLaunch linked when entry.Kind == QuickLaunchKind.LinkedBookmark:
                LaunchBookmark(linked.Snapshot, linked.Entry);
                break;
            case BookmarkSource source when entry.Kind == QuickLaunchKind.SourceFolder:
                if (source.FolderPath != entry.RecipeToken) { ShowInfo("登録元が変わりました。一覧を開き直してください。"); return; }
                OpenBookmarkRoot(source);
                break;
            case WorkspaceItem workspace when entry.Kind == QuickLaunchKind.Workspace:
                if (!_store.Workspaces.Contains(workspace) || QuickWorkspaceRecipe(workspace) != entry.RecipeToken) { ShowInfo("組合せが変わりました。一覧を開き直してください。"); return; }
                LaunchQuickWorkspace(workspace, entry.RecipeToken);
                break;
        }
    }

    private bool ConfirmQuickWorkspace(WorkspaceItem workspace, ShortcutItem[] items)
    {
        using var form = new Forms.Form { Text = $"{workspace.Name}: {items.Length} 件をまとめて開く", Width = 760, Height = 470, StartPosition = Forms.FormStartPosition.CenterScreen };
        var explanation = new Forms.Label { Dock = Forms.DockStyle.Top, Height = 48, Text = "次の登録をすべて開きます。この組合せは起動中のDHubでは次回から確認を省略します。\n内容が変わった場合やDHubを再起動した場合は、もう一度確認します。" };
        var list = new Forms.ListBox { Dock = Forms.DockStyle.Fill, HorizontalScrollbar = true };
        foreach (var item in items) list.Items.Add(item.Name + "  | 対象: " + item.TargetPath +
            "  | 対象引数: " + item.Arguments + "  | 起動アプリ: " +
            (string.IsNullOrWhiteSpace(ShortcutRunner.GetEffectiveOpenApplicationPath(item)) ? "Windowsの既定" : ShortcutRunner.GetEffectiveOpenApplicationPath(item)) +
            "  | アプリ引数: " + item.OpenApplicationArguments);
        var buttons = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 46, FlowDirection = Forms.FlowDirection.RightToLeft };
        var cancel = new Forms.Button { Text = "キャンセル", AutoSize = true, DialogResult = Forms.DialogResult.Cancel };
        var open = new Forms.Button { Text = $"{items.Length} 件を開く", AutoSize = true, DialogResult = Forms.DialogResult.OK };
        buttons.Controls.Add(cancel); buttons.Controls.Add(open);
        form.Controls.Add(list); form.Controls.Add(explanation); form.Controls.Add(buttons);
        form.CancelButton = cancel;
        // No default Enter action: choosing the set does not accidentally confirm the second step.
        return form.ShowDialog() == Forms.DialogResult.OK;
    }

    private async void LaunchQuickWorkspace(WorkspaceItem workspace, string recipe)
    {
        if (_launchingWorkspaces.Contains(workspace.Id)) { ShowInfo("この組合せは起動処理中です。"); return; }
        var items = _store.GetWorkspaceItems(workspace).ToArray();
        if (items.Length == 0) return;
        if (!_confirmedQuickSets.Contains(recipe))
        {
            if (!ConfirmQuickWorkspace(workspace, items)) return;
            if (!_store.Workspaces.Contains(workspace) || QuickWorkspaceRecipe(workspace) != recipe) { ShowInfo("確認中に組合せが変わりました。一覧を開き直してください。"); return; }
            _confirmedQuickSets.Add(recipe);
        }
        if (!_launchingWorkspaces.Add(workspace.Id)) return;
        var launched = 0;
        try
        {
            foreach (var item in items)
            {
                if (_isExitRequested || !_store.Workspaces.Contains(workspace) || QuickWorkspaceRecipe(workspace) != recipe)
                { if (!_isExitRequested) ShowInfo("組合せが変更されたため、残りの起動を止めました。"); break; }
                if (LaunchItem(item)) launched++;
                if (workspace.DelaySeconds > 0 && item != items[^1]) await Task.Delay(TimeSpan.FromSeconds(workspace.DelaySeconds));
            }
            if (launched > 0) { workspace.TouchLaunched(); PersistLaunchHistory("Workspace", "組合せの起動処理を終了", $"{workspace.Name}: {launched}/{items.Length} 件へ起動要求を送信"); }
        }
        finally { _launchingWorkspaces.Remove(workspace.Id); RefreshTrayMenu(); }
    }

    private void CreateQuickWorkspace()
    {
        if (!EnsureEditingAllowed()) return;
        var workspace = new WorkspaceItem();
        if (ShowWorkspaceEditDialog(workspace, isNew: true))
        {
            try { _store.AddWorkspace(workspace); RefreshTrayMenu(); }
            catch (Exception ex) { _store.Workspaces.Remove(workspace); ShowInfo("組合せを保存できませんでした。保存データの確認をしてください。\n" + ex.Message); }
        }
        ShowQuickSearch();
    }

    private async void QuickSearchWindow_AddRequested(object? sender, QuickAddRequest request)
    {
        if (_quickAddPending || !EnsureEditingAllowed()) return;
        if (request.Targets.Count > 100) { _quickSearchWindow?.SetAddStatus(false, "一度に追加できるのは100件までです。"); return; }
        _quickAddPending = true;
        _quickSearchWindow?.SetAddStatus(true, "対象を確認しています。フォルダーの中身は取り込みません。");
        var added = new List<ShortcutItem>();
        try
        {
            var candidates = await Task.Run(() => request.Targets.Select(raw =>
            {
                var target = raw.Trim().Trim('"');
                var type = ShortcutDetector.DetectType(target);
                return type is null ? null : new ShortcutItem { Name = ShortcutDetector.GuessName(target), TargetPath = target,
                    ShortcutType = type.Value, OpenApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(type.Value),
                    IsFavorite = true, GroupName = "未分類" };
            }).ToArray());
            if (_isExitRequested) return;
            if (!EnsureEditingAllowed()) return;
            var duplicateCount = 0;
            foreach (var item in candidates)
            {
                if (item is null) continue;
                if (_store.ContainsEquivalent(item)) { duplicateCount++; continue; }
                item.SortOrder = _store.Items.Count;
                _store.Items.Add(item); added.Add(item);
            }
            if (added.Count > 0) _store.Save();
            RefreshFilterItemsKeepSelection(); ApplyFilter();
            _quickScope = null;
            RefreshQuickEntriesIfVisible();
            _quickSearchWindow?.SetAddStatus(false, $"{added.Count} 件を登録 / 既存 {duplicateCount} 件 / 未確認 {candidates.Count(x => x is null)} 件。対象は開いていません。");
        }
        catch (Exception ex)
        {
            foreach (var item in added) _store.Items.Remove(item);
            RefreshQuickEntriesIfVisible();
            _quickSearchWindow?.SetAddStatus(false, "登録できませんでした。保存状態を管理画面で確認してください: " + ex.Message);
        }
        finally { _quickAddPending = false; _quickSearchWindow?.FinishAdd(); }
    }
}
