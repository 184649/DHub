using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShortcutList.Models;

public class WorkspaceItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _memo = string.Empty;
    private string _checklist = string.Empty;
    private List<string> _shortcutIds = new();
    private int _delaySeconds;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private DateTime? _lastLaunchedAt;
    private int _launchCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set => SetField(ref _id, value ?? Guid.NewGuid().ToString("N")); }
    public string Name { get => _name; set => SetField(ref _name, value?.Trim() ?? string.Empty); }
    public string Description { get => _description; set => SetField(ref _description, value?.Trim() ?? string.Empty); }
    public string Memo { get => _memo; set => SetField(ref _memo, value ?? string.Empty); }
    public string Checklist { get => _checklist; set => SetField(ref _checklist, value ?? string.Empty); }
    public List<string> ShortcutIds { get => _shortcutIds; set => SetField(ref _shortcutIds, value ?? new List<string>()); }
    public int DelaySeconds { get => _delaySeconds; set => SetField(ref _delaySeconds, Math.Max(0, value)); }
    public DateTime CreatedAt { get => _createdAt; set => SetField(ref _createdAt, value); }
    public DateTime UpdatedAt { get => _updatedAt; set => SetField(ref _updatedAt, value); }
    public DateTime? LastLaunchedAt { get => _lastLaunchedAt; set => SetField(ref _lastLaunchedAt, value); }
    public int LaunchCount { get => _launchCount; set => SetField(ref _launchCount, value); }

    public string CountDisplay => $"{ShortcutIds.Count} 件";
    public string LastLaunchedDisplay => LastLaunchedAt.HasValue ? LastLaunchedAt.Value.ToString("yyyy/MM/dd HH:mm") : "-";
    public string SearchText => $"{Name} {Description} {Memo} {Checklist}".ToLowerInvariant();

    public void TouchUpdated()
    {
        UpdatedAt = DateTime.Now;
        OnPropertyChanged(nameof(UpdatedAt));
    }

    public void TouchLaunched()
    {
        LaunchCount++;
        LastLaunchedAt = DateTime.Now;
        OnPropertyChanged(nameof(LaunchCount));
        OnPropertyChanged(nameof(LastLaunchedAt));
        OnPropertyChanged(nameof(LastLaunchedDisplay));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
