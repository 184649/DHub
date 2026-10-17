using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShortcutList.Models;
using ShortcutList.Services;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace ShortcutList.Views;

public partial class MainWindow
{
    private StackPanel? _launcherPinsPanel;
    private WpfButton? _launcherOverflow;
    private bool _positioningLauncher;
    private bool _draggingLauncher;
    private string? _launcherContentSignature;
    private ShortcutItem[] _launcherBoundPins = Array.Empty<ShortcutItem>();
    private int? _launcherSessionLeft;
    private int? _launcherSessionTop;

    [StructLayout(LayoutKind.Sequential)]
    private struct LauncherNativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out LauncherNativeRect rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private List<ShortcutItem> OrderedLauncherPins()
    {
        var items = _store.Items.Where(x => x.IsFavorite).ToDictionary(x => x.Id, StringComparer.Ordinal);
        return LauncherBarLayout.OrderedIds(items.Values.Select(x => new LauncherPin(x.Id, x.Name, x.SortOrder)))
            .Select(id => items[id]).ToList();
    }

    private string LauncherLabel(ShortcutItem item, int length) => LauncherBarLayout.Label(item.Id,
        string.IsNullOrWhiteSpace(item.Name) ? item.TypeText : item.Name, length,
        _store.Items.Select(x => new LauncherPin(x.Id, string.IsNullOrWhiteSpace(x.Name) ? x.TypeText : x.Name, x.SortOrder)));

    private void LaunchPinnedItem(ShortcutItem item, string recipe)
    {
        if (!item.IsFavorite || Recipe(item) != recipe)
        {
            ShowInfo("固定バーの登録または起動方法が変更されました。選び直してください。");
            RefreshLauncherBar();
            return;
        }
        LaunchItem(item); // Also revalidates membership; history-save errors never repeat a shell request.
    }

    private static WpfButton BarButton(object content, string tooltip, double width) => new()
    {
        Content = content, ToolTip = tooltip, Width = width, Height = 34,
        Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(2, 0, 2, 0),
        Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(215, 220, 226)),
        BorderThickness = new Thickness(1), Cursor = Cursors.Hand
    };

    private void ShowLauncherBarButton_Click(object sender, RoutedEventArgs e) => SetLauncherBarVisible(true);
    private void PinSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ShortcutListView.SelectedItems.Cast<ShortcutItem>().ToArray();
        if (selected.Length == 0) { ShowInfo("固定バーに追加する項目を一覧で選択してください。ファイル・フォルダー・URLを追加できます。"); return; }
        if (SetPinnedItems(selected, true)) SetLauncherBarVisible(true);
    }
    private void BookmarkSourcesButton_Click(object sender, RoutedEventArgs e) => ShowBookmarkSourceManager();

    private void SetLauncherBarVisible(bool visible)
    {
        // Visibility remains usable in recovery/read-only mode. A failed preference save is explicit.
        if (visible) ShowLauncherBar(); else _launcherBar?.Hide();
        if (_store.Settings.ShowLauncherBar == visible || _store.IsReadOnly) return;
        var previous = _store.Settings.ShowLauncherBar;
        _store.Settings.ShowLauncherBar = visible;
        try { _store.Save(); }
        catch (Exception ex)
        {
            _store.Settings.ShowLauncherBar = previous;
            ShowInfo("表示は今回の起動中だけ変更しました。次回の表示設定を保存できませんでした。保存データを確認してください。\n" + ex.Message);
        }
    }

    private void ShowLauncherBar()
    {
        if (_launcherBar is null)
        {
            _launcherSessionLeft = _store.Settings.LauncherBarLeft;
            _launcherSessionTop = _store.Settings.LauncherBarTop;
            _launcherPinsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
            var grip = new Border { Width = 20, Background = Brushes.Transparent, Cursor = Cursors.SizeAll,
                ToolTip = "ここをドラッグして固定バーを移動", Child = new TextBlock { Text = "⠿", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center } };
            row.Children.Add(grip);
            var menu = BarButton("DHub ▾", "お気に入り・検索・管理メニュー", 64);
            menu.Click += (_, _) => ShowLauncherPopup();
            row.Children.Add(menu);
            row.Children.Add(_launcherPinsPanel);
            _launcherOverflow = BarButton("»", "表示しきれない固定項目", 36);
            _launcherOverflow.Click += (_, _) => ShowLauncherOverflow();
            row.Children.Add(_launcherOverflow);
            var add = BarButton("＋ 追加", "ファイル・フォルダー・URLを固定バーに追加。バーへのドロップでも追加できます。", 70);
            add.Click += (_, _) => OpenPinnedAddDialog();
            row.Children.Add(add);
            var hide = BarButton("×", "固定バーを隠す（DHubは終了しません）。管理画面の「固定バーを表示」で戻せます。", 28);
            hide.Click += (_, _) => SetLauncherBarVisible(false);
            row.Children.Add(hide);
            _launcherBar = new Window
            {
                Title = "DHub 固定バー", MinHeight = 48, SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, AllowDrop = true,
                Content = new Border { Child = row, Background = new SolidColorBrush(Color.FromRgb(243, 245, 248)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(160, 172, 188)), BorderThickness = new Thickness(1) }
            };
            _launcherBar.DragOver += (_, e) =>
            {
                e.Effects = !_store.IsReadOnly && CanAcceptDrop(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
                e.Handled = true;
            };
            _launcherBar.Drop += LauncherBar_Drop;
            _launcherBar.Closing += (_, e) =>
            {
                if (_isExitRequested) return;
                e.Cancel = true;
                SetLauncherBarVisible(false);
            };
            SystemParameters.StaticPropertyChanged += LauncherDisplaySettingsChanged;
            _launcherBar.Closed += (_, _) => SystemParameters.StaticPropertyChanged -= LauncherDisplaySettingsChanged;
            _launcherBar.ContentRendered += (_, _) => PositionLauncherBar();
            _launcherBar.SizeChanged += (_, _) => { if (_launcherBar.IsVisible) PositionLauncherBar(); };
            grip.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                _draggingLauncher = true;
                try { _launcherBar.DragMove(); }
                catch (InvalidOperationException) { return; }
                finally { _draggingLauncher = false; }
                PositionLauncherBar(useCurrentPosition: true);
                SaveLauncherBarPosition();
                RefreshLauncherBar();
                e.Handled = true;
            };
        }
        RefreshLauncherBar();
        _launcherBar.Show();
        _launcherBar.UpdateLayout();
        PositionLauncherBar();
    }

    private Forms.Screen LauncherScreen(bool useCurrentPosition = false)
    {
        if (useCurrentPosition && _launcherBar is not null)
            return Forms.Screen.FromHandle(new WindowInteropHelper(_launcherBar).EnsureHandle());
        if (_launcherSessionLeft is int left && _launcherSessionTop is int top)
            return Forms.Screen.FromPoint(new System.Drawing.Point(left, top));
        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }

    private void PositionLauncherBar(bool useCurrentPosition = false)
    {
        if (_launcherBar is null || _positioningLauncher || _draggingLauncher) return;
        _positioningLauncher = true;
        try
        {
            var handle = new WindowInteropHelper(_launcherBar).EnsureHandle();
            if (!GetWindowRect(handle, out var bounds)) return;
            var area = LauncherScreen(useCurrentPosition).WorkingArea;
            var point = LauncherBarLayout.Place(new(area.Left, area.Top, area.Width, area.Height),
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                useCurrentPosition ? bounds.Left : _launcherSessionLeft,
                useCurrentPosition ? bounds.Top : _launcherSessionTop);
            // Position only this ordinary application window; never changes taskbar/appbar or security settings.
            SetWindowPos(handle, IntPtr.Zero, (int)point.Left, (int)point.Top, 0, 0, 0x0015); // no size, z-order, or activation change
        }
        finally { _positioningLauncher = false; }
    }

    private void SaveLauncherBarPosition()
    {
        if (_launcherBar is null) return;
        if (!GetWindowRect(new WindowInteropHelper(_launcherBar).Handle, out var bounds)) return;
        _launcherSessionLeft = bounds.Left;
        _launcherSessionTop = bounds.Top;
        if (_store.IsReadOnly) return;
        var oldLeft = _store.Settings.LauncherBarLeft;
        var oldTop = _store.Settings.LauncherBarTop;
        if (oldLeft == bounds.Left && oldTop == bounds.Top) return;
        _store.Settings.LauncherBarLeft = bounds.Left;
        _store.Settings.LauncherBarTop = bounds.Top;
        try { _store.Save(); }
        catch (Exception ex)
        {
            _store.Settings.LauncherBarLeft = oldLeft;
            _store.Settings.LauncherBarTop = oldTop;
            ShowInfo("固定バーの位置を保存できませんでした。保存データを確認してください。\n" + ex.Message);
        }
    }

    private void LauncherDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isExitRequested || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_isExitRequested) return;
            RefreshLauncherBar(); PositionLauncherBar();
        }));
    }

    private int LauncherVisiblePinCount(int count)
    {
        var area = LauncherScreen().WorkingArea;
        var scale = _launcherBar is null ? 1 : VisualTreeHelper.GetDpi(_launcherBar).DpiScaleX;
        return LauncherBarLayout.VisiblePinCount(count, area.Width / scale);
    }

    private void RefreshLauncherBar()
    {
        if (_launcherBar is null || _launcherPinsPanel is null || _launcherOverflow is null) return;
        var pins = OrderedLauncherPins();
        var visibleCount = LauncherVisiblePinCount(pins.Count);
        var availableWidth = LauncherScreen().WorkingArea.Width / VisualTreeHelper.GetDpi(_launcherBar).DpiScaleX;
        _launcherBar.MaxWidth = Math.Max(1, availableWidth);
        // History-only updates must not rebuild a clicked/focused button or move keyboard focus.
        var signature = System.Text.Json.JsonSerializer.Serialize(new { visibleCount, ReadOnly = _store.IsReadOnly,
            availableWidth, Pins = pins.Select(x => new { Recipe = Recipe(x), Label = LauncherLabel(x, 13) }) });
        if (_launcherContentSignature == signature && LauncherBarLayout.SameInstances(_launcherBoundPins, pins)) return;
        _launcherContentSignature = signature;
        _launcherBoundPins = pins.ToArray();
        _launcherPinsPanel.Children.Clear();
        if (pins.Count == 0)
        {
            _launcherPinsPanel.Children.Add(new TextBlock { Text = "ファイル・フォルダー・URLをドロップ", FontSize = 12, MaxWidth = Math.Max(0, Math.Min(220, availableWidth - 228)), TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0), ToolTip = "管理画面の「選択をバーに追加」でも固定できます。お気に入りが固定バーの項目になります。" });
        }
        foreach (var item in pins.Take(visibleCount))
        {
            var recipe = Recipe(item);
            var label = new TextBlock { Text = $"{item.IconText} {LauncherLabel(item, 13)}", TextTrimming = TextTrimming.CharacterEllipsis };
            var button = BarButton(label, item.LaunchRecipeTooltip + "\n右クリック: 左右に移動 / バーから外す", 120);
            button.Click += (_, _) => LaunchPinnedItem(item, recipe);
            button.ContextMenu = CreatePinContextMenu(item);
            _launcherPinsPanel.Children.Add(button);
        }
        _launcherOverflow.Visibility = pins.Count > visibleCount ? Visibility.Visible : Visibility.Collapsed;
        _launcherOverflow.ToolTip = $"残り {pins.Count - visibleCount} 件を表示（右クリックで並べ替え）";
    }

    private WpfMenu CreatePinContextMenu(ShortcutItem item)
    {
        var menu = new WpfMenu();
        void Add(string label, Action action, bool enabled = true)
        {
            var entry = new WpfMenuItem { Header = label, IsEnabled = enabled };
            entry.Click += (_, _) => action(); menu.Items.Add(entry);
        }
        var pins = OrderedLauncherPins();
        var index = pins.IndexOf(item);
        Add("← 左へ移動", () => MovePinnedItem(item, -1), !_store.IsReadOnly && index > 0);
        Add("右へ移動 →", () => MovePinnedItem(item, 1), !_store.IsReadOnly && index >= 0 && index < pins.Count - 1);
        Add("バーから外す（登録は残す）", () => SetPinnedItems(new[] { item }, false), !_store.IsReadOnly);
        return menu;
    }

    private void ShowLauncherOverflow()
    {
        if (_launcherOverflow is null) return;
        var pins = OrderedLauncherPins();
        var menu = new WpfMenu { PlacementTarget = _launcherOverflow, Placement = PlacementMode.Top,
            MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height - 80) };
        foreach (var item in pins.Skip(LauncherVisiblePinCount(pins.Count)))
        {
            var recipe = Recipe(item);
            var row = new WpfMenuItem { Header = new TextBlock { Text = $"{item.IconText} {LauncherLabel(item, 30)}" }, ToolTip = item.LaunchRecipeTooltip,
                ContextMenu = CreatePinContextMenu(item) };
            row.Click += (_, _) => LaunchPinnedItem(item, recipe);
            menu.Items.Add(row);
        }
        _launcherOverflow.ContextMenu = menu;
        menu.IsOpen = true;
    }

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
            ShowInfo("固定バーの変更を保存できませんでした。画面は変更前に戻しましたが、ディスクへの保存が完了したか不明な場合があります。「保存データの確認 / 復旧」で確認してください。\n" + error.Message);
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

    private static LauncherPinRecipe PinRecipe(ShortcutItem item) => new(item.TargetPath, item.Arguments,
        item.OpenApplicationPath, item.OpenApplicationArguments, (int)item.ShortcutType);
    private static bool SameLaunchRecipe(ShortcutItem a, ShortcutItem b) => PinRecipe(a) == PinRecipe(b);

    private bool AddPinnedCandidates(IEnumerable<ShortcutItem> candidates)
    {
        var additions = new List<ShortcutItem>();
        var targets = new List<ShortcutItem>();
        foreach (var candidate in candidates)
        {
            var existing = _store.Items.Concat(additions).FirstOrDefault(x => SameLaunchRecipe(x, candidate));
            if (existing is null) { additions.Add(candidate); existing = candidate; }
            if (!targets.Contains(existing)) targets.Add(existing);
        }
        if (targets.Count == 0) return false;
        if (additions.Count == 0) return SetPinnedItems(targets, true);
        var before = _store.Items.Select(x => (Item: x, x.IsFavorite, x.SortOrder, x.UpdatedAt)).ToArray();
        var pins = OrderedLauncherPins();
        return SavePinChange(() =>
        {
            foreach (var item in additions) _store.Items.Add(item);
            foreach (var item in targets)
            {
                if (pins.Contains(item)) continue;
                item.IsFavorite = true; item.UpdatedAt = DateTime.Now; pins.Add(item);
            }
            for (var i = 0; i < pins.Count; i++) pins[i].SortOrder = i;
        }, () =>
        {
            foreach (var item in additions) _store.Items.Remove(item);
            foreach (var old in before) { old.Item.IsFavorite = old.IsFavorite; old.Item.SortOrder = old.SortOrder; old.Item.UpdatedAt = old.UpdatedAt; }
        });
    }

    private void OpenPinnedAddDialog()
    {
        if (!EnsureEditingAllowed()) return;
        var dialog = new ShortcutEditWindow { Owner = _launcherBar ?? this };
        dialog.ConfigureForPinnedAdd();
        if (dialog.ShowDialog() == true && dialog.ResultItem is not null) AddPinnedCandidates(new[] { dialog.ResultItem });
    }

    private void LauncherBar_Drop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (!EnsureEditingAllowed()) return;
        var targets = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[] : null;
        if (targets is null && e.Data.GetDataPresent(System.Windows.DataFormats.Text))
            targets = (e.Data.GetData(System.Windows.DataFormats.Text)?.ToString() ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targets is null) return;
        var candidates = new List<ShortcutItem>();
        var skipped = 0;
        foreach (var target in targets)
        {
            var type = ShortcutDetector.DetectType(target);
            if (type is null) { skipped++; continue; }
            candidates.Add(new ShortcutItem { Name = ShortcutDetector.GuessName(target), TargetPath = target,
                ShortcutType = type.Value, OpenApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(type.Value) });
        }
        if (candidates.Count > 0 && !AddPinnedCandidates(candidates)) return;
        if (skipped > 0) ShowInfo($"追加できない項目が {skipped} 件ありました。存在するファイル・フォルダー、または http/https URLを指定してください。");
    }
}
