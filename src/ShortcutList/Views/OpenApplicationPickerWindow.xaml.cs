using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using ShortcutList.Models;
using ShortcutList.Services;

namespace ShortcutList.Views;

public partial class OpenApplicationPickerWindow : Window
{
    private const string AllCategory = "すべて";

    private readonly ShortcutType _shortcutType;
    private readonly string _targetPath;
    private readonly List<OpenApplicationCandidate> _allCandidates;
    private readonly ObservableCollection<OpenApplicationCandidate> _visibleCandidates = new();
    private bool _updatingCategory;

    public string ResultApplicationPath { get; private set; } = string.Empty;
    public string ResultApplicationArguments { get; private set; } = string.Empty;

    public OpenApplicationPickerWindow(
        ShortcutType shortcutType,
        string? currentApplicationPath,
        string? currentApplicationArguments,
        string? targetPath = null)
    {
        InitializeComponent();

        _shortcutType = shortcutType;
        _targetPath = targetPath ?? string.Empty;
        _allCandidates = OpenApplicationDiscovery
            .GetCandidates(shortcutType, currentApplicationPath, _targetPath)
            .ToList();

        CandidateListView.ItemsSource = _visibleCandidates;
        ArgumentsTextBox.Text = currentApplicationArguments ?? string.Empty;

        InitializeCategoryFilter();
        ApplyFilter();
        SelectCurrentOrDefault(currentApplicationPath);

        SearchTextBox.Focus();
        Keyboard.Focus(SearchTextBox);
    }

    private OpenApplicationCandidate? SelectedCandidate => CandidateListView.SelectedItem as OpenApplicationCandidate;

    private void InitializeCategoryFilter()
    {
        _updatingCategory = true;
        CategoryComboBox.Items.Clear();
        CategoryComboBox.Items.Add(AllCategory);

        foreach (var category in _allCandidates.Select(x => x.Category).Distinct().OrderBy(x => x))
        {
            CategoryComboBox.Items.Add(category);
        }

        CategoryComboBox.SelectedItem = AllCategory;
        _updatingCategory = false;
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCategory)
        {
            return;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selectedBeforeFilter = SelectedCandidate;
        var keyword = SearchTextBox.Text.Trim().ToLowerInvariant();
        var category = CategoryComboBox.SelectedItem?.ToString() ?? AllCategory;
        var words = keyword
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var items = _allCandidates.AsEnumerable();

        if (!string.Equals(category, AllCategory, StringComparison.OrdinalIgnoreCase))
        {
            items = items.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (words.Count > 0)
        {
            items = items.Where(x => words.All(word => x.SearchText.Contains(word)));
        }

        _visibleCandidates.Clear();
        foreach (var item in items)
        {
            _visibleCandidates.Add(item);
        }

        if (selectedBeforeFilter is not null)
        {
            var selected = _visibleCandidates.FirstOrDefault(x =>
                string.Equals(x.ApplicationPath, selectedBeforeFilter.ApplicationPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Name, selectedBeforeFilter.Name, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                CandidateListView.SelectedItem = selected;
                CandidateListView.ScrollIntoView(selected);
                RefreshSelectedPath();
                return;
            }
        }

        if (_visibleCandidates.Count > 0)
        {
            CandidateListView.SelectedItem = _visibleCandidates[0];
            CandidateListView.ScrollIntoView(_visibleCandidates[0]);
        }
        else
        {
            CandidateListView.SelectedItem = null;
        }

        RefreshSelectedPath();
    }

    private void SelectCurrentOrDefault(string? currentApplicationPath)
    {
        OpenApplicationCandidate? selected = null;

        if (!string.IsNullOrWhiteSpace(currentApplicationPath))
        {
            selected = _visibleCandidates.FirstOrDefault(x => string.Equals(x.ApplicationPath, currentApplicationPath.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        selected ??= _visibleCandidates.FirstOrDefault(x => x.IsDefault);
        selected ??= _visibleCandidates.FirstOrDefault();

        if (selected is not null)
        {
            CandidateListView.SelectedItem = selected;
            CandidateListView.ScrollIntoView(selected);
        }

        RefreshSelectedPath();
    }

    private void CandidateListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSelectedPath();
    }

    private void CandidateListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        CommitSelection();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = "開くアプリを直接選択",
            Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var candidate = new OpenApplicationCandidate(
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            dialog.FileName,
            "直接参照",
            "直接参照で選択したアプリです。");

        _allCandidates.RemoveAll(x =>
            string.Equals(x.ApplicationPath, candidate.ApplicationPath, StringComparison.OrdinalIgnoreCase));
        _allCandidates.Insert(0, candidate);

        InitializeCategoryFilter();
        SearchTextBox.Text = string.Empty;
        ApplyFilter();
        CandidateListView.SelectedItem = _visibleCandidates.FirstOrDefault(x =>
            string.Equals(x.ApplicationPath, candidate.ApplicationPath, StringComparison.OrdinalIgnoreCase));
        RefreshSelectedPath();
    }

    private void UseDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        ResultApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(_shortcutType);
        ResultApplicationArguments = string.Empty;
        DialogResult = true;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void CommitSelection()
    {
        if (SelectedCandidate is null)
        {
            System.Windows.MessageBox.Show(
                "使用するアプリを選択してください。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        ResultApplicationPath = SelectedCandidate.ApplicationPath;
        ResultApplicationArguments = ArgumentsTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void RefreshSelectedPath()
    {
        SelectedPathTextBlock.Text = SelectedCandidate?.DisplayPath ?? "未選択";
    }
}
