using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ShortcutList.Models;
using ShortcutList.Services;
using Controls = System.Windows.Controls;

namespace ShortcutList.Views;

public sealed record QuickAddRequest(IReadOnlyList<string> Targets);

public partial class QuickSearchWindow : Window
{
    private readonly ObservableCollection<QuickLaunchEntry> _results = new();
    private IReadOnlyList<QuickLaunchEntry> _source = Array.Empty<QuickLaunchEntry>();
    private string _sourceStatus = string.Empty;
    private int _limit = 100;
    private bool _updating;
    private bool _adding;

    public event EventHandler<QuickLaunchEntry>? LaunchRequested;
    public event EventHandler<QuickAddRequest>? AddRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? WorkspaceRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ManagementRequested;
    public event EventHandler? ExitRequested;

    public QuickSearchWindow()
    {
        InitializeComponent();
        ResultListView.ItemsSource = _results;
    }

    public void ShowSearch(IReadOnlyList<QuickLaunchEntry> source, string status = "")
    {
        _updating = true;
        _source = source;
        _sourceStatus = status;
        SearchTextBox.Text = string.Empty;
        AddPanel.Visibility = Visibility.Collapsed;
        _limit = 100;
        _updating = false;
        RefreshResults();
        Show();
        Activate();
        SearchTextBox.Focus();
        Keyboard.Focus(SearchTextBox);
    }

    public void UpdateSource(IReadOnlyList<QuickLaunchEntry> source, string status)
    {
        var selected = ResultListView.SelectedItem as QuickLaunchEntry;
        _source = source;
        _sourceStatus = status;
        // Background refresh must not silently switch the target underneath Enter.
        RefreshResults(selected, preserveSelection: true);
    }

    public void SetAddStatus(bool busy, string message)
    {
        _adding = busy;
        SaveTargetButton.IsEnabled = !busy;
        _sourceStatus = message;
        RefreshResults(ResultListView.SelectedItem as QuickLaunchEntry, preserveSelection: true);
        if (!busy && IsVisible && AddPanel.Visibility == Visibility.Visible) AddTargetTextBox.Focus();
    }

    public void FinishAdd()
    {
        _adding = false;
        SaveTargetButton.IsEnabled = true;
    }

    private void SearchTextBox_TextChanged(object sender, Controls.TextChangedEventArgs e)
    {
        if (_updating || ResultListView is null) return;
        _limit = 100;
        RefreshResults();
    }

    private void RefreshResults(QuickLaunchEntry? selected = null, bool preserveSelection = false)
    {
        var results = QuickLaunchSearch.Query(_source, SearchTextBox.Text, _limit);
        _results.Clear();
        foreach (var item in results.Items) _results.Add(item);
        if (preserveSelection)
            ResultListView.SelectedItem = selected is null ? null : _results.FirstOrDefault(x => x.Key == selected.Key && x.RecipeToken == selected.RecipeToken);
        else if (_results.Count > 0) ResultListView.SelectedIndex = 0;
        SearchHint.Visibility = SearchTextBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _source.Count == 0
            ? "ここへファイルやフォルダーをドロップするか、下の「追加」から登録できます。"
            : "見つかりません。短い名前で検索するか、再読込してください。";
        MoreButton.Visibility = results.TotalCount > _results.Count ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"{_results.Count} / {results.TotalCount} 件" + (string.IsNullOrWhiteSpace(_sourceStatus) ? "" : " · " + _sourceStatus);
        StatusText.ToolTip = StatusText.Text;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (AddPanel.Visibility == Visibility.Visible) { AddPanel.Visibility = Visibility.Collapsed; SearchTextBox.Focus(); }
            else Hide();
            e.Handled = true;
            return;
        }
        if (AddTargetTextBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter) { RequestAdd(new[] { AddTargetTextBox.Text }); e.Handled = true; }
            return;
        }
        if (Keyboard.FocusedElement is Controls.Button { DataContext: QuickLaunchEntry focusedEntry })
        {
            if (e.Key == Key.Enter) { LaunchEntry(focusedEntry); e.Handled = true; }
            return;
        }
        if (!SearchTextBox.IsKeyboardFocusWithin && !ResultListView.IsKeyboardFocusWithin) return;
        if (e.Key == Key.Enter) { LaunchSelected(); e.Handled = true; return; }
        if ((e.Key == Key.Down || e.Key == Key.Up) && ResultListView.Items.Count > 0)
        {
            var next = ResultListView.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            ResultListView.SelectedIndex = Math.Clamp(next, 0, ResultListView.Items.Count - 1);
            ResultListView.ScrollIntoView(ResultListView.SelectedItem);
            e.Handled = true;
        }
    }

    private void ResultListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin &&
            Controls.ItemsControl.ContainerFromElement(ResultListView, origin) is Controls.ListViewItem row &&
            row.Content is QuickLaunchEntry entry) LaunchEntry(entry);
    }
    private void OpenEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Controls.Button { DataContext: QuickLaunchEntry entry }) LaunchEntry(entry);
        e.Handled = true;
    }
    private void LaunchSelected() { if (ResultListView.SelectedItem is QuickLaunchEntry entry) LaunchEntry(entry); }
    private void LaunchEntry(QuickLaunchEntry entry)
    {
        if (!IsVisible) return;
        if (!entry.CanLaunch) { _sourceStatus = "この項目はまだ開けません。再読込するか、元フォルダーから確認してください。"; RefreshResults(entry, true); return; }
        Hide();
        LaunchRequested?.Invoke(this, entry);
    }
    private void RequestAdd(IReadOnlyList<string> targets)
    {
        if (_adding || targets.Count == 0) return;
        AddRequested?.Invoke(this, new QuickAddRequest(targets));
    }
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !_adding && (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) || e.Data.GetDataPresent(System.Windows.DataFormats.Text)) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) RequestAdd(files);
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text) && e.Data.GetData(System.Windows.DataFormats.Text) is string text) RequestAdd(new[] { text });
        e.Handled = true;
    }
    private void AddButton_Click(object sender, RoutedEventArgs e) { AddPanel.Visibility = Visibility.Visible; AddTargetTextBox.Focus(); }
    private void SaveTargetButton_Click(object sender, RoutedEventArgs e) => RequestAdd(new[] { AddTargetTextBox.Text });
    private void MoreButton_Click(object sender, RoutedEventArgs e) { _limit += 100; RefreshResults(ResultListView.SelectedItem as QuickLaunchEntry, true); }
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void WorkspaceButton_Click(object sender, RoutedEventArgs e) { Hide(); WorkspaceRequested?.Invoke(this, EventArgs.Empty); }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) { Hide(); SettingsRequested?.Invoke(this, EventArgs.Empty); }
    private void ManagementButton_Click(object sender, RoutedEventArgs e) { Hide(); ManagementRequested?.Invoke(this, EventArgs.Empty); }
    private void ExitButton_Click(object sender, RoutedEventArgs e) { Hide(); ExitRequested?.Invoke(this, EventArgs.Empty); }
    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Explicit Add mode keeps a drop target available while Explorer has focus.
        // Normal launching still dismisses immediately; Esc leaves Add mode.
        if (AddPanel.Visibility != Visibility.Visible) Hide();
    }
}
