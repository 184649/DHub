using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using ShortcutList.Models;
using ShortcutList.Services;

namespace ShortcutList.Views;

public partial class ShortcutEditWindow : Window
{
    private bool _nameEditedByUser;
    private bool _openApplicationEditedByUser;
    private bool _updatingOpenApplicationText;
    private bool _updatingQuickOpenApplicationComboBox;
    private System.Windows.Controls.TextBox? _openApplicationQuickTextBox;
    private List<OpenApplicationCandidate> _openApplicationCandidates = new();

    public ShortcutItem? ResultItem { get; private set; }

    public ShortcutEditWindow()
    {
        InitializeComponent();
        NameTextBox.TextChanged += NameTextBox_TextChanged;
        SelectColor("#CBD5E1");
        ApplyDefaultOpenApplicationIfNeeded(force: true);
        RefreshOpenApplicationCandidates();
    }

    public ShortcutEditWindow(ShortcutItem item)
    {
        InitializeComponent();
        NameTextBox.TextChanged += NameTextBox_TextChanged;

        ResultItem = new ShortcutItem
        {
            Id = item.Id,
            Name = item.Name,
            TargetPath = item.TargetPath,
            Arguments = item.Arguments,
            OpenApplicationPath = item.OpenApplicationPath,
            OpenApplicationArguments = item.OpenApplicationArguments,
            ShortcutType = item.ShortcutType,
            IsFavorite = item.IsFavorite,
            GroupName = item.GroupName,
            Tags = item.Tags,
            TagColor = item.TagColor,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            LaunchCount = item.LaunchCount,
            LastLaunchedAt = item.LastLaunchedAt,
            SortOrder = item.SortOrder,
            Memo = item.Memo,
        };

        NameTextBox.Text = item.Name;
        TargetTextBox.Text = item.TargetPath;
        ArgumentsTextBox.Text = item.Arguments;
        SetOpenApplicationText(string.IsNullOrWhiteSpace(item.OpenApplicationPath)
            ? ShortcutRunner.GetDefaultOpenApplicationPath(item.ShortcutType)
            : item.OpenApplicationPath);
        OpenApplicationArgumentsTextBox.Text = item.OpenApplicationArguments;
        FavoriteCheckBox.IsChecked = item.IsFavorite;
        GroupTextBox.Text = item.GroupName;
        TagsTextBox.Text = item.Tags;
        MemoTextBox.Text = item.Memo;
        SelectColor(item.TagColor);
        RefreshOpenApplicationCandidates();

        _nameEditedByUser = true;
        _openApplicationEditedByUser = !string.IsNullOrWhiteSpace(item.OpenApplicationPath);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            CheckFileExists = false,
            ValidateNames = false,
            FileName = "フォルダを選択"
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        var path = dialog.FileName;

        if (!System.IO.File.Exists(path) &&
            System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(path)))
        {
            path = System.IO.Path.GetDirectoryName(path) ?? path;
        }

        TargetTextBox.Text = path;
    }

    private void QuickOpenApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        var key = (sender as System.Windows.Controls.Button)?.Tag?.ToString() ?? string.Empty;

        RefreshOpenApplicationCandidates();

        if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
        {
            _openApplicationEditedByUser = false;
            OpenApplicationArgumentsTextBox.Text = string.Empty;
            ApplyDefaultOpenApplicationIfNeeded(force: true);
            ApplyOpenApplicationQuickFilter(string.Empty, selectCurrentApplication: true);
            return;
        }

        var candidate = FindQuickOpenApplicationCandidate(key);
        if (candidate is null)
        {
            System.Windows.MessageBox.Show(
                "このPCでは該当アプリが見つかりませんでした。候補検索にアプリ名を入力するか、詳細指定から直接参照してください。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        _openApplicationEditedByUser = true;
        SetOpenApplicationText(candidate.ApplicationPath);
        OpenApplicationArgumentsTextBox.Text = string.Empty;
        ApplyOpenApplicationQuickFilter(string.Empty, selectCurrentApplication: true);
    }

    private OpenApplicationCandidate? FindQuickOpenApplicationCandidate(string key)
    {
        var normalizedKey = key.Trim().ToLowerInvariant();

        bool Match(OpenApplicationCandidate candidate)
        {
            var name = candidate.Name.ToLowerInvariant();
            var path = candidate.ApplicationPath.ToLowerInvariant();

            return normalizedKey switch
            {
                "explorer" => name.Contains("エクスプローラー") || path.EndsWith("explorer.exe") || path == "explorer.exe",
                "vscode" => name.Contains("visual studio code") || path.EndsWith("code.exe"),
                "visualstudio" => (name.Contains("visual studio") && !name.Contains("code")) || path.EndsWith("devenv.exe"),
                "chrome" => name.Contains("chrome") || path.EndsWith("chrome.exe"),
                "edge" => name.Contains("edge") || path.EndsWith("msedge.exe"),
                "excel" => name.Contains("excel") || path.EndsWith("excel.exe"),
                _ => name.Contains(normalizedKey) || path.Contains(normalizedKey),
            };
        }

        return _openApplicationCandidates.FirstOrDefault(Match);
    }

    private void BrowseOpenApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        var shortcutType = ShortcutDetector.DetectType(TargetTextBox.Text.Trim()) ?? ShortcutType.File;
        var dialog = new OpenApplicationPickerWindow(
            shortcutType,
            OpenApplicationTextBox.Text.Trim(),
            OpenApplicationArgumentsTextBox.Text.Trim(),
            TargetTextBox.Text.Trim())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _openApplicationEditedByUser = true;
        SetOpenApplicationText(dialog.ResultApplicationPath);
        OpenApplicationArgumentsTextBox.Text = dialog.ResultApplicationArguments;
        RefreshOpenApplicationCandidates();
    }

    private void ClearOpenApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        _openApplicationEditedByUser = false;
        OpenApplicationArgumentsTextBox.Text = string.Empty;
        ApplyDefaultOpenApplicationIfNeeded(force: true);
        RefreshOpenApplicationCandidates();
    }

    private void TargetTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_nameEditedByUser)
        {
            var target = TargetTextBox.Text.Trim();
            var guessed = ShortcutDetector.GuessName(target);

            if (!string.IsNullOrWhiteSpace(guessed))
            {
                NameTextBox.Text = guessed;
            }
        }

        ApplyDefaultOpenApplicationIfNeeded(force: false);
        RefreshOpenApplicationCandidates();
    }

    private void OpenApplicationQuickComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (OpenApplicationQuickComboBox.Template.FindName("PART_EditableTextBox", OpenApplicationQuickComboBox)
            is not System.Windows.Controls.TextBox editableTextBox)
        {
            return;
        }

        if (_openApplicationQuickTextBox is not null)
        {
            _openApplicationQuickTextBox.TextChanged -= OpenApplicationQuickTextBox_TextChanged;
        }

        _openApplicationQuickTextBox = editableTextBox;
        _openApplicationQuickTextBox.TextChanged += OpenApplicationQuickTextBox_TextChanged;
    }

    private void OpenApplicationQuickTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingQuickOpenApplicationComboBox)
        {
            return;
        }

        if (!OpenApplicationQuickComboBox.IsKeyboardFocusWithin)
        {
            return;
        }

        ApplyOpenApplicationQuickFilter(OpenApplicationQuickComboBox.Text, selectCurrentApplication: false);

        if (OpenApplicationQuickComboBox.Items.Count > 0)
        {
            OpenApplicationQuickComboBox.IsDropDownOpen = true;
        }
    }

    private void OpenApplicationQuickComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingQuickOpenApplicationComboBox)
        {
            return;
        }

        if (OpenApplicationQuickComboBox.SelectedItem is not OpenApplicationCandidate candidate)
        {
            return;
        }

        _openApplicationEditedByUser = true;
        SetOpenApplicationText(candidate.ApplicationPath);

        if (candidate.IsDefault)
        {
            OpenApplicationArgumentsTextBox.Text = string.Empty;
        }
    }

    private void OpenApplicationTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingOpenApplicationText)
        {
            return;
        }

        _openApplicationEditedByUser = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetTextBox.Text.Trim();
        var type = ShortcutDetector.DetectType(target);

        if (type is null)
        {
            System.Windows.MessageBox.Show(
                "存在するフォルダ、ファイル、またはURLを入力してください。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = ShortcutDetector.GuessName(target);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(
                "名前を入力してください。",
                "DHub",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        ResultItem ??= new ShortcutItem
        {
            CreatedAt = DateTime.Now
        };

        ResultItem.Name = name;
        ResultItem.TargetPath = target;
        ResultItem.Arguments = ArgumentsTextBox.Text.Trim();
        ResultItem.OpenApplicationPath = ResolveOpenApplicationPathForSave(type.Value);
        ResultItem.OpenApplicationArguments = OpenApplicationArgumentsTextBox.Text.Trim();
        ResultItem.ShortcutType = type.Value;
        ResultItem.IsFavorite = FavoriteCheckBox.IsChecked == true;
        ResultItem.Tags = TagsTextBox.Text.Trim();
        ResultItem.GroupName = GroupTextBox.Text.Trim();
        ResultItem.TagColor = GetSelectedColor();
        ResultItem.Memo = MemoTextBox.Text;
        ResultItem.UpdatedAt = DateTime.Now;

        DialogResult = true;
        Close();
    }

    private string ResolveOpenApplicationPathForSave(ShortcutType shortcutType)
    {
        var openApplicationPath = OpenApplicationTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(openApplicationPath))
        {
            return ShortcutRunner.GetDefaultOpenApplicationPath(shortcutType);
        }

        return openApplicationPath;
    }

    private void ApplyDefaultOpenApplicationIfNeeded(bool force)
    {
        if (!force && _openApplicationEditedByUser)
        {
            return;
        }

        var type = ShortcutDetector.DetectType(TargetTextBox.Text.Trim());
        var defaultPath = type.HasValue ? ShortcutRunner.GetDefaultOpenApplicationPath(type.Value) : string.Empty;
        SetOpenApplicationText(defaultPath);
    }

    private void SetOpenApplicationText(string value)
    {
        _updatingOpenApplicationText = true;
        OpenApplicationTextBox.Text = value;
        _updatingOpenApplicationText = false;
    }

    private void RefreshOpenApplicationCandidates()
    {
        if (OpenApplicationQuickComboBox is null)
        {
            return;
        }

        var target = TargetTextBox.Text.Trim();
        var type = ShortcutDetector.DetectType(target) ?? ShortcutType.File;
        var currentApplicationPath = OpenApplicationTextBox.Text.Trim();

        _openApplicationCandidates = OpenApplicationDiscovery
            .GetCandidates(type, currentApplicationPath, target)
            .ToList();

        ApplyOpenApplicationQuickFilter(string.Empty, selectCurrentApplication: true);
    }

    private void OpenApplicationQuickComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_updatingQuickOpenApplicationComboBox)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Tab or Key.Escape)
        {
            return;
        }

        ApplyOpenApplicationQuickFilter(OpenApplicationQuickComboBox.Text, selectCurrentApplication: false);

        if (OpenApplicationQuickComboBox.Items.Count > 0)
        {
            OpenApplicationQuickComboBox.IsDropDownOpen = true;
        }
    }

    private void OpenApplicationQuickComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (_updatingQuickOpenApplicationComboBox)
        {
            return;
        }

        var hasSelectedApplication = OpenApplicationQuickComboBox.SelectedItem is OpenApplicationCandidate;
        var keyword = hasSelectedApplication
            ? string.Empty
            : OpenApplicationQuickComboBox.Text;

        ApplyOpenApplicationQuickFilter(keyword, selectCurrentApplication: hasSelectedApplication);
    }

    private void ApplyOpenApplicationQuickFilter(string? keyword, bool selectCurrentApplication)
    {
        var searchText = keyword?.Trim() ?? string.Empty;
        var words = searchText
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var filtered = _openApplicationCandidates.AsEnumerable();
        if (words.Length > 0)
        {
            filtered = filtered.Where(candidate => words.All(word => candidate.SearchText.Contains(word)));
        }

        var filteredList = filtered.ToList();
        var currentApplicationPath = OpenApplicationTextBox.Text.Trim();

        _updatingQuickOpenApplicationComboBox = true;
        OpenApplicationQuickComboBox.ItemsSource = null;
        OpenApplicationQuickComboBox.ItemsSource = filteredList;

        if (selectCurrentApplication)
        {
            OpenApplicationQuickComboBox.SelectedItem = filteredList.FirstOrDefault(x =>
                string.Equals(x.ApplicationPath, currentApplicationPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            OpenApplicationQuickComboBox.SelectedItem = null;
            OpenApplicationQuickComboBox.Text = searchText;
        }

        _updatingQuickOpenApplicationComboBox = false;
    }

    private string GetSelectedColor()
    {
        if (TagColorComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string color &&
            !string.IsNullOrWhiteSpace(color))
        {
            return color;
        }

        return "#CBD5E1";
    }

    private void SelectColor(string color)
    {
        foreach (var item in TagColorComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), color, StringComparison.OrdinalIgnoreCase))
            {
                TagColorComboBox.SelectedItem = item;
                return;
            }
        }

        if (TagColorComboBox.Items.Count > 0)
        {
            TagColorComboBox.SelectedIndex = 0;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nameEditedByUser = !string.IsNullOrWhiteSpace(NameTextBox.Text);
    }
}
