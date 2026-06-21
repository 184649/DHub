using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ShortcutList.Models;

namespace ShortcutList.Views;

public partial class QuickSearchWindow : Window
{
    private readonly ObservableCollection<ShortcutItem> _results = new();
    private IReadOnlyList<ShortcutItem> _source = Array.Empty<ShortcutItem>();

    public event EventHandler<ShortcutItem>? LaunchRequested;

    public QuickSearchWindow()
    {
        InitializeComponent();
        ResultListView.ItemsSource = _results;
    }

    public void ShowSearch(IReadOnlyList<ShortcutItem> source)
    {
        _source = source;
        SearchTextBox.Text = string.Empty;
        RefreshResults();

        Show();
        Activate();
        SearchTextBox.Focus();
        Keyboard.Focus(SearchTextBox);
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshResults();
    }

    private void RefreshResults()
    {
        var words = SearchTextBox.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        var items = _source.AsEnumerable();

        if (words.Count > 0)
        {
            items = items.Where(x => words.All(word => x.SearchText.Contains(word)));
        }

        _results.Clear();

        foreach (var item in items.OrderByDescending(x => x.IsFavorite)
                                  .ThenByDescending(x => x.LastLaunchedAt ?? DateTime.MinValue)
                                  .ThenBy(x => x.Name)
                                  .Take(20))
        {
            _results.Add(item);
        }

        if (_results.Count > 0)
        {
            ResultListView.SelectedIndex = 0;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && ResultListView.Items.Count > 0)
        {
            ResultListView.SelectedIndex = Math.Min(ResultListView.SelectedIndex + 1, ResultListView.Items.Count - 1);
            ResultListView.ScrollIntoView(ResultListView.SelectedItem);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && ResultListView.Items.Count > 0)
        {
            ResultListView.SelectedIndex = Math.Max(ResultListView.SelectedIndex - 1, 0);
            ResultListView.ScrollIntoView(ResultListView.SelectedItem);
            e.Handled = true;
        }
    }

    private void ResultListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        LaunchSelected();
    }

    private void LaunchSelected()
    {
        if (ResultListView.SelectedItem is not ShortcutItem item)
        {
            return;
        }

        Hide();
        LaunchRequested?.Invoke(this, item);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Hide();
    }
}
