using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShortcutList.Models;

public class CommandItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _command = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _groupName = string.Empty;
    private string _tags = string.Empty;
    private string _memo = string.Empty;
    private bool _runAsAdministrator;
    private bool _confirmBeforeRun = true;
    private bool _isFavorite;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private DateTime? _lastRunAt;
    private int _runCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set => SetField(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value); }
    public string Name { get => _name; set => SetField(ref _name, value?.Trim() ?? string.Empty); }
    public string Command { get => _command; set => SetField(ref _command, value?.Trim() ?? string.Empty); }
    public string Arguments { get => _arguments; set => SetField(ref _arguments, value?.Trim() ?? string.Empty); }
    public string WorkingDirectory { get => _workingDirectory; set => SetField(ref _workingDirectory, value?.Trim() ?? string.Empty); }
    public string GroupName { get => _groupName; set => SetField(ref _groupName, value?.Trim() ?? string.Empty); }
    public string Tags { get => _tags; set => SetField(ref _tags, NormalizeTagsText(value)); }
    public string Memo { get => _memo; set => SetField(ref _memo, value ?? string.Empty); }
    public bool RunAsAdministrator { get => _runAsAdministrator; set => SetField(ref _runAsAdministrator, value); }
    public bool ConfirmBeforeRun { get => _confirmBeforeRun; set => SetField(ref _confirmBeforeRun, value); }
    public bool IsFavorite { get => _isFavorite; set => SetField(ref _isFavorite, value); }
    public DateTime CreatedAt { get => _createdAt; set => SetField(ref _createdAt, value); }
    public DateTime UpdatedAt { get => _updatedAt; set => SetField(ref _updatedAt, value); }
    public DateTime? LastRunAt { get => _lastRunAt; set => SetField(ref _lastRunAt, value); }
    public int RunCount { get => _runCount; set => SetField(ref _runCount, value); }

    public string GroupDisplay => string.IsNullOrWhiteSpace(GroupName) ? "未分類" : GroupName;
    public string TagsDisplay => string.IsNullOrWhiteSpace(Tags) ? "-" : Tags;
    public string FavoriteText => IsFavorite ? "★" : "☆";
    public string LastRunDisplay => LastRunAt.HasValue ? LastRunAt.Value.ToString("yyyy/MM/dd HH:mm") : "-";
    public string SearchText => $"{Name} {Command} {Arguments} {WorkingDirectory} {GroupDisplay} {TagsDisplay} {Memo}".ToLowerInvariant();

    public IReadOnlyList<string> GetTagList() => SplitTags(Tags).ToList();

    public void TouchUpdated()
    {
        UpdatedAt = DateTime.Now;
        OnPropertyChanged(nameof(UpdatedAt));
    }

    public void TouchRun()
    {
        RunCount++;
        LastRunAt = DateTime.Now;
        OnPropertyChanged(nameof(RunCount));
        OnPropertyChanged(nameof(LastRunAt));
        OnPropertyChanged(nameof(LastRunDisplay));
    }

    public static IEnumerable<string> SplitTags(string? tags)
    {
        return (tags ?? string.Empty)
            .Replace('、', ',')
            .Replace('，', ',')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeTagsText(string? tags) => string.Join(", ", SplitTags(tags));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SearchText));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
