using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;
using ShortcutList.Models;
using ShortcutList.Services;

namespace ShortcutList.Views;

public partial class MainWindow : Window
{
    private const int QuickSearchHotkeyId = 0x534C;
    private const int MainWindowHotkeyId = 0x534D;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private const uint VkD = 0x44;
    private const int WmHotkey = 0x0312;

    private const string AllFilter = "すべて";
    private const string AllGroupsFilter = "すべてのグループ";
    private const string AllTagsFilter = "すべてのタグ";
    private const string FolderFilter = "フォルダのみ";
    private const string UrlFilter = "URLのみ";
    private const string FileFilter = "ファイルのみ";
    private const string FavoriteFilter = "お気に入りのみ";
    private const string BrokenFilter = "未検出のみ";

    private readonly ShortcutStore _store = new();
    private readonly ObservableCollection<ShortcutItem> _visibleItems = new();
    private readonly UpdateService _updateService = new();

    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _trayIcon;
    private bool _isExitRequested;
    private bool _isOpeningFromTray;
    private QuickSearchWindow? _quickSearchWindow;
    private HwndSource? _source;
    private string _sortKey = "Manual";
    private bool _sortAscending = true;

    private const double NameColumnDefaultWidth = 240;
    private const double PathColumnDefaultWidth = 460;
    private const double ArgumentsColumnDefaultWidth = 150;
    private const double OpenApplicationColumnDefaultWidth = 150;
    private const double GroupColumnDefaultWidth = 120;
    private const double TagsColumnDefaultWidth = 160;
    private const double LaunchCountColumnDefaultWidth = 90;
    private const double LastLaunchColumnDefaultWidth = 150;
    private const double CreatedAtColumnDefaultWidth = 150;
    private const double UpdatedAtColumnDefaultWidth = 150;
    private const double StatusColumnDefaultWidth = 90;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();

        ShortcutListView.ItemsSource = _visibleItems;

        _store.Load();
        FileIconService.IsEnabled = _store.Settings.UseRealFileIcons;
        if (_store.WasRecoveredOnLoad && _store.Settings.ShowRestoreMessageAfterRecovery)
        {
            Dispatcher.BeginInvoke(new Action(() => ShowInfo(_store.LastLoadMessage)));
        }

        InitializeFilter();
        ApplyColumnVisibility();
        ApplyFilter();
        InitializeTrayIcon();

        VersionTextBlock.Text = $"v{_updateService.CurrentVersion}";

        // 初回起動時はウィンドウを一切表示しないため、Loaded ではなく
        // コンストラクター完了後にバックグラウンドで自動更新確認を実行します。
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_store.Settings.CheckUpdateOnStartup)
            {
                _ = CheckUpdateAsync(silent: true);
            }
            if (_store.Settings.ShowHomeOnStartup && Visibility == Visibility.Visible)
            {
                ShowHomeDashboard();
            }
        }));
    }

    private ShortcutItem? SelectedItem => ShortcutListView.SelectedItem as ShortcutItem;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveColumnWidths();

        SearchTextBox.Focus();
        Keyboard.Focus(SearchTextBox);

        // MainWindow は初回起動時には Show() されないため、
        // トレイアイコンの初回ダブルクリックで初めて Loaded が発生します。
        // そのタイミングで StartMinimizedToTray を見て Hide() してしまうと、
        // 「1回目だけ一瞬表示されてすぐ消える」動きになります。
        // トレイから明示的に開いた場合は、初回 Loaded でも必ず表示を維持します。
        if (_store.Settings.StartMinimizedToTray && !_isOpeningFromTray)
        {
            Hide();
        }

        _isOpeningFromTray = false;

        await Task.CompletedTask;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        if (_store.Settings.EnableGlobalHotkeys)
        {
            RegisterHotKey(helper.Handle, QuickSearchHotkeyId, ModControl | ModShift, VkSpace);
            RegisterHotKey(helper.Handle, MainWindowHotkeyId, ModControl | ModShift, VkD);
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        UnregisterHotKey(helper.Handle, QuickSearchHotkeyId);
        UnregisterHotKey(helper.Handle, MainWindowHotkeyId);
        _source?.RemoveHook(WndProc);
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == QuickSearchHotkeyId)
            {
                ShowQuickSearch();
                handled = true;
            }
            else if (id == MainWindowHotkeyId)
            {
                ShowMainWindowFromTray();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void InitializeFilter()
    {
        FilterComboBox.Items.Clear();
        FilterComboBox.Items.Add(AllFilter);
        FilterComboBox.Items.Add(FolderFilter);
        FilterComboBox.Items.Add(UrlFilter);
        FilterComboBox.Items.Add(FileFilter);
        FilterComboBox.Items.Add(FavoriteFilter);
        FilterComboBox.Items.Add(BrokenFilter);
        FilterComboBox.SelectedItem = AllFilter;

        GroupFilterComboBox.Items.Clear();
        GroupFilterComboBox.Items.Add(AllGroupsFilter);
        foreach (var group in _store.GetGroups())
        {
            GroupFilterComboBox.Items.Add(group);
        }
        GroupFilterComboBox.SelectedItem = AllGroupsFilter;

        TagFilterComboBox.Items.Clear();
        TagFilterComboBox.Items.Add(AllTagsFilter);
        foreach (var tag in _store.GetTags())
        {
            TagFilterComboBox.Items.Add(tag);
        }
        TagFilterComboBox.SelectedItem = AllTagsFilter;

        SortComboBox.Items.Clear();
        SortComboBox.Items.Add("手動順");
        SortComboBox.Items.Add("お気に入り優先");
        SortComboBox.Items.Add("最近使った順");
        SortComboBox.Items.Add("使用回数順");
        SortComboBox.Items.Add("名前順");
        SortComboBox.Items.Add("グループ順");
        SortComboBox.Items.Add("タグ順");
        SortComboBox.SelectedItem = "手動順";
    }

    private void RefreshFilterItemsKeepSelection()
    {
        var current = FilterComboBox.SelectedItem?.ToString() ?? AllFilter;
        var currentGroup = GroupFilterComboBox.SelectedItem?.ToString() ?? AllGroupsFilter;
        var currentTag = TagFilterComboBox.SelectedItem?.ToString() ?? AllTagsFilter;
        var currentSort = SortComboBox.SelectedItem?.ToString() ?? "手動順";

        FilterComboBox.SelectionChanged -= FilterComboBox_SelectionChanged;
        GroupFilterComboBox.SelectionChanged -= GroupFilterComboBox_SelectionChanged;
        TagFilterComboBox.SelectionChanged -= TagFilterComboBox_SelectionChanged;
        SortComboBox.SelectionChanged -= SortComboBox_SelectionChanged;

        InitializeFilter();

        FilterComboBox.SelectedItem = FilterComboBox.Items.Contains(current) ? current : AllFilter;
        GroupFilterComboBox.SelectedItem = GroupFilterComboBox.Items.Contains(currentGroup) ? currentGroup : AllGroupsFilter;
        TagFilterComboBox.SelectedItem = TagFilterComboBox.Items.Contains(currentTag) ? currentTag : AllTagsFilter;
        SortComboBox.SelectedItem = SortComboBox.Items.Contains(currentSort) ? currentSort : "手動順";

        FilterComboBox.SelectionChanged += FilterComboBox_SelectionChanged;
        GroupFilterComboBox.SelectionChanged += GroupFilterComboBox_SelectionChanged;
        TagFilterComboBox.SelectionChanged += TagFilterComboBox_SelectionChanged;
        SortComboBox.SelectionChanged += SortComboBox_SelectionChanged;
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedItem?.Id;
        var keyword = SearchTextBox.Text?.Trim() ?? string.Empty;

        var words = keyword
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        var filter = FilterComboBox.SelectedItem?.ToString() ?? AllFilter;

        IEnumerable<ShortcutItem> items = _store.Items;

        if (filter == FolderFilter)
        {
            items = items.Where(x => x.ShortcutType == ShortcutType.Folder);
        }
        else if (filter == UrlFilter)
        {
            items = items.Where(x => x.ShortcutType == ShortcutType.Url);
        }
        else if (filter == FileFilter)
        {
            items = items.Where(x => x.ShortcutType == ShortcutType.File);
        }
        else if (filter == FavoriteFilter)
        {
            items = items.Where(x => x.IsFavorite);
        }
        else if (filter == BrokenFilter)
        {
            items = items.Where(x => x.IsBroken);
        }
        var groupFilter = GroupFilterComboBox.SelectedItem?.ToString() ?? AllGroupsFilter;
        if (!string.Equals(groupFilter, AllGroupsFilter, StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(x => string.Equals(x.GroupDisplay, groupFilter, StringComparison.OrdinalIgnoreCase));
        }

        var tagFilter = TagFilterComboBox.SelectedItem?.ToString() ?? AllTagsFilter;
        if (!string.Equals(tagFilter, AllTagsFilter, StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(x => x.HasTag(tagFilter));
        }

        if (words.Count > 0)
        {
            items = items.Where(x => words.All(word => x.SearchText.Contains(word)));
        }

        var sortMode = SortComboBox.SelectedItem?.ToString() ?? "手動順";
        if (words.Count > 0 && _sortKey == "Manual" && sortMode == "手動順")
        {
            items = ApplySearchScoreSort(items, words);
        }
        else
        {
            items = ApplySort(items);
        }

        _visibleItems.Clear();

        foreach (var item in items)
        {
            _visibleItems.Add(item);
        }

        if (selectedId is not null)
        {
            var selected = _visibleItems.FirstOrDefault(x => x.Id == selectedId);
            if (selected is not null)
            {
                ShortcutListView.SelectedItem = selected;
            }
        }

        CountTextBlock.Text = $"{_visibleItems.Count} 個のショートカット";
        HealthStatusTextBlock.Text = $"未検出 {_store.Items.Count(x => x.IsBroken)} 件";
        RefreshSortHeader();
        RefreshTrayMenu();
    }

    private IEnumerable<ShortcutItem> ApplySearchScoreSort(IEnumerable<ShortcutItem> items, IReadOnlyList<string> words)
    {
        return items
            .Select(item => new { Item = item, Score = CalculateSearchScore(item, words) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.IsFavorite)
            .ThenByDescending(x => x.Item.LastLaunchedAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.Item.LaunchCount)
            .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Item);
    }

    private static int CalculateSearchScore(ShortcutItem item, IReadOnlyList<string> words)
    {
        var score = 0;

        foreach (var word in words)
        {
            score += CalculateFieldScore(item.Name, word, 10000, 8000, 6000);
            score += CalculateFieldScore(item.TagsDisplay, word, 6500, 5200, 4200);
            score += CalculateFieldScore(item.GroupDisplay, word, 6000, 4800, 3800);
            score += CalculateFieldScore(item.OpenApplicationDisplay, word, 4200, 3400, 2600);
            score += CalculateFieldScore(item.TypeText, word, 3800, 3000, 2200);
            score += CalculateFieldScore(item.TargetPath, word, 2400, 1800, 1200);
            score += CalculateFieldScore(item.MemoDisplay, word, 1800, 1300, 900);
            score += CalculateFieldScore(item.StatusText, word, 1200, 900, 600);
        }

        if (item.IsFavorite)
        {
            score += 800;
        }

        if (item.LastLaunchedAt.HasValue)
        {
            var days = Math.Max(0, (DateTime.Now - item.LastLaunchedAt.Value).TotalDays);
            score += Math.Max(0, 500 - (int)Math.Min(500, days * 25));
        }

        score += Math.Min(400, item.LaunchCount * 20);

        return score;
    }

    private static int CalculateFieldScore(string? value, string word, int exactScore, int startsWithScore, int containsScore)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(word))
        {
            return 0;
        }

        var normalizedValue = NormalizeSearchValue(value);
        var normalizedWord = NormalizeSearchValue(word);

        if (string.Equals(normalizedValue, normalizedWord, StringComparison.OrdinalIgnoreCase))
        {
            return exactScore;
        }

        if (normalizedValue.StartsWith(normalizedWord, StringComparison.OrdinalIgnoreCase))
        {
            return startsWithScore;
        }

        if (normalizedValue.Contains(normalizedWord, StringComparison.OrdinalIgnoreCase))
        {
            return containsScore;
        }

        return 0;
    }

    private static string NormalizeSearchValue(string value)
    {
        return value
            .Replace("　", " ")
            .Replace("_", " ")
            .Replace("-", " ")
            .Replace("/", " ")
            .Replace("\\", " ")
            .Trim()
            .ToLowerInvariant();
    }

    private IEnumerable<ShortcutItem> ApplySort(IEnumerable<ShortcutItem> items)
    {
        var sortMode = SortComboBox.SelectedItem?.ToString() ?? "手動順";
        if (sortMode == "お気に入り優先")
        {
            return items.OrderByDescending(x => x.IsFavorite)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (sortMode == "最近使った順")
        {
            return items.OrderByDescending(x => x.IsFavorite)
                .ThenByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (sortMode == "使用回数順")
        {
            return items.OrderByDescending(x => x.IsFavorite)
                .ThenByDescending(x => x.LaunchCount)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (sortMode == "名前順")
        {
            return items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (sortMode == "グループ順")
        {
            return items.OrderBy(x => x.GroupDisplay, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(x => x.IsFavorite)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (sortMode == "タグ順")
        {
            return items.OrderBy(x => x.TagsDisplay, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(x => x.IsFavorite)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        return _sortKey switch
        {
            "Favorite" => _sortAscending
                ? items.OrderBy(x => x.IsFavorite)
                : items.OrderByDescending(x => x.IsFavorite),


            "Name" => _sortAscending
                ? items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase),

            "Path" => _sortAscending
                ? items.OrderBy(x => x.TargetPath, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.TargetPath, StringComparer.CurrentCultureIgnoreCase),

            "Arguments" => _sortAscending
                ? items.OrderBy(x => x.Arguments, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.Arguments, StringComparer.CurrentCultureIgnoreCase),

            "OpenApplication" => _sortAscending
                ? items.OrderBy(x => x.OpenApplicationDisplay, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.OpenApplicationDisplay, StringComparer.CurrentCultureIgnoreCase),


            "Group" => _sortAscending
                ? items.OrderBy(x => x.GroupDisplay, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.GroupDisplay, StringComparer.CurrentCultureIgnoreCase),

            "Tags" => _sortAscending
                ? items.OrderBy(x => x.TagsDisplay, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.TagsDisplay, StringComparer.CurrentCultureIgnoreCase),

            "LaunchCount" => _sortAscending
                ? items.OrderBy(x => x.LaunchCount)
                : items.OrderByDescending(x => x.LaunchCount),

            "LastLaunch" => _sortAscending
                ? items.OrderBy(x => x.LastLaunchedAt ?? DateTime.MinValue)
                : items.OrderByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue),

            "CreatedAt" => _sortAscending
                ? items.OrderBy(x => x.CreatedAt)
                : items.OrderByDescending(x => x.CreatedAt),

            "UpdatedAt" => _sortAscending
                ? items.OrderBy(x => x.UpdatedAt)
                : items.OrderByDescending(x => x.UpdatedAt),

            "Status" => _sortAscending
                ? items.OrderBy(x => x.StatusText, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(x => x.StatusText, StringComparer.CurrentCultureIgnoreCase),

            _ => items.OrderBy(x => x.SortOrder).ThenBy(x => _store.Items.IndexOf(x))
        };
    }

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader header || header.Column is null)
        {
            return;
        }

        var key = GetSortKeyFromHeader(header.Column.Header?.ToString() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_sortKey == key)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortKey = key;
            _sortAscending = true;
        }

        if (SortComboBox.Items.Contains("手動順"))
        {
            SortComboBox.SelectedItem = "手動順";
        }

        ApplyFilter();
    }

    private static string GetSortKeyFromHeader(string headerText)
    {
        var clean = headerText.Replace(" ▲", string.Empty).Replace(" ▼", string.Empty);

        return clean switch
        {
            "★" => "Favorite",
            "お気に入り" => "Favorite",
            "名前" => "Name",
            "パス" => "Path",
            "引数" => "Arguments",
            "開くアプリ" => "OpenApplication",
            "グループ" => "Group",
            "タグ" => "Tags",
            "起動回数" => "LaunchCount",
            "最終起動" => "LastLaunch",
            "作成日" => "CreatedAt",
            "更新日" => "UpdatedAt",
            "状態" => "Status",
            _ => string.Empty
        };
    }

    private void RefreshSortHeader()
    {
        FavoriteColumn.Header = "★";
        NameColumn.Header = "名前";
        PathColumn.Header = "パス";
        ArgumentsColumn.Header = "引数";
        OpenApplicationColumn.Header = "開くアプリ";
        GroupColumn.Header = "グループ";
        TagsColumn.Header = "タグ";
        LaunchCountColumn.Header = "起動回数";
        LastLaunchColumn.Header = "最終起動";
        CreatedAtColumn.Header = "作成日";
        UpdatedAtColumn.Header = "更新日";
        StatusColumn.Header = "状態";

        var arrow = _sortAscending ? " ▲" : " ▼";
        var label = "手動";

        switch (_sortKey)
        {
            case "Favorite":
                FavoriteColumn.Header = "★" + arrow;
                label = "お気に入り";
                break;
            case "Name":
                NameColumn.Header = "名前" + arrow;
                label = "名前";
                break;
            case "Path":
                PathColumn.Header = "パス" + arrow;
                label = "パス";
                break;
            case "Arguments":
                ArgumentsColumn.Header = "引数" + arrow;
                label = "引数";
                break;
            case "OpenApplication":
                OpenApplicationColumn.Header = "開くアプリ" + arrow;
                label = "開くアプリ";
                break;
            case "Group":
                GroupColumn.Header = "グループ" + arrow;
                label = "グループ";
                break;
            case "Tags":
                TagsColumn.Header = "タグ" + arrow;
                label = "タグ";
                break;
            case "LaunchCount":
                LaunchCountColumn.Header = "起動回数" + arrow;
                label = "起動回数";
                break;
            case "LastLaunch":
                LastLaunchColumn.Header = "最終起動" + arrow;
                label = "最終起動";
                break;
            case "CreatedAt":
                CreatedAtColumn.Header = "作成日" + arrow;
                label = "作成日";
                break;
            case "UpdatedAt":
                UpdatedAtColumn.Header = "更新日" + arrow;
                label = "更新日";
                break;
            case "Status":
                StatusColumn.Header = "状態" + arrow;
                label = "状態";
                break;
        }

        var sortMode = SortComboBox.SelectedItem?.ToString() ?? "手動順";
        var hasSearchKeyword = !string.IsNullOrWhiteSpace(SearchTextBox?.Text);
        SortStatusTextBlock.Text = hasSearchKeyword && _sortKey == "Manual" && sortMode == "手動順"
            ? "並び順: 検索スコア順"
            : sortMode != "手動順"
                ? $"並び順: {sortMode}"
                : _sortKey == "Manual"
                    ? "並び順: 手動"
                    : $"並び順: {label} {(_sortAscending ? "昇順" : "降順")}";
    }

    private void ApplyColumnVisibility()
    {
        ApplyResponsiveColumnWidths();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveColumnWidths();
    }

    private void ApplyResponsiveColumnWidths()
    {
        if (!IsInitialized || ShortcutListView is null)
        {
            return;
        }

        var availableWidth = ShortcutListView.ActualWidth - 112;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            availableWidth = ActualWidth - 160;
        }

        availableWidth = Math.Max(360, availableWidth);

        var totalDefaultWidth = 0.0;
        totalDefaultWidth += _store.Settings.ShowNameColumn ? NameColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowPathColumn ? PathColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowArgumentsColumn ? ArgumentsColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowOpenApplicationColumn ? OpenApplicationColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowGroupColumn ? GroupColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowTagsColumn ? TagsColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowLaunchCountColumn ? LaunchCountColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowLastLaunchColumn ? LastLaunchColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowCreatedAtColumn ? CreatedAtColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowUpdatedAtColumn ? UpdatedAtColumnDefaultWidth : 0;
        totalDefaultWidth += _store.Settings.ShowStatusColumn ? StatusColumnDefaultWidth : 0;

        if (totalDefaultWidth <= 0)
        {
            return;
        }

        var scale = Math.Min(1.0, availableWidth / totalDefaultWidth);

        SetResponsiveColumnWidth(NameColumn, _store.Settings.ShowNameColumn, NameColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(PathColumn, _store.Settings.ShowPathColumn, PathColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(ArgumentsColumn, _store.Settings.ShowArgumentsColumn, ArgumentsColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(OpenApplicationColumn, _store.Settings.ShowOpenApplicationColumn, OpenApplicationColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(GroupColumn, _store.Settings.ShowGroupColumn, GroupColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(TagsColumn, _store.Settings.ShowTagsColumn, TagsColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(LaunchCountColumn, _store.Settings.ShowLaunchCountColumn, LaunchCountColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(LastLaunchColumn, _store.Settings.ShowLastLaunchColumn, LastLaunchColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(CreatedAtColumn, _store.Settings.ShowCreatedAtColumn, CreatedAtColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(UpdatedAtColumn, _store.Settings.ShowUpdatedAtColumn, UpdatedAtColumnDefaultWidth, scale);
        SetResponsiveColumnWidth(StatusColumn, _store.Settings.ShowStatusColumn, StatusColumnDefaultWidth, scale);
    }

    private static void SetResponsiveColumnWidth(GridViewColumn column, bool isVisible, double defaultWidth, double scale)
    {
        column.Width = isVisible ? Math.Max(28, Math.Floor(defaultWidth * scale)) : 0;
    }

    private void ColumnSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { StaysOpen = true };

        AddColumnMenuItem(menu, "名前", _store.Settings.ShowNameColumn, value => _store.Settings.ShowNameColumn = value);
        AddColumnMenuItem(menu, "パス", _store.Settings.ShowPathColumn, value => _store.Settings.ShowPathColumn = value);
        AddColumnMenuItem(menu, "引数", _store.Settings.ShowArgumentsColumn, value => _store.Settings.ShowArgumentsColumn = value);
        AddColumnMenuItem(menu, "開くアプリ", _store.Settings.ShowOpenApplicationColumn, value => _store.Settings.ShowOpenApplicationColumn = value);
        AddColumnMenuItem(menu, "グループ", _store.Settings.ShowGroupColumn, value => _store.Settings.ShowGroupColumn = value);
        AddColumnMenuItem(menu, "タグ", _store.Settings.ShowTagsColumn, value => _store.Settings.ShowTagsColumn = value);
        AddColumnMenuItem(menu, "起動回数", _store.Settings.ShowLaunchCountColumn, value => _store.Settings.ShowLaunchCountColumn = value);
        AddColumnMenuItem(menu, "最終起動", _store.Settings.ShowLastLaunchColumn, value => _store.Settings.ShowLastLaunchColumn = value);
        AddColumnMenuItem(menu, "作成日", _store.Settings.ShowCreatedAtColumn, value => _store.Settings.ShowCreatedAtColumn = value);
        AddColumnMenuItem(menu, "更新日", _store.Settings.ShowUpdatedAtColumn, value => _store.Settings.ShowUpdatedAtColumn = value);
        AddColumnMenuItem(menu, "状態", _store.Settings.ShowStatusColumn, value => _store.Settings.ShowStatusColumn = value);

        menu.IsOpen = true;
    }

    private void AddColumnMenuItem(ContextMenu menu, string header, bool isChecked, Action<bool> setter)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = isChecked,
            StaysOpenOnClick = true
        };

        item.Click += (_, _) =>
        {
            setter(item.IsChecked);
            ApplyColumnVisibility();
            _store.Save();
        };

        menu.Items.Add(item);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, "ホーム", ShowHomeDashboard);
        AddMenuItem(menu, "統合検索 / コマンドパレット", ShowCommandPalette);
        AddMenuItem(menu, "コマンドショートカット管理", ShowCommandManager);
        AddMenuItem(menu, "操作ログ", ShowOperationLogWindow);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "設定画面を開く", ShowSettingsScreen);
        AddMenuItem(menu, "バックアップから復元", ShowRestoreWizard);
        AddMenuItem(menu, "一括編集", ShowBulkEditDialog);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "全データをエクスポート", ExportFullBackup);
        AddMenuItem(menu, "ショートカットをエクスポート", ExportShortcuts);
        AddMenuItem(menu, "ショートカットをインポート", ImportShortcuts);
        AddMenuItem(menu, "CSVエクスポート", ExportCsv);
        AddMenuItem(menu, "CSVインポート", ImportCsv);
        AddMenuItem(menu, "手動バックアップ作成", () => ManualBackupButton_Click(this, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "設定をエクスポート", ExportSettings);
        AddMenuItem(menu, "設定をインポート", ImportSettings);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "ワークスペース管理", ShowWorkspaceManager);
        AddMenuItem(menu, "グループ管理", ManageGroups);
        AddMenuItem(menu, "重複を整理", CleanupDuplicates);
        AddMenuItem(menu, "データ保存場所を開く", OpenStorageFolder);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "更新を確認", () => _ = CheckUpdateAsync(false));

        menu.IsOpen = true;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAddDialog();
    }

    private void OpenAddDialog()
    {
        var dialog = new ShortcutEditWindow { Owner = this };

        if (dialog.ShowDialog() != true || dialog.ResultItem is null)
        {
            return;
        }

        if (_store.ContainsTarget(dialog.ResultItem.TargetPath))
        {
            System.Windows.MessageBox.Show(
                "同じパスまたはURLがすでに登録されています。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        _store.Add(dialog.ResultItem);
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
        {
            return;
        }

        var dialog = new ShortcutEditWindow(SelectedItem) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.ResultItem is null)
        {
            return;
        }

        if (_store.ContainsTarget(dialog.ResultItem.TargetPath, SelectedItem.Id))
        {
            System.Windows.MessageBox.Show(
                "同じパスまたはURLがすでに登録されています。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        SelectedItem.Name = dialog.ResultItem.Name;
        SelectedItem.TargetPath = dialog.ResultItem.TargetPath;
        SelectedItem.Arguments = dialog.ResultItem.Arguments;
        SelectedItem.OpenApplicationPath = dialog.ResultItem.OpenApplicationPath;
        SelectedItem.OpenApplicationArguments = dialog.ResultItem.OpenApplicationArguments;
        SelectedItem.ShortcutType = dialog.ResultItem.ShortcutType;
        SelectedItem.IsFavorite = dialog.ResultItem.IsFavorite;
        SelectedItem.GroupName = dialog.ResultItem.GroupName;
        SelectedItem.Tags = dialog.ResultItem.Tags;
        SelectedItem.TagColor = dialog.ResultItem.TagColor;
        SelectedItem.TouchUpdated();

        _store.Save();
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
    }

    private void FavoriteCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ShortcutItem item })
        {
            return;
        }

        ShortcutListView.SelectedItem = item;
        ToggleFavorite(item);
        e.Handled = true;
    }


    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (System.Windows.MessageBox.Show(
                $"「{SelectedItem.Name}」を削除しますか？",
                "DHub",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        _store.Remove(SelectedItem);
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
    }

    private void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        ExportShortcuts();
    }

    private void ExportShortcuts()
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "ショートカットをエクスポート",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"ShortcutList_Shortcuts_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _store.ExportShortcutsToFile(dialog.FileName);
        ShowInfo("ショートカットをエクスポートしました。");
    }

    private void ImportShortcuts()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = "ショートカットをインポート",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        if (AskYesNo("現在のショートカット一覧をインポートした内容で置き換えます。続行しますか？") != true)
        {
            return;
        }

        _store.ImportShortcutsFromFile(dialog.FileName);
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
        ShowInfo("ショートカットをインポートしました。");
    }

    private void ExportSettings()
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "設定をエクスポート",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"ShortcutList_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _store.ExportSettingsToFile(dialog.FileName);
        ShowInfo("設定をエクスポートしました。");
    }

    private void ImportSettings()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = "設定をインポート",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        if (AskYesNo("現在の表示設定・起動設定をインポートした設定で置き換えます。続行しますか？") != true)
        {
            return;
        }

        _store.ImportSettingsFromFile(dialog.FileName);
        ApplyColumnVisibility();
        RefreshTrayMenu();
        ShowInfo("設定をインポートしました。");
    }

    private void ManageGroups()
    {
        var groups = _store.GetGroups().Where(x => x != "未分類").ToList();
        if (groups.Count == 0)
        {
            ShowInfo("管理できるグループはありません。");
            return;
        }

        using var selectDialog = new Forms.Form
        {
            Text = "グループ管理",
            Width = 360,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        var combo = new Forms.ComboBox { Left = 16, Top = 16, Width = 300, DropDownStyle = Forms.ComboBoxStyle.DropDownList };
        combo.Items.AddRange(groups.Cast<object>().ToArray());
        combo.SelectedIndex = 0;

        var rename = new Forms.Button { Text = "名前変更", Left = 16, Top = 60, Width = 100 };
        var delete = new Forms.Button { Text = "削除", Left = 126, Top = 60, Width = 100 };
        var close = new Forms.Button { Text = "閉じる", Left = 236, Top = 60, Width = 80 };

        rename.Click += (_, _) =>
        {
            var oldName = combo.SelectedItem?.ToString() ?? "";
            var input = Microsoft.VisualBasic.Interaction.InputBox("新しいグループ名を入力してください。", "グループ名変更", oldName);
            if (string.IsNullOrWhiteSpace(input)) return;
            _store.RenameGroup(oldName, input.Trim());
            selectDialog.DialogResult = Forms.DialogResult.OK;
            selectDialog.Close();
        };

        delete.Click += (_, _) =>
        {
            var group = combo.SelectedItem?.ToString() ?? "";
            if (Forms.MessageBox.Show($"「{group}」を未分類に戻しますか？", "DHub", Forms.MessageBoxButtons.YesNo) != Forms.DialogResult.Yes) return;
            _store.DeleteGroupName(group);
            selectDialog.DialogResult = Forms.DialogResult.OK;
            selectDialog.Close();
        };

        close.Click += (_, _) => selectDialog.Close();

        selectDialog.Controls.Add(combo);
        selectDialog.Controls.Add(rename);
        selectDialog.Controls.Add(delete);
        selectDialog.Controls.Add(close);

        if (selectDialog.ShowDialog() == Forms.DialogResult.OK)
        {
            RefreshFilterItemsKeepSelection();
            ApplyFilter();
        }
    }

    private void CleanupDuplicates()
    {
        var groups = _store.GetDuplicateGroups();
        if (groups.Count == 0)
        {
            ShowInfo("重複しているショートカットはありません。");
            return;
        }

        var removeCount = groups.Sum(x => x.Count() - 1);

        if (AskYesNo($"重複しているショートカットが {removeCount} 件あります。2件目以降を削除しますか？") != true)
        {
            return;
        }

        var removed = _store.RemoveDuplicateExtras();
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
        ShowInfo($"{removed} 件の重複を削除しました。");
    }

    private void OpenStorageFolder()
    {
        ShortcutRunner.OpenFolder(_store.StorageFolder);
    }

    private void CheckLinksButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _store.Items)
        {
            item.RefreshStatus();
        }

        var brokenCount = _store.Items.Count(x => x.IsBroken);
        ApplyFilter();

        System.Windows.MessageBox.Show(
            brokenCount == 0
                ? "存在しないファイル・フォルダはありません。"
                : $"存在しないファイル・フォルダが {brokenCount} 件あります。フィルタで「未検出のみ」を選ぶと確認できます。",
            "DHub",
            System.Windows.MessageBoxButton.OK,
            brokenCount == 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null) return;
        ShortcutRunner.OpenFolder(SelectedItem.TargetPath);
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null)
        {
            if (_visibleItems.Count == 1)
            {
                LaunchItem(_visibleItems[0]);
            }
            return;
        }

        LaunchItem(SelectedItem);
    }

    private void LaunchItem(ShortcutItem item)
    {
        try
        {
            ShortcutRunner.Run(item);
            item.TouchLaunched();
            _store.Save();
            _store.AddLog("Launch", "ショートカットを起動", item.Name + " / " + item.TargetPath);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _store.AddLog("Launch", "ショートカット起動失敗", item.Name + " / " + ex.Message, "Error");
            System.Windows.MessageBox.Show(
                $"ショートカットを開けませんでした。\n\n対象: {item.TargetPath}\n開くアプリ: {item.OpenApplicationDisplay}\n\n{ex.Message}",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void ToggleFavorite(ShortcutItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        item.TouchUpdated();
        _store.Save();
        _store.AddLog("Favorite", item.IsFavorite ? "お気に入り登録" : "お気に入り解除", item.Name);
        ApplyFilter();
    }

    private void FavoriteButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (SelectedItem is null)
        {
            return;
        }

        ToggleFavorite(SelectedItem);
    }

    private void ResetOpenApplication(ShortcutItem item)
    {
        item.OpenApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(item.ShortcutType);
        item.OpenApplicationArguments = string.Empty;
        item.TouchUpdated();
        _store.Save();
        ApplyFilter();
    }

    private void SelectOpenApplication(ShortcutItem item)
    {
        var dialog = new OpenApplicationPickerWindow(
            item.ShortcutType,
            item.OpenApplicationPath,
            item.OpenApplicationArguments,
            item.TargetPath)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        item.OpenApplicationPath = dialog.ResultApplicationPath;
        item.OpenApplicationArguments = dialog.ResultApplicationArguments;
        item.TouchUpdated();
        _store.Save();
        ApplyFilter();
    }

    private void ShowItemContextMenu(ShortcutItem item)
    {
        ShortcutListView.ContextMenu = BuildItemContextMenu(item);
        ShortcutListView.ContextMenu.IsOpen = true;
    }

    private ContextMenu BuildItemContextMenu(ShortcutItem item)
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, "開く", () => LaunchItem(item));
        AddMenuItem(menu, "場所を開く", () => ShortcutRunner.OpenFolder(item.TargetPath));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, item.IsFavorite ? "お気に入り解除" : "お気に入り登録", () => ToggleFavorite(item));
        AddMenuItem(menu, "開くアプリを候補から選択...", () => SelectOpenApplication(item));
        AddMenuItem(menu, "開くアプリを既定に戻す", () => ResetOpenApplication(item));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "編集", () => EditButton_Click(this, new RoutedEventArgs()));
        AddMenuItem(menu, "メモを編集", () => EditShortcutMemo(item));
        AddMenuItem(menu, "削除", () => DeleteButton_Click(this, new RoutedEventArgs()));
        AddMenuItem(menu, "パスをコピー", () => WpfClipboard.SetText(item.TargetPath));

        return menu;
    }

    private void ShortcutListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            ShortcutListView.SelectedItem = null;
            return;
        }

        item.Focus();
        item.IsSelected = true;
        if (item.DataContext is ShortcutItem shortcut)
        {
            ShortcutListView.ContextMenu = BuildItemContextMenu(shortcut);
        }
    }

    private void ShortcutListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SelectedItem is null)
        {
            e.Handled = true;
            return;
        }

        ShortcutListView.ContextMenu = BuildItemContextMenu(SelectedItem);
    }

    private void EditShortcutMemo(ShortcutItem item)
    {
        if (EditMemo($"メモ - {item.Name}", item.Memo, out var memo))
        {
            item.Memo = memo;
            item.TouchUpdated();
            _store.SaveChanges();
            _store.AddLog("Memo", "ショートカットメモを更新", item.Name);
            ApplyFilter();
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T matched)
            {
                return matched;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void TagFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SortComboBox.SelectedItem?.ToString() ?? "手動順";
        _sortKey = selected == "手動順" ? "Manual" : _sortKey;
        ApplyFilter();
    }

    private void ShortcutListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedItem is null) return;
        LaunchItem(SelectedItem);
    }

    private void ShortcutListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = CanAcceptDrop(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        var added = false;
        var duplicateCount = 0;

        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
            e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                if (_store.ContainsTarget(path))
                {
                    duplicateCount++;
                    continue;
                }

                added |= TryAddFromTarget(path);
            }
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
        {
            var text = e.Data.GetData(System.Windows.DataFormats.Text)?.ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var target = text.Trim();

                if (_store.ContainsTarget(target))
                {
                    duplicateCount++;
                }
                else
                {
                    added |= TryAddFromTarget(target);
                }
            }
        }

        if (added)
        {
            RefreshFilterItemsKeepSelection();
            ApplyFilter();
        }

        if (duplicateCount > 0)
        {
            ShowInfo($"重複しているため追加しなかった項目が {duplicateCount} 件あります。");
        }

        e.Handled = true;
    }

    private static bool CanAcceptDrop(System.Windows.DragEventArgs e)
    {
        return e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
               e.Data.GetDataPresent(System.Windows.DataFormats.Text);
    }

    private bool TryAddFromTarget(string target)
    {
        var type = ShortcutDetector.DetectType(target);
        if (type is null) return false;

        var item = new ShortcutItem
        {
            Name = ShortcutDetector.GuessName(target),
            TargetPath = target,
            ShortcutType = type.Value,
            OpenApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(type.Value),
            GroupName = "未分類",
            SortOrder = _store.Items.Count,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _store.Add(item);
        return true;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.N)
        {
            OpenAddDialog();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Space)
        {
            ShowQuickSearch();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P)
        {
            ShowCommandPalette();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D)
        {
            ShowMainWindowFromTray();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            RunButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                 e.Key == Key.C &&
                 SelectedItem is not null)
        {
            WpfClipboard.SetText(SelectedItem.TargetPath);
            e.Handled = true;
        }
    }

    private void ShowQuickSearch()
    {
        _quickSearchWindow ??= new QuickSearchWindow();
        _quickSearchWindow.LaunchRequested -= QuickSearchWindow_LaunchRequested;
        _quickSearchWindow.LaunchRequested += QuickSearchWindow_LaunchRequested;
        _quickSearchWindow.ShowSearch(_store.Items.ToList());
    }

    private void QuickSearchWindow_LaunchRequested(object? sender, ShortcutItem item)
    {
        LaunchItem(item);
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            var resourceInfo = WpfApplication.GetResourceStream(new Uri("pack://application:,,,/Assets/DHub.ico"));
            if (resourceInfo?.Stream is not null)
            {
                return new Drawing.Icon(resourceInfo.Stream);
            }
        }
        catch
        {
            // アイコン読込に失敗した場合でもアプリ自体は起動できるようにします。
        }

        return Drawing.SystemIcons.Application;
    }

    private void ShowMainWindowFromTray()
    {
        _isOpeningFromTray = true;

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        Focus();

        SearchTextBox.Focus();
        Keyboard.Focus(SearchTextBox);
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = LoadApplicationIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "DHub",
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) =>
        {
            Dispatcher.Invoke(ShowMainWindowFromTray);
        };

        RefreshTrayMenu();
    }

    private void RefreshTrayMenu()
    {
        if (_notifyIcon is null) return;

        var menu = new Forms.ContextMenuStrip();

        var favorites = _store.Items
            .Where(x => x.IsFavorite)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var favoriteMenu = new Forms.ToolStripMenuItem("お気に入り");
        if (favorites.Count == 0)
        {
            favoriteMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("登録なし") { Enabled = false });
        }
        else
        {
            foreach (var item in favorites.Take(30))
            {
                AddTrayLaunchItem(favoriteMenu, item);
            }
        }
        menu.Items.Add(favoriteMenu);

        var recentItems = _store.Items
            .Where(x => x.LastLaunchedAt.HasValue)
            .OrderByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(15)
            .ToList();

        var recentMenu = new Forms.ToolStripMenuItem("最近使った項目");
        if (recentItems.Count == 0)
        {
            recentMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("履歴なし") { Enabled = false });
        }
        else
        {
            foreach (var item in recentItems)
            {
                AddTrayLaunchItem(recentMenu, item);
            }
        }
        menu.Items.Add(recentMenu);

        var homeItemRoot = new Forms.ToolStripMenuItem("ホーム");
        homeItemRoot.Click += (_, _) => Dispatcher.Invoke(ShowHomeDashboard);
        menu.Items.Add(homeItemRoot);

        var commandPaletteRoot = new Forms.ToolStripMenuItem("統合検索 / コマンドパレット");
        commandPaletteRoot.Click += (_, _) => Dispatcher.Invoke(ShowCommandPalette);
        menu.Items.Add(commandPaletteRoot);

        var workspaceRoot = new Forms.ToolStripMenuItem("ワークスペース");
        if (_store.Workspaces.Count == 0)
        {
            workspaceRoot.DropDownItems.Add(new Forms.ToolStripMenuItem("登録なし") { Enabled = false });
        }
        else
        {
            foreach (var workspace in _store.Workspaces.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var workspaceItem = new Forms.ToolStripMenuItem($"▶ {workspace.Name}");
                workspaceItem.ToolTipText = workspace.Description;
                workspaceItem.Click += (_, _) => Dispatcher.Invoke(() => LaunchWorkspace(workspace));
                workspaceRoot.DropDownItems.Add(workspaceItem);
            }
        }
        menu.Items.Add(workspaceRoot);

        var commandRoot = new Forms.ToolStripMenuItem("コマンド");
        if (_store.Commands.Count == 0)
        {
            commandRoot.DropDownItems.Add(new Forms.ToolStripMenuItem("登録なし") { Enabled = false });
        }
        else
        {
            foreach (var command in _store.Commands.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Take(40))
            {
                var commandItem = new Forms.ToolStripMenuItem($"{command.FavoriteText} {command.Name}");
                commandItem.ToolTipText = command.Command + " " + command.Arguments;
                commandItem.Click += (_, _) => Dispatcher.Invoke(() => RunCommand(command));
                commandRoot.DropDownItems.Add(commandItem);
            }
        }
        menu.Items.Add(commandRoot);

        var groupRoot = new Forms.ToolStripMenuItem("グループ");
        var groupedItems = _store.Items
            .GroupBy(x => x.GroupDisplay, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == "未分類" ? "zzzzzz" : g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (groupedItems.Count == 0)
        {
            groupRoot.DropDownItems.Add(new Forms.ToolStripMenuItem("登録なし") { Enabled = false });
        }
        else
        {
            foreach (var group in groupedItems)
            {
                var groupMenu = new Forms.ToolStripMenuItem(group.Key);
                foreach (var item in group.OrderByDescending(x => x.IsFavorite)
                                          .ThenByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue)
                                          .ThenBy(x => x.SortOrder)
                                          .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                                          .Take(40))
                {
                    AddTrayLaunchItem(groupMenu, item);
                }

                groupRoot.DropDownItems.Add(groupMenu);
            }
        }
        menu.Items.Add(groupRoot);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var showItem = new Forms.ToolStripMenuItem("管理画面を開く");
        showItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindowFromTray);
        menu.Items.Add(showItem);

        var spotlightItem = new Forms.ToolStripMenuItem("クイック検索");
        spotlightItem.Click += (_, _) => Dispatcher.Invoke(ShowQuickSearch);
        menu.Items.Add(spotlightItem);

        var addItem = new Forms.ToolStripMenuItem("追加");
        addItem.Click += (_, _) => Dispatcher.Invoke(OpenAddDialog);
        menu.Items.Add(addItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exportShortcutsItem = new Forms.ToolStripMenuItem("ショートカットをエクスポート");
        exportShortcutsItem.Click += (_, _) => Dispatcher.Invoke(ExportShortcuts);
        menu.Items.Add(exportShortcutsItem);

        var importShortcutsItem = new Forms.ToolStripMenuItem("ショートカットをインポート");
        importShortcutsItem.Click += (_, _) => Dispatcher.Invoke(ImportShortcuts);
        menu.Items.Add(importShortcutsItem);

        var exportSettingsItem = new Forms.ToolStripMenuItem("設定をエクスポート");
        exportSettingsItem.Click += (_, _) => Dispatcher.Invoke(ExportSettings);
        menu.Items.Add(exportSettingsItem);

        var importSettingsItem = new Forms.ToolStripMenuItem("設定をインポート");
        importSettingsItem.Click += (_, _) => Dispatcher.Invoke(ImportSettings);
        menu.Items.Add(importSettingsItem);

        var logItem = new Forms.ToolStripMenuItem("操作ログ");
        logItem.Click += (_, _) => Dispatcher.Invoke(ShowOperationLogWindow);
        menu.Items.Add(logItem);

        var commandManageItem = new Forms.ToolStripMenuItem("コマンド管理");
        commandManageItem.Click += (_, _) => Dispatcher.Invoke(ShowCommandManager);
        menu.Items.Add(commandManageItem);

        var storageItem = new Forms.ToolStripMenuItem("データ保存場所を開く");
        storageItem.Click += (_, _) => Dispatcher.Invoke(OpenStorageFolder);
        menu.Items.Add(storageItem);

        var startTrayItem = new Forms.ToolStripMenuItem("起動時はトレイのみ")
        {
            Checked = _store.Settings.StartMinimizedToTray,
            CheckOnClick = true
        };
        startTrayItem.Click += (_, _) =>
        {
            _store.Settings.StartMinimizedToTray = startTrayItem.Checked;
            _store.Save();
        };
        menu.Items.Add(startTrayItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _isExitRequested = true;
            WpfApplication.Current.Shutdown();
        });
        menu.Items.Add(exitItem);

        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private void AddTrayLaunchItem(Forms.ToolStripMenuItem parent, ShortcutItem item)
    {
        var text = item.Name.Length > 36 ? item.Name[..36] + "..." : item.Name;
        var menuItem = new Forms.ToolStripMenuItem($"{item.FavoriteText} {item.IconText} {text}");
        menuItem.ToolTipText = item.TargetPath;
        menuItem.Click += (_, _) => Dispatcher.Invoke(() => LaunchItem(item));
        parent.DropDownItems.Add(menuItem);
    }
    private void SidebarAllButton_Click(object sender, RoutedEventArgs e)
    {
        FilterComboBox.SelectedItem = AllFilter;
        GroupFilterComboBox.SelectedItem = AllGroupsFilter;
        TagFilterComboBox.SelectedItem = AllTagsFilter;
        SortComboBox.SelectedItem = "手動順";
        SearchTextBox.Clear();
        ApplyFilter();
    }

    private void SidebarFavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        FilterComboBox.SelectedItem = FavoriteFilter;
        SortComboBox.SelectedItem = "お気に入り優先";
        ApplyFilter();
    }

    private void SidebarRecentButton_Click(object sender, RoutedEventArgs e)
    {
        FilterComboBox.SelectedItem = AllFilter;
        SortComboBox.SelectedItem = "最近使った順";
        ApplyFilter();
    }

    private void SidebarBrokenButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _store.Items)
        {
            item.RefreshStatus();
        }
        FilterComboBox.SelectedItem = BrokenFilter;
        ApplyFilter();
    }

    private void ManualBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _store.CreateManualBackup();
        ShowInfo($"バックアップを作成しました。\n\n{path}");
    }

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ShowWorkspaceManager();
    }

    private void ShowWorkspaceManager()
    {
        using var form = new Forms.Form
        {
            Text = "ワークスペース管理",
            Width = 620,
            Height = 430,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        var list = new Forms.ListBox { Left = 12, Top = 12, Width = 390, Height = 330 };
        var refresh = () =>
        {
            list.Items.Clear();
            foreach (var workspace in _store.Workspaces.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                list.Items.Add(workspace);
            }
            list.DisplayMember = nameof(WorkspaceItem.Name);
        };
        refresh();

        var run = new Forms.Button { Text = "開く", Left = 420, Top = 12, Width = 150, Height = 34 };
        var add = new Forms.Button { Text = "追加", Left = 420, Top = 56, Width = 150, Height = 34 };
        var edit = new Forms.Button { Text = "編集", Left = 420, Top = 100, Width = 150, Height = 34 };
        var delete = new Forms.Button { Text = "削除", Left = 420, Top = 144, Width = 150, Height = 34 };
        var close = new Forms.Button { Text = "閉じる", Left = 420, Top = 300, Width = 150, Height = 34 };

        run.Click += (_, _) =>
        {
            if (list.SelectedItem is WorkspaceItem workspace)
            {
                LaunchWorkspace(workspace);
            }
        };

        add.Click += (_, _) =>
        {
            var workspace = new WorkspaceItem();
            if (ShowWorkspaceEditDialog(workspace, isNew: true))
            {
                _store.AddWorkspace(workspace);
                refresh();
                RefreshTrayMenu();
            }
        };

        edit.Click += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceItem workspace) return;
            if (ShowWorkspaceEditDialog(workspace, isNew: false))
            {
                workspace.TouchUpdated();
                _store.Save();
                refresh();
                RefreshTrayMenu();
            }
        };

        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not WorkspaceItem workspace) return;
            if (Forms.MessageBox.Show($"「{workspace.Name}」を削除しますか？", "DHub", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) != Forms.DialogResult.Yes) return;
            _store.RemoveWorkspace(workspace);
            refresh();
            RefreshTrayMenu();
        };

        close.Click += (_, _) => form.Close();
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItem is WorkspaceItem workspace)
            {
                LaunchWorkspace(workspace);
            }
        };

        form.Controls.Add(list);
        form.Controls.Add(run);
        form.Controls.Add(add);
        form.Controls.Add(edit);
        form.Controls.Add(delete);
        form.Controls.Add(close);
        form.ShowDialog();
    }

    private bool ShowWorkspaceEditDialog(WorkspaceItem workspace, bool isNew)
    {
        using var form = new Forms.Form
        {
            Text = isNew ? "ワークスペース追加" : "ワークスペース編集",
            Width = 680,
            Height = 560,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        var nameLabel = new Forms.Label { Text = "名前", Left = 12, Top = 16, Width = 80 };
        var nameBox = new Forms.TextBox { Left = 100, Top = 12, Width = 520, Text = workspace.Name };
        var descLabel = new Forms.Label { Text = "説明", Left = 12, Top = 50, Width = 80 };
        var descBox = new Forms.TextBox { Left = 100, Top = 46, Width = 520, Text = workspace.Description };
        var delayLabel = new Forms.Label { Text = "待機秒", Left = 12, Top = 84, Width = 80 };
        var delayBox = new Forms.NumericUpDown { Left = 100, Top = 80, Width = 100, Minimum = 0, Maximum = 60, Value = workspace.DelaySeconds };
        var itemLabel = new Forms.Label { Text = "含めるショートカット", Left = 12, Top = 120, Width = 200 };
        var checkedList = new Forms.CheckedListBox { Left = 12, Top = 146, Width = 630, Height = 300, CheckOnClick = true };

        foreach (var item in _store.Items.OrderBy(x => x.GroupDisplay).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var index = checkedList.Items.Add(item);
            if (workspace.ShortcutIds.Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)))
            {
                checkedList.SetItemChecked(index, true);
            }
        }
        checkedList.DisplayMember = nameof(ShortcutItem.Name);

        var ok = new Forms.Button { Text = "保存", Left = 430, Top = 470, Width = 100, Height = 34, DialogResult = Forms.DialogResult.OK };
        var cancel = new Forms.Button { Text = "キャンセル", Left = 540, Top = 470, Width = 100, Height = 34, DialogResult = Forms.DialogResult.Cancel };
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        form.Controls.Add(nameLabel);
        form.Controls.Add(nameBox);
        form.Controls.Add(descLabel);
        form.Controls.Add(descBox);
        form.Controls.Add(delayLabel);
        form.Controls.Add(delayBox);
        form.Controls.Add(itemLabel);
        form.Controls.Add(checkedList);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);

        if (form.ShowDialog() != Forms.DialogResult.OK)
        {
            return false;
        }

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowInfo("ワークスペース名を入力してください。");
            return false;
        }

        var ids = checkedList.CheckedItems.OfType<ShortcutItem>().Select(x => x.Id).ToList();
        if (ids.Count == 0)
        {
            ShowInfo("ワークスペースに含めるショートカットを1件以上選択してください。");
            return false;
        }

        workspace.Name = name;
        workspace.Description = descBox.Text.Trim();
        workspace.DelaySeconds = (int)delayBox.Value;
        workspace.ShortcutIds = ids;
        if (isNew)
        {
            workspace.CreatedAt = DateTime.Now;
        }
        workspace.UpdatedAt = DateTime.Now;
        return true;
    }

    private async void LaunchWorkspace(WorkspaceItem workspace)
    {
        var items = _store.GetWorkspaceItems(workspace);
        if (items.Count == 0)
        {
            ShowInfo("このワークスペースに有効なショートカットがありません。");
            return;
        }

        foreach (var item in items)
        {
            LaunchItem(item);
            if (workspace.DelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(workspace.DelaySeconds));
            }
        }

        workspace.TouchLaunched();
        _store.Save();
        _store.AddLog("Workspace", "ワークスペースを起動", workspace.Name);
        RefreshTrayMenu();
    }

    private void ExportFullBackup()
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "DHub全データをエクスポート",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"DHub_FullBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        _store.ExportToFile(dialog.FileName);
        ShowInfo("全データをエクスポートしました。");
    }

    private void ExportCsv()
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "ショートカットをCSVエクスポート",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"DHub_Shortcuts_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        _store.ExportCsv(dialog.FileName);
        ShowInfo("CSVエクスポートしました。");
    }

    private void ImportCsv()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = "ショートカットをCSVインポート",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var replace = AskYesNo("現在の一覧を置き換えますか？\n\nはい: 置き換え\nいいえ: 重複を除いて追加") == true;
        var count = _store.ImportCsv(dialog.FileName, replace);
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
        ShowInfo($"{count} 件をCSVインポートしました。");
    }

    private IReadOnlyList<ShortcutItem> GetSelectedItems()
    {
        return ShortcutListView.SelectedItems
            .OfType<ShortcutItem>()
            .ToList();
    }

    private void SettingsScreenButton_Click(object sender, RoutedEventArgs e) => ShowSettingsScreen();

    private void RestoreWizardButton_Click(object sender, RoutedEventArgs e) => ShowRestoreWizard();

    private void BulkEditButton_Click(object sender, RoutedEventArgs e) => ShowBulkEditDialog();

    private void ShowSettingsScreen()
    {
        using var form = new Forms.Form
        {
            Text = "DHub 設定",
            Width = 560,
            Height = 520,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var tabs = new Forms.TabControl { Left = 12, Top = 12, Width = 520, Height = 400 };
        var general = new Forms.TabPage("一般");
        var backup = new Forms.TabPage("保存・復元");
        var data = new Forms.TabPage("入出力");

        var startMinimized = new Forms.CheckBox { Text = "起動時にトレイへ最小化", Left = 16, Top = 20, Width = 300, Checked = _store.Settings.StartMinimizedToTray };
        var checkUpdate = new Forms.CheckBox { Text = "起動時に更新確認", Left = 16, Top = 52, Width = 300, Checked = _store.Settings.CheckUpdateOnStartup };
        var hotkeys = new Forms.CheckBox { Text = "グローバルホットキーを有効化", Left = 16, Top = 84, Width = 300, Checked = _store.Settings.EnableGlobalHotkeys };
        var realIcons = new Forms.CheckBox { Text = "実ファイルのアイコン表示を有効化", Left = 16, Top = 116, Width = 300, Checked = _store.Settings.UseRealFileIcons };
        var showHome = new Forms.CheckBox { Text = "起動時にホームを表示", Left = 16, Top = 148, Width = 300, Checked = _store.Settings.ShowHomeOnStartup };
        var operationLog = new Forms.CheckBox { Text = "起動ログ・操作ログを保存", Left = 16, Top = 180, Width = 300, Checked = _store.Settings.EnableOperationLog };
        var separatedData = new Forms.CheckBox { Text = "設定・データを分離ファイルにも保存", Left = 16, Top = 212, Width = 360, Checked = _store.Settings.EnableSeparatedDataFiles };
        var updateLabel = new Forms.Label { Text = "更新確認URL", Left = 16, Top = 248, Width = 120 };
        var updateUrl = new Forms.TextBox { Left = 16, Top = 270, Width = 470, Text = _store.Settings.UpdateVersionUrl };
        general.Controls.AddRange(new Forms.Control[] { startMinimized, checkUpdate, hotkeys, realIcons, showHome, operationLog, separatedData, updateLabel, updateUrl });

        var autoBackup = new Forms.CheckBox { Text = "保存時に自動バックアップを作成", Left = 16, Top = 20, Width = 320, Checked = _store.Settings.EnableAutoBackup };
        var safeSave = new Forms.CheckBox { Text = "安全保存を有効化（一時ファイル→検証→置換）", Left = 16, Top = 52, Width = 420, Checked = _store.Settings.EnableSafeSave };
        var autoRecover = new Forms.CheckBox { Text = "起動時に設定破損を自動復旧", Left = 16, Top = 84, Width = 320, Checked = _store.Settings.AutoRecoverOnStartup };
        var showRestore = new Forms.CheckBox { Text = "自動復旧時にメッセージを表示", Left = 16, Top = 116, Width = 320, Checked = _store.Settings.ShowRestoreMessageAfterRecovery };
        var keepLabel = new Forms.Label { Text = "バックアップ保持件数", Left = 16, Top = 154, Width = 160 };
        var keepCount = new Forms.NumericUpDown { Left = 180, Top = 150, Width = 80, Minimum = 1, Maximum = 200, Value = Math.Max(1, _store.Settings.AutoBackupKeepCount) };
        var manualBackup = new Forms.Button { Text = "手動バックアップ作成", Left = 16, Top = 200, Width = 160, Height = 32 };
        var restore = new Forms.Button { Text = "バックアップから復元", Left = 190, Top = 200, Width = 170, Height = 32 };
        manualBackup.Click += (_, _) => ManualBackupButton_Click(this, new RoutedEventArgs());
        restore.Click += (_, _) => ShowRestoreWizard();
        backup.Controls.AddRange(new Forms.Control[] { autoBackup, safeSave, autoRecover, showRestore, keepLabel, keepCount, manualBackup, restore });

        var exportFull = new Forms.Button { Text = "全データをエクスポート", Left = 16, Top = 20, Width = 180, Height = 32 };
        var exportShortcut = new Forms.Button { Text = "ショートカットJSON出力", Left = 210, Top = 20, Width = 190, Height = 32 };
        var importShortcut = new Forms.Button { Text = "ショートカットJSON取込", Left = 16, Top = 64, Width = 180, Height = 32 };
        var exportCsvButton = new Forms.Button { Text = "CSVエクスポート", Left = 210, Top = 64, Width = 190, Height = 32 };
        var importCsvButton = new Forms.Button { Text = "CSVインポート", Left = 16, Top = 108, Width = 180, Height = 32 };
        var storage = new Forms.Button { Text = "データ保存場所を開く", Left = 210, Top = 108, Width = 190, Height = 32 };
        exportFull.Click += (_, _) => ExportFullBackup();
        exportShortcut.Click += (_, _) => ExportShortcuts();
        importShortcut.Click += (_, _) => ImportShortcuts();
        exportCsvButton.Click += (_, _) => ExportCsv();
        importCsvButton.Click += (_, _) => ImportCsv();
        storage.Click += (_, _) => OpenStorageFolder();
        data.Controls.AddRange(new Forms.Control[] { exportFull, exportShortcut, importShortcut, exportCsvButton, importCsvButton, storage });

        tabs.TabPages.Add(general);
        tabs.TabPages.Add(backup);
        tabs.TabPages.Add(data);

        var schemaLabel = new Forms.Label
        {
            Text = $"設定スキーマ: v{_store.Settings.SchemaVersion} / 保存先: {_store.StoragePath}",
            Left = 12,
            Top = 420,
            Width = 520,
            Height = 20
        };
        var ok = new Forms.Button { Text = "保存", Left = 320, Top = 448, Width = 90, Height = 32, DialogResult = Forms.DialogResult.OK };
        var cancel = new Forms.Button { Text = "閉じる", Left = 420, Top = 448, Width = 90, Height = 32, DialogResult = Forms.DialogResult.Cancel };
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Controls.Add(tabs);
        form.Controls.Add(schemaLabel);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);

        if (form.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        _store.Settings.StartMinimizedToTray = startMinimized.Checked;
        _store.Settings.CheckUpdateOnStartup = checkUpdate.Checked;
        _store.Settings.EnableGlobalHotkeys = hotkeys.Checked;
        _store.Settings.UseRealFileIcons = realIcons.Checked;
        _store.Settings.ShowHomeOnStartup = showHome.Checked;
        _store.Settings.EnableOperationLog = operationLog.Checked;
        _store.Settings.EnableSeparatedDataFiles = separatedData.Checked;
        FileIconService.IsEnabled = _store.Settings.UseRealFileIcons;
        _store.Settings.UpdateVersionUrl = updateUrl.Text.Trim();
        _store.Settings.EnableAutoBackup = autoBackup.Checked;
        _store.Settings.EnableSafeSave = safeSave.Checked;
        _store.Settings.AutoRecoverOnStartup = autoRecover.Checked;
        _store.Settings.ShowRestoreMessageAfterRecovery = showRestore.Checked;
        _store.Settings.AutoBackupKeepCount = (int)keepCount.Value;
        _store.SaveChanges();

        ApplyColumnVisibility();
        RefreshTrayMenu();
        ShowInfo("設定を保存しました。ホットキー設定はアプリ再起動後に反映されます。");
    }

    private void ShowRestoreWizard()
    {
        var backups = _store.GetBackupFiles().ToList();
        if (backups.Count == 0)
        {
            ShowInfo("復元できるバックアップがありません。");
            return;
        }

        using var form = new Forms.Form
        {
            Text = "バックアップから復元",
            Width = 760,
            Height = 460,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var label = new Forms.Label
        {
            Text = "復元するバックアップを選択してください。復元前に現在の状態も自動バックアップします。",
            Left = 12,
            Top = 12,
            Width = 700,
            Height = 24
        };

        var list = new Forms.ListView
        {
            Left = 12,
            Top = 44,
            Width = 720,
            Height = 320,
            View = Forms.View.Details,
            FullRowSelect = true,
            MultiSelect = false
        };
        list.Columns.Add("作成日時", 170);
        list.Columns.Add("種類", 120);
        list.Columns.Add("サイズ", 90);
        list.Columns.Add("パス", 330);

        foreach (var backup in backups)
        {
            var type = backup.Name.Contains("_manual_", StringComparison.OrdinalIgnoreCase) ? "手動" : "自動";
            var row = new Forms.ListViewItem(backup.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"));
            row.SubItems.Add(type);
            row.SubItems.Add($"{backup.Length / 1024.0:N1} KB");
            row.SubItems.Add(backup.FullName);
            row.Tag = backup.FullName;
            list.Items.Add(row);
        }

        if (list.Items.Count > 0) list.Items[0].Selected = true;

        var restore = new Forms.Button { Text = "復元", Left = 520, Top = 380, Width = 90, Height = 32 };
        var openFolder = new Forms.Button { Text = "フォルダを開く", Left = 12, Top = 380, Width = 120, Height = 32 };
        var cancel = new Forms.Button { Text = "閉じる", Left = 620, Top = 380, Width = 90, Height = 32 };

        openFolder.Click += (_, _) => OpenStorageFolder();
        cancel.Click += (_, _) => form.Close();
        restore.Click += (_, _) =>
        {
            if (list.SelectedItems.Count == 0 || list.SelectedItems[0].Tag is not string path)
            {
                Forms.MessageBox.Show("復元するバックアップを選択してください。", "DHub", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
                return;
            }

            if (Forms.MessageBox.Show("選択したバックアップで現在の設定を復元します。続行しますか？", "DHub", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) != Forms.DialogResult.Yes)
            {
                return;
            }

            try
            {
                _store.RestoreFromBackup(path);
                RefreshFilterItemsKeepSelection();
                ApplyFilter();
                RefreshTrayMenu();
                Forms.MessageBox.Show("バックアップから復元しました。", "DHub", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
                form.Close();
            }
            catch (Exception ex)
            {
                Forms.MessageBox.Show("復元に失敗しました。\n" + ex.Message, "DHub", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
            }
        };

        form.Controls.Add(label);
        form.Controls.Add(list);
        form.Controls.Add(openFolder);
        form.Controls.Add(restore);
        form.Controls.Add(cancel);
        form.ShowDialog();
    }

    private void ShowBulkEditDialog()
    {
        var selected = GetSelectedItems().ToList();
        if (selected.Count == 0)
        {
            ShowInfo("一括編集するショートカットを一覧から複数選択してください。\nCtrlキーまたはShiftキーで複数選択できます。");
            return;
        }

        using var form = new Forms.Form
        {
            Text = $"一括編集（対象 {selected.Count} 件）",
            Width = 760,
            Height = 500,
            StartPosition = Forms.FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
            BackColor = Drawing.Color.FromArgb(245, 247, 250),
            Font = new Drawing.Font("Yu Gothic UI", 10F),
            Padding = new Forms.Padding(0)
        };

        Forms.Panel CreateCard()
        {
            return new Forms.Panel
            {
                Dock = Forms.DockStyle.Top,
                Height = 68,
                BackColor = Drawing.Color.White,
                BorderStyle = Forms.BorderStyle.FixedSingle,
                Padding = new Forms.Padding(14, 10, 14, 10),
                Margin = new Forms.Padding(0, 0, 0, 10)
            };
        }

        Forms.Button CreateActionButton(string text, int width, Drawing.Color backColor, Drawing.Color foreColor)
        {
            return new Forms.Button
            {
                Text = text,
                Width = width,
                Height = 34,
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = Forms.FlatStyle.Flat,
                Margin = new Forms.Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = false
            };
        }

        var root = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Forms.Padding(18),
            BackColor = form.BackColor,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100F));
        for (var i = 0; i < 6; i++)
        {
            root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        }
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100F));

        var headerPanel = new Forms.Panel { Dock = Forms.DockStyle.Top, Height = 58, Margin = new Forms.Padding(0, 0, 0, 12) };
        var titleLabel = new Forms.Label
        {
            Text = "一括編集",
            AutoSize = true,
            Font = new Drawing.Font("Yu Gothic UI", 14F, Drawing.FontStyle.Bold),
            ForeColor = Drawing.Color.FromArgb(31, 41, 55),
            Left = 0,
            Top = 0
        };
        var targetLabel = new Forms.Label
        {
            Text = $"対象: 選択中の {selected.Count} 件",
            AutoSize = true,
            Font = new Drawing.Font("Yu Gothic UI", 10F),
            ForeColor = Drawing.Color.FromArgb(75, 85, 99),
            Left = 2,
            Top = 32
        };
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(targetLabel);

        var groupCard = CreateCard();
        var groupCheck = new Forms.CheckBox
        {
            Text = "グループを変更",
            Width = 130,
            Height = 28,
            Location = new Drawing.Point(0, 14),
            ForeColor = Drawing.Color.FromArgb(31, 41, 55)
        };
        var groupBox = new Forms.ComboBox
        {
            Left = 150,
            Top = 12,
            Width = 540,
            DropDownStyle = Forms.ComboBoxStyle.DropDown,
            Anchor = Forms.AnchorStyles.Left | Forms.AnchorStyles.Right | Forms.AnchorStyles.Top
        };
        groupBox.Items.AddRange(_store.GetGroups().Cast<object>().ToArray());
        groupCard.Controls.Add(groupCheck);
        groupCard.Controls.Add(groupBox);

        var tagCard = CreateCard();
        tagCard.Height = 78;
        var tagCheck = new Forms.CheckBox
        {
            Text = "タグを変更",
            Width = 130,
            Height = 28,
            Location = new Drawing.Point(0, 18),
            ForeColor = Drawing.Color.FromArgb(31, 41, 55)
        };
        var tagMode = new Forms.ComboBox
        {
            Left = 150,
            Top = 16,
            Width = 120,
            DropDownStyle = Forms.ComboBoxStyle.DropDownList
        };
        tagMode.Items.AddRange(new object[] { "置き換え", "追加", "削除" });
        tagMode.SelectedIndex = 0;
        var tagBox = new Forms.TextBox
        {
            Left = 286,
            Top = 16,
            Width = 404,
            Anchor = Forms.AnchorStyles.Left | Forms.AnchorStyles.Right | Forms.AnchorStyles.Top
        };
        var tagHint = new Forms.Label
        {
            Text = "タグはカンマ区切りで入力します。例: 開発, 仕事, 重要",
            AutoSize = true,
            Left = 150,
            Top = 46,
            ForeColor = Drawing.Color.FromArgb(107, 114, 128)
        };
        tagCard.Controls.Add(tagCheck);
        tagCard.Controls.Add(tagMode);
        tagCard.Controls.Add(tagBox);
        tagCard.Controls.Add(tagHint);

        var favoriteCard = CreateCard();
        var favoriteCheck = new Forms.CheckBox
        {
            Text = "お気に入りを変更",
            Width = 140,
            Height = 28,
            Location = new Drawing.Point(0, 14),
            ForeColor = Drawing.Color.FromArgb(31, 41, 55)
        };
        var favoriteMode = new Forms.ComboBox
        {
            Left = 150,
            Top = 12,
            Width = 220,
            DropDownStyle = Forms.ComboBoxStyle.DropDownList
        };
        favoriteMode.Items.AddRange(new object[] { "お気に入りにする", "お気に入り解除" });
        favoriteMode.SelectedIndex = 0;
        favoriteCard.Controls.Add(favoriteCheck);
        favoriteCard.Controls.Add(favoriteMode);

        var appCard = CreateCard();
        appCard.Height = 106;
        var appCheck = new Forms.CheckBox
        {
            Text = "開くアプリを変更",
            Width = 150,
            Height = 28,
            Location = new Drawing.Point(0, 14),
            ForeColor = Drawing.Color.FromArgb(31, 41, 55)
        };
        var appBox = new Forms.TextBox
        {
            Left = 150,
            Top = 12,
            Width = 390,
            Anchor = Forms.AnchorStyles.Left | Forms.AnchorStyles.Right | Forms.AnchorStyles.Top
        };
        var appBrowse = CreateActionButton("参照", 70, Drawing.Color.White, Drawing.Color.FromArgb(31, 41, 55));
        appBrowse.Left = 548;
        appBrowse.Top = 10;
        appBrowse.FlatAppearance.BorderColor = Drawing.Color.FromArgb(203, 213, 225);
        appBrowse.FlatAppearance.BorderSize = 1;
        var appClear = CreateActionButton("既定に戻す", 100, Drawing.Color.FromArgb(37, 99, 235), Drawing.Color.White);
        appClear.Left = 624;
        appClear.Top = 10;
        appClear.FlatAppearance.BorderSize = 0;
        var argLabel = new Forms.Label
        {
            Text = "アプリ引数",
            Width = 80,
            Left = 150,
            Top = 52,
            ForeColor = Drawing.Color.FromArgb(55, 65, 81)
        };
        var argBox = new Forms.TextBox
        {
            Left = 232,
            Top = 48,
            Width = 458,
            Anchor = Forms.AnchorStyles.Left | Forms.AnchorStyles.Right | Forms.AnchorStyles.Top
        };
        var appHint = new Forms.Label
        {
            Text = "未入力のときは種類に応じた既定アプリを使用します。",
            AutoSize = true,
            Left = 150,
            Top = 78,
            ForeColor = Drawing.Color.FromArgb(107, 114, 128)
        };
        appCard.Controls.Add(appCheck);
        appCard.Controls.Add(appBox);
        appCard.Controls.Add(appBrowse);
        appCard.Controls.Add(appClear);
        appCard.Controls.Add(argLabel);
        appCard.Controls.Add(argBox);
        appCard.Controls.Add(appHint);

        var notePanel = new Forms.Panel
        {
            Dock = Forms.DockStyle.Top,
            Height = 46,
            Margin = new Forms.Padding(0, 0, 0, 10)
        };
        var note = new Forms.Label
        {
            Text = "未チェックの項目は変更しません。変更したい項目だけチェックして適用してください。",
            AutoSize = false,
            Width = 700,
            Height = 36,
            Left = 0,
            Top = 4,
            ForeColor = Drawing.Color.FromArgb(75, 85, 99)
        };
        notePanel.Controls.Add(note);

        var buttonPanel = new Forms.FlowLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            FlowDirection = Forms.FlowDirection.RightToLeft,
            Padding = new Forms.Padding(0, 8, 0, 0)
        };
        var ok = CreateActionButton("適用", 110, Drawing.Color.FromArgb(16, 185, 129), Drawing.Color.White);
        ok.DialogResult = Forms.DialogResult.OK;
        ok.FlatAppearance.BorderSize = 0;
        var cancel = CreateActionButton("キャンセル", 110, Drawing.Color.White, Drawing.Color.FromArgb(31, 41, 55));
        cancel.DialogResult = Forms.DialogResult.Cancel;
        cancel.FlatAppearance.BorderColor = Drawing.Color.FromArgb(203, 213, 225);
        cancel.FlatAppearance.BorderSize = 1;
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        buttonPanel.Controls.Add(ok);
        buttonPanel.Controls.Add(cancel);

        appBrowse.Click += (_, _) =>
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Title = "開くアプリを選択",
                Filter = "実行ファイル (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                appBox.Text = dialog.FileName;
            }
        };

        appClear.Click += (_, _) =>
        {
            appBox.Text = string.Empty;
            argBox.Text = string.Empty;
        };

        root.Controls.Add(headerPanel, 0, 0);
        root.Controls.Add(groupCard, 0, 1);
        root.Controls.Add(tagCard, 0, 2);
        root.Controls.Add(favoriteCard, 0, 3);
        root.Controls.Add(appCard, 0, 4);
        root.Controls.Add(notePanel, 0, 5);
        root.Controls.Add(buttonPanel, 0, 6);
        form.Controls.Add(root);

        if (form.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        if (!groupCheck.Checked && !tagCheck.Checked && !favoriteCheck.Checked && !appCheck.Checked)
        {
            ShowInfo("変更対象が選択されていません。");
            return;
        }

        if (AskYesNo($"{selected.Count} 件のショートカットを一括編集します。続行しますか？") != true)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (groupCheck.Checked)
            {
                item.GroupName = groupBox.Text.Trim();
            }

            if (tagCheck.Checked)
            {
                var tags = ShortcutItem.SplitTags(tagBox.Text).ToList();
                if (tagMode.Text == "置き換え")
                {
                    item.Tags = string.Join(", ", tags);
                }
                else if (tagMode.Text == "追加")
                {
                    item.Tags = string.Join(", ", item.GetTagList().Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase));
                }
                else if (tagMode.Text == "削除")
                {
                    item.Tags = string.Join(", ", item.GetTagList().Where(x => !tags.Contains(x, StringComparer.OrdinalIgnoreCase)));
                }
            }

            if (favoriteCheck.Checked)
            {
                item.IsFavorite = favoriteMode.SelectedIndex == 0;
            }

            if (appCheck.Checked)
            {
                item.OpenApplicationPath = string.IsNullOrWhiteSpace(appBox.Text)
                    ? ShortcutRunner.GetDefaultOpenApplicationPath(item.ShortcutType)
                    : appBox.Text.Trim();
                item.OpenApplicationArguments = argBox.Text.Trim();
            }

            item.TouchUpdated();
        }

        _store.SaveChanges();
        RefreshFilterItemsKeepSelection();
        ApplyFilter();
        ShowInfo($"{selected.Count} 件を一括編集しました。");
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => ShowHomeDashboard();

    private void CommandPaletteButton_Click(object sender, RoutedEventArgs e) => ShowCommandPalette();

    private void CommandShortcutButton_Click(object sender, RoutedEventArgs e) => ShowCommandManager();

    private void OperationLogButton_Click(object sender, RoutedEventArgs e) => ShowOperationLogWindow();

    private void ShowHomeDashboard()
    {
        using var form = new Forms.Form
        {
            Text = "DHub ホーム - 今日の作業ダッシュボード",
            Width = 900,
            Height = 620,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var title = new Forms.Label
        {
            Text = "今日の作業ダッシュボード",
            Left = 16,
            Top = 14,
            Width = 500,
            Height = 28,
            Font = new Drawing.Font("Meiryo UI", 14, Drawing.FontStyle.Bold)
        };

        var summary = new Forms.Label
        {
            Text = $"お気に入り {_store.Items.Count(x => x.IsFavorite)} 件 / 最近使った {_store.Items.Count(x => x.LastLaunchedAt.HasValue)} 件 / ワークスペース {_store.Workspaces.Count} 件 / コマンド {_store.Commands.Count} 件 / 未検出 {_store.Items.Count(x => x.IsBroken)} 件",
            Left = 16,
            Top = 48,
            Width = 840,
            Height = 24
        };

        var favoriteList = CreateDashboardList("お気に入り", 16, 84);
        foreach (var item in _store.Items.Where(x => x.IsFavorite).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Take(12))
        {
            favoriteList.Items.Add(new DashboardEntry(item.Name, item.TargetPath, () => LaunchItem(item)));
        }

        var recentList = CreateDashboardList("最近使った", 306, 84);
        foreach (var item in _store.Items.Where(x => x.LastLaunchedAt.HasValue).OrderByDescending(x => x.LastLaunchedAt).Take(12))
        {
            recentList.Items.Add(new DashboardEntry(item.Name, item.LastLaunchedDisplay, () => LaunchItem(item)));
        }

        var workspaceList = CreateDashboardList("ワークスペース", 596, 84);
        foreach (var workspace in _store.Workspaces.OrderByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue).ThenBy(x => x.Name).Take(12))
        {
            workspaceList.Items.Add(new DashboardEntry(workspace.Name, workspace.Description, () => LaunchWorkspace(workspace)));
        }

        var commandList = CreateDashboardList("コマンド", 16, 346);
        foreach (var command in _store.Commands.OrderByDescending(x => x.IsFavorite).ThenByDescending(x => x.LastRunAt ?? DateTime.MinValue).ThenBy(x => x.Name).Take(12))
        {
            commandList.Items.Add(new DashboardEntry(command.Name, command.Command, () => RunCommand(command)));
        }

        var alertList = CreateDashboardList("警告・最近のログ", 306, 346);
        foreach (var item in _store.Items.Where(x => x.IsBroken).Take(6))
        {
            alertList.Items.Add(new DashboardEntry("未検出: " + item.Name, item.TargetPath, () => ShortcutListView.SelectedItem = item));
        }
        foreach (var log in _store.Logs.Take(6))
        {
            alertList.Items.Add(new DashboardEntry(log.Message, log.CreatedAtDisplay, () => { }));
        }

        var actionPanel = new Forms.GroupBox { Text = "すぐ使う", Left = 596, Top = 346, Width = 270, Height = 190 };
        var palette = new Forms.Button { Text = "統合検索 / コマンドパレット", Left = 18, Top = 28, Width = 220, Height = 32 };
        var add = new Forms.Button { Text = "ショートカット追加", Left = 18, Top = 68, Width = 220, Height = 32 };
        var commands = new Forms.Button { Text = "コマンド管理", Left = 18, Top = 108, Width = 220, Height = 32 };
        var logs = new Forms.Button { Text = "操作ログ", Left = 18, Top = 148, Width = 220, Height = 32 };
        palette.Click += (_, _) => ShowCommandPalette();
        add.Click += (_, _) => OpenAddDialog();
        commands.Click += (_, _) => ShowCommandManager();
        logs.Click += (_, _) => ShowOperationLogWindow();
        actionPanel.Controls.AddRange(new Forms.Control[] { palette, add, commands, logs });

        favoriteList.DoubleClick += (_, _) => ExecuteDashboardEntry(favoriteList);
        recentList.DoubleClick += (_, _) => ExecuteDashboardEntry(recentList);
        workspaceList.DoubleClick += (_, _) => ExecuteDashboardEntry(workspaceList);
        commandList.DoubleClick += (_, _) => ExecuteDashboardEntry(commandList);
        alertList.DoubleClick += (_, _) => ExecuteDashboardEntry(alertList);

        form.Controls.AddRange(new Forms.Control[] { title, summary, favoriteList, recentList, workspaceList, commandList, alertList, actionPanel });
        form.ShowDialog();
    }

    private static Forms.ListBox CreateDashboardList(string title, int left, int top)
    {
        var list = new Forms.ListBox
        {
            Left = left,
            Top = top,
            Width = 270,
            Height = 240,
            IntegralHeight = false
        };
        list.Items.Add(new DashboardEntry("【" + title + "】", "", () => { }));
        return list;
    }

    private static void ExecuteDashboardEntry(Forms.ListBox list)
    {
        if (list.SelectedItem is DashboardEntry entry)
        {
            entry.Execute();
        }
    }

    private sealed class DashboardEntry
    {
        private readonly Action _execute;
        public DashboardEntry(string title, string subtitle, Action execute)
        {
            Title = title;
            Subtitle = subtitle;
            _execute = execute;
        }
        public string Title { get; }
        public string Subtitle { get; }
        public void Execute() => _execute();
        public override string ToString() => string.IsNullOrWhiteSpace(Subtitle) ? Title : $"{Title} - {Subtitle}";
    }

    private List<UnifiedSearchResult> BuildUnifiedSearchResults()
    {
        var results = new List<UnifiedSearchResult>();

        foreach (var item in _store.Items)
        {
            results.Add(new UnifiedSearchResult
            {
                Kind = UnifiedSearchResultKind.Shortcut,
                Title = item.Name,
                Subtitle = item.TargetPath,
                SearchText = item.SearchText,
                Payload = item,
                ExecuteAction = () => LaunchItem(item)
            });
        }

        foreach (var workspace in _store.Workspaces)
        {
            results.Add(new UnifiedSearchResult
            {
                Kind = UnifiedSearchResultKind.Workspace,
                Title = workspace.Name,
                Subtitle = workspace.Description,
                SearchText = workspace.SearchText,
                Payload = workspace,
                ExecuteAction = () => LaunchWorkspace(workspace)
            });
        }

        foreach (var command in _store.Commands)
        {
            results.Add(new UnifiedSearchResult
            {
                Kind = UnifiedSearchResultKind.Command,
                Title = command.Name,
                Subtitle = command.Command + " " + command.Arguments,
                SearchText = command.SearchText,
                Payload = command,
                ExecuteAction = () => RunCommand(command)
            });
        }

        AddActionResult(results, "ショートカットを追加", "新しいショートカットを登録します", OpenAddDialog);
        AddActionResult(results, "コマンドを追加", "コマンドショートカットを登録します", () => ShowCommandEditDialog(new CommandItem(), true));
        AddActionResult(results, "ワークスペース管理", "複数ショートカットをまとめて開く設定", ShowWorkspaceManager);
        AddActionResult(results, "バックアップを作成", "現在のデータを手動バックアップします", () => ManualBackupButton_Click(this, new RoutedEventArgs()));
        AddActionResult(results, "リンク切れチェック", "存在しないファイル・フォルダを確認します", () => CheckLinksButton_Click(this, new RoutedEventArgs()));
        AddActionResult(results, "操作ログ", "起動ログ・操作ログを表示します", ShowOperationLogWindow);
        AddActionResult(results, "設定", "DHub設定画面を開きます", ShowSettingsScreen);
        AddActionResult(results, "データ保存場所を開く", _store.StorageFolder, OpenStorageFolder);

        foreach (var log in _store.Logs.Take(100))
        {
            results.Add(new UnifiedSearchResult
            {
                Kind = UnifiedSearchResultKind.Log,
                Title = log.Message,
                Subtitle = log.CreatedAtDisplay + " " + log.Detail,
                SearchText = $"{log.CreatedAtDisplay} {log.Level} {log.Category} {log.Message} {log.Detail}".ToLowerInvariant(),
                Payload = log,
                ExecuteAction = () => ShowInfo(log.DisplayText + "\n" + log.Detail)
            });
        }

        return results;
    }

    private static void AddActionResult(List<UnifiedSearchResult> results, string title, string subtitle, Action action)
    {
        results.Add(new UnifiedSearchResult
        {
            Kind = UnifiedSearchResultKind.Action,
            Title = title,
            Subtitle = subtitle,
            SearchText = (title + " " + subtitle).ToLowerInvariant(),
            ExecuteAction = action
        });
    }

    private void ShowCommandPalette()
    {
        using var form = new Forms.Form
        {
            Text = "DHub 統合検索 / コマンドパレット",
            Width = 760,
            Height = 560,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            KeyPreview = true
        };

        var search = new Forms.TextBox { Left = 12, Top = 12, Width = 720, Height = 28 };
        var list = new Forms.ListBox { Left = 12, Top = 48, Width = 720, Height = 420, IntegralHeight = false };
        var hint = new Forms.Label { Left = 12, Top = 478, Width = 720, Height = 24, Text = "Enter: 実行 / Esc: 閉じる / 検索対象: ショートカット・ワークスペース・コマンド・操作・メモ・ログ" };
        var allResults = BuildUnifiedSearchResults();

        void Refresh()
        {
            var words = search.Text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            list.Items.Clear();
            IEnumerable<UnifiedSearchResult> filtered = allResults;
            if (words.Length > 0)
            {
                filtered = filtered.Where(x => words.All(w => (x.SearchText + " " + x.Title + " " + x.Subtitle).ToLowerInvariant().Contains(w)));
            }

            foreach (var result in filtered.OrderBy(x => x.Kind).ThenBy(x => x.Title).Take(200))
            {
                list.Items.Add(result);
            }

            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        void ExecuteSelected()
        {
            if (list.SelectedItem is not UnifiedSearchResult result) return;
            form.Close();
            result.ExecuteAction?.Invoke();
        }

        search.TextChanged += (_, _) => Refresh();
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Forms.Keys.Enter)
            {
                ExecuteSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Forms.Keys.Down && list.Items.Count > 0)
            {
                list.Focus();
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                e.Handled = true;
            }
        };
        list.DoubleClick += (_, _) => ExecuteSelected();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Forms.Keys.Enter)
            {
                ExecuteSelected();
                e.Handled = true;
            }
        };
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Forms.Keys.Escape) form.Close();
        };

        form.Controls.Add(search);
        form.Controls.Add(list);
        form.Controls.Add(hint);
        Refresh();
        form.Shown += (_, _) => search.Focus();
        form.ShowDialog();
    }

    private void ShowCommandManager()
    {
        using var form = new Forms.Form
        {
            Text = "コマンドショートカット管理",
            Width = 760,
            Height = 500,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        var list = new Forms.ListBox { Left = 12, Top = 12, Width = 520, Height = 390, IntegralHeight = false };
        void Refresh()
        {
            list.Items.Clear();
            foreach (var command in _store.Commands.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.GroupDisplay).ThenBy(x => x.Name))
            {
                list.Items.Add(command);
            }
            list.DisplayMember = nameof(CommandItem.Name);
        }

        var run = new Forms.Button { Text = "実行", Left = 550, Top = 12, Width = 150, Height = 34 };
        var add = new Forms.Button { Text = "追加", Left = 550, Top = 56, Width = 150, Height = 34 };
        var edit = new Forms.Button { Text = "編集", Left = 550, Top = 100, Width = 150, Height = 34 };
        var memo = new Forms.Button { Text = "メモ", Left = 550, Top = 144, Width = 150, Height = 34 };
        var delete = new Forms.Button { Text = "削除", Left = 550, Top = 188, Width = 150, Height = 34 };
        var close = new Forms.Button { Text = "閉じる", Left = 550, Top = 368, Width = 150, Height = 34 };

        run.Click += (_, _) => { if (list.SelectedItem is CommandItem c) RunCommand(c); };
        add.Click += (_, _) => { if (ShowCommandEditDialog(new CommandItem(), true)) Refresh(); };
        edit.Click += (_, _) => { if (list.SelectedItem is CommandItem c && ShowCommandEditDialog(c, false)) Refresh(); };
        memo.Click += (_, _) => { if (list.SelectedItem is CommandItem c && EditMemo("コマンドメモ", c.Memo, out var m)) { c.Memo = m; c.TouchUpdated(); _store.SaveChanges(); Refresh(); } };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItem is not CommandItem c) return;
            if (Forms.MessageBox.Show($"「{c.Name}」を削除しますか？", "DHub", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) != Forms.DialogResult.Yes) return;
            _store.RemoveCommand(c);
            _store.AddLog("Command", "コマンドを削除", c.Name);
            Refresh();
        };
        close.Click += (_, _) => form.Close();
        list.DoubleClick += (_, _) => { if (list.SelectedItem is CommandItem c) RunCommand(c); };

        form.Controls.AddRange(new Forms.Control[] { list, run, add, edit, memo, delete, close });
        Refresh();
        form.ShowDialog();
    }

    private bool ShowCommandEditDialog(CommandItem command, bool isNew)
    {
        using var form = new Forms.Form
        {
            Text = isNew ? "コマンド追加" : "コマンド編集",
            Width = 640,
            Height = 520,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };

        var nameLabel = new Forms.Label { Text = "名前", Left = 16, Top = 18, Width = 100 };
        var nameBox = new Forms.TextBox { Left = 130, Top = 14, Width = 440, Text = command.Name };
        var commandLabel = new Forms.Label { Text = "コマンド", Left = 16, Top = 58, Width = 100 };
        var commandBox = new Forms.TextBox { Left = 130, Top = 54, Width = 360, Text = command.Command };
        var browse = new Forms.Button { Text = "参照", Left = 500, Top = 52, Width = 70 };
        var argsLabel = new Forms.Label { Text = "引数", Left = 16, Top = 98, Width = 100 };
        var argsBox = new Forms.TextBox { Left = 130, Top = 94, Width = 440, Text = command.Arguments };
        var workLabel = new Forms.Label { Text = "作業フォルダ", Left = 16, Top = 138, Width = 100 };
        var workBox = new Forms.TextBox { Left = 130, Top = 134, Width = 360, Text = command.WorkingDirectory };
        var workBrowse = new Forms.Button { Text = "参照", Left = 500, Top = 132, Width = 70 };
        var groupLabel = new Forms.Label { Text = "グループ", Left = 16, Top = 178, Width = 100 };
        var groupBox = new Forms.ComboBox { Left = 130, Top = 174, Width = 180, DropDownStyle = Forms.ComboBoxStyle.DropDown, Text = command.GroupName };
        groupBox.Items.AddRange(_store.GetGroups().Cast<object>().ToArray());
        var tagsLabel = new Forms.Label { Text = "タグ", Left = 330, Top = 178, Width = 50 };
        var tagsBox = new Forms.TextBox { Left = 380, Top = 174, Width = 190, Text = command.Tags };
        var favorite = new Forms.CheckBox { Text = "お気に入り", Left = 130, Top = 214, Width = 120, Checked = command.IsFavorite };
        var confirm = new Forms.CheckBox { Text = "実行前に確認", Left = 260, Top = 214, Width = 130, Checked = command.ConfirmBeforeRun };
        var admin = new Forms.CheckBox { Text = "管理者として実行", Left = 400, Top = 214, Width = 160, Checked = command.RunAsAdministrator };
        var memoLabel = new Forms.Label { Text = "メモ", Left = 16, Top = 254, Width = 100 };
        var memoBox = new Forms.TextBox { Left = 130, Top = 250, Width = 440, Height = 130, Multiline = true, ScrollBars = Forms.ScrollBars.Vertical, Text = command.Memo };
        var ok = new Forms.Button { Text = "保存", Left = 390, Top = 410, Width = 85, Height = 32, DialogResult = Forms.DialogResult.OK };
        var cancel = new Forms.Button { Text = "キャンセル", Left = 485, Top = 410, Width = 85, Height = 32, DialogResult = Forms.DialogResult.Cancel };

        browse.Click += (_, _) =>
        {
            using var dialog = new Forms.OpenFileDialog { Title = "コマンドを選択", Filter = "実行ファイル・バッチ (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|All files (*.*)|*.*" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                commandBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                if (string.IsNullOrWhiteSpace(workBox.Text)) workBox.Text = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        };
        workBrowse.Click += (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "作業フォルダを選択" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) workBox.Text = dialog.SelectedPath;
        };

        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Controls.AddRange(new Forms.Control[] { nameLabel, nameBox, commandLabel, commandBox, browse, argsLabel, argsBox, workLabel, workBox, workBrowse, groupLabel, groupBox, tagsLabel, tagsBox, favorite, confirm, admin, memoLabel, memoBox, ok, cancel });

        if (form.ShowDialog() != Forms.DialogResult.OK) return false;
        if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(commandBox.Text))
        {
            ShowInfo("名前とコマンドを入力してください。");
            return false;
        }

        command.Name = nameBox.Text.Trim();
        command.Command = commandBox.Text.Trim();
        command.Arguments = argsBox.Text.Trim();
        command.WorkingDirectory = workBox.Text.Trim();
        command.GroupName = groupBox.Text.Trim();
        command.Tags = tagsBox.Text.Trim();
        command.IsFavorite = favorite.Checked;
        command.ConfirmBeforeRun = confirm.Checked;
        command.RunAsAdministrator = admin.Checked;
        command.Memo = memoBox.Text;
        command.TouchUpdated();
        if (isNew)
        {
            command.CreatedAt = DateTime.Now;
            _store.AddCommand(command);
            _store.AddLog("Command", "コマンドを追加", command.Name);
        }
        else
        {
            _store.SaveChanges();
            _store.AddLog("Command", "コマンドを編集", command.Name);
        }
        RefreshTrayMenu();
        return true;
    }

    private void RunCommand(CommandItem command)
    {
        try
        {
            if (command.ConfirmBeforeRun && AskYesNo($"コマンドを実行しますか？\n\n{command.Name}\n{command.Command} {command.Arguments}") != true)
            {
                return;
            }

            ShortcutRunner.RunCommand(command);
            command.TouchRun();
            _store.SaveChanges();
            _store.AddLog("Command", "コマンドを実行", command.Name + " / " + command.Command);
            RefreshTrayMenu();
        }
        catch (Exception ex)
        {
            _store.AddLog("Command", "コマンド実行失敗", command.Name + " / " + ex.Message, "Error");
            System.Windows.MessageBox.Show("コマンドを実行できませんでした。\n" + ex.Message, "DHub", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void ShowOperationLogWindow()
    {
        using var form = new Forms.Form
        {
            Text = "DHub 操作ログ",
            Width = 900,
            Height = 560,
            StartPosition = Forms.FormStartPosition.CenterScreen
        };
        var list = new Forms.ListView { Left = 12, Top = 12, Width = 850, Height = 430, View = Forms.View.Details, FullRowSelect = true };
        list.Columns.Add("日時", 150);
        list.Columns.Add("レベル", 70);
        list.Columns.Add("カテゴリ", 110);
        list.Columns.Add("内容", 240);
        list.Columns.Add("詳細", 260);

        void Refresh()
        {
            list.Items.Clear();
            foreach (var log in _store.Logs.Take(1000))
            {
                var row = new Forms.ListViewItem(log.CreatedAtDisplay);
                row.SubItems.Add(log.Level);
                row.SubItems.Add(log.Category);
                row.SubItems.Add(log.Message);
                row.SubItems.Add(log.Detail);
                list.Items.Add(row);
            }
        }

        var export = new Forms.Button { Text = "CSV出力", Left = 12, Top = 458, Width = 100, Height = 32 };
        var clear = new Forms.Button { Text = "クリア", Left = 122, Top = 458, Width = 100, Height = 32 };
        var close = new Forms.Button { Text = "閉じる", Left = 762, Top = 458, Width = 100, Height = 32 };
        export.Click += (_, _) => ExportLogsCsv();
        clear.Click += (_, _) =>
        {
            if (Forms.MessageBox.Show("ログをすべて削除しますか？", "DHub", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question) != Forms.DialogResult.Yes) return;
            _store.ClearLogs();
            Refresh();
        };
        close.Click += (_, _) => form.Close();
        form.Controls.AddRange(new Forms.Control[] { list, export, clear, close });
        Refresh();
        form.ShowDialog();
    }

    private void ExportLogsCsv()
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "操作ログをCSV出力",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"DHub_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var lines = new List<string> { "CreatedAt,Level,Category,Message,Detail" };
        lines.AddRange(_store.Logs.Select(x => string.Join(',', new[] { CsvForLog(x.CreatedAtDisplay), CsvForLog(x.Level), CsvForLog(x.Category), CsvForLog(x.Message), CsvForLog(x.Detail) })));
        System.IO.File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
        ShowInfo("操作ログをCSV出力しました。");
    }

    private static string CsvForLog(string? value)
    {
        var v = value ?? string.Empty;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private bool EditMemo(string title, string currentMemo, out string memo)
    {
        using var form = new Forms.Form
        {
            Text = title,
            Width = 640,
            Height = 440,
            StartPosition = Forms.FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var box = new Forms.TextBox { Left = 12, Top = 12, Width = 600, Height = 330, Multiline = true, ScrollBars = Forms.ScrollBars.Vertical, Text = currentMemo };
        var ok = new Forms.Button { Text = "保存", Left = 420, Top = 360, Width = 90, Height = 32, DialogResult = Forms.DialogResult.OK };
        var cancel = new Forms.Button { Text = "キャンセル", Left = 520, Top = 360, Width = 90, Height = 32, DialogResult = Forms.DialogResult.Cancel };
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        form.Controls.Add(box);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        var result = form.ShowDialog() == Forms.DialogResult.OK;
        memo = result ? box.Text : currentMemo;
        return result;
    }

    private async Task CheckUpdateAsync(bool silent)
    {
        try
        {
            var latest = await _updateService.CheckAsync(_store.Settings.UpdateVersionUrl);
            if (latest is null)
            {
                if (!silent) ShowInfo("更新URLが未設定です。設定ファイルの UpdateVersionUrl をGitHubのversion.jsonに変更してください。");
                return;
            }

            if (!_updateService.IsNewer(latest.Version))
            {
                if (!silent) ShowInfo("最新バージョンです。");
                return;
            }

            if (string.IsNullOrWhiteSpace(latest.DownloadUrl))
            {
                if (!silent) ShowInfo("version.json の downloadUrl が未設定です。");
                return;
            }

            if (!silent)
            {
                var message = $"新しいバージョンがあります。\n\n現在: {_updateService.CurrentVersion}\n最新: {latest.Version}\n\n更新しますか？";
                if (AskYesNo(message) != true) return;
            }

            var downloadedZip = await _updateService.DownloadAppZipAsync(latest.DownloadUrl);
            var updater = await _updateService.DownloadAndExtractUpdaterAsync(latest.UpdaterUrl);
            _updateService.ReplaceCurrentExeAndRestartFromZip(downloadedZip, updater);
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                System.Windows.MessageBox.Show(
                    "更新確認に失敗しました。\n" + ex.Message,
                    "DHub",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }

    private bool? AskYesNo(string message)
    {
        return System.Windows.MessageBox.Show(
            message,
            "DHub",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
    }

    private static void ShowInfo(string message)
    {
        System.Windows.MessageBox.Show(
            message,
            "DHub",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}
