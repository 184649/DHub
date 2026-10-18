using System.Windows;
using ShortcutList.Models;

namespace ShortcutList.Views;

// The old floating bar is retired. Stored positions and favorites are not erased.
public partial class MainWindow
{
    private List<ShortcutItem> OrderedLauncherPins()
    {
        var items = _store.Items.Where(x => x.IsFavorite).ToDictionary(x => x.Id, StringComparer.Ordinal);
        return LauncherBarLayout.OrderedIds(items.Values.Select(x => new LauncherPin(x.Id, x.Name, x.SortOrder)))
            .Select(id => items[id]).ToList();
    }

    private string LauncherLabel(ShortcutItem item, int length) => LauncherBarLayout.Label(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.TypeText : item.Name, length,
        _store.Items.Select(x => new LauncherPin(x.Id, string.IsNullOrWhiteSpace(x.Name) ? x.TypeText : x.Name, x.SortOrder)));

    private void ShowLauncherBarButton_Click(object sender, RoutedEventArgs e) => ShowQuickSearch();
    private void PinSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ShortcutListView.SelectedItems.Cast<ShortcutItem>().ToArray();
        if (selected.Length == 0) { ShowInfo("お気に入りに追加する項目を一覧で選択してください。"); return; }
        SetPinnedItems(selected, true);
    }
    private void BookmarkSourcesButton_Click(object sender, RoutedEventArgs e) => ShowBookmarkSourceManager();

    private bool SavePinChange(Action change, Action rollback)
    {
        if (!EnsureEditingAllowed()) return false;
        var selectedIds = ShortcutListView.SelectedItems.Cast<ShortcutItem>().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        void RefreshSelection()
        {
            ApplyFilter();
            foreach (var item in _visibleItems.Where(x => selectedIds.Contains(x.Id)))
                if (!ShortcutListView.SelectedItems.Contains(item)) ShortcutListView.SelectedItems.Add(item);
        }
        var error = LauncherBarLayout.SaveEdit(change, _store.Save, rollback);
        if (error is not null)
        {
            RefreshSelection();
            ShowInfo("お気に入りの変更を保存できませんでした。画面は変更前に戻しましたが、ディスクへの保存が完了したか不明な場合があります。「保存データの確認 / 復旧」で確認してください。\n" + error.Message);
            return false;
        }
        RefreshFilterItemsKeepSelection();
        RefreshSelection();
        return true;
    }

    private bool SetPinnedItems(IEnumerable<ShortcutItem> selection, bool pinned)
    {
        var selected = selection.Where(x => _store.Items.Contains(x)).Distinct().ToArray();
        if (selected.Length == 0) return false;
        if (selected.All(x => x.IsFavorite == pinned)) return true;
        var before = _store.Items.Select(x => (Item: x, x.IsFavorite, x.SortOrder, x.UpdatedAt)).ToArray();
        var pins = OrderedLauncherPins();
        return SavePinChange(() =>
        {
            foreach (var item in selected)
            {
                if (item.IsFavorite == pinned) continue;
                item.IsFavorite = pinned;
                item.UpdatedAt = DateTime.Now;
                if (pinned) pins.Add(item);
            }
            if (pinned) for (var i = 0; i < pins.Count; i++) pins[i].SortOrder = i;
        }, () => { foreach (var old in before) { old.Item.IsFavorite = old.IsFavorite; old.Item.SortOrder = old.SortOrder; old.Item.UpdatedAt = old.UpdatedAt; } });
    }

    private void MovePinnedItem(ShortcutItem item, int direction)
    {
        var pins = OrderedLauncherPins();
        if (!pins.Contains(item)) return;
        var ids = pins.Select(x => x.Id).ToArray();
        var next = LauncherBarLayout.Move(ids, item.Id, direction);
        if (next.SequenceEqual(ids)) return;
        var before = pins.Select(x => (Item: x, x.SortOrder, x.UpdatedAt)).ToArray();
        SavePinChange(() =>
        {
            for (var i = 0; i < next.Count; i++) pins.Single(x => x.Id == next[i]).SortOrder = i;
            item.UpdatedAt = DateTime.Now;
        }, () => { foreach (var old in before) { old.Item.SortOrder = old.SortOrder; old.Item.UpdatedAt = old.UpdatedAt; } });
    }

}
