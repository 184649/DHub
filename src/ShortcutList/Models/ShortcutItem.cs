using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using ShortcutList.Services;

namespace ShortcutList.Models;

public class ShortcutItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _targetPath = string.Empty;
    private string _arguments = string.Empty;
    private string _openApplicationPath = string.Empty;
    private string _openApplicationArguments = string.Empty;
    private string _groupName = string.Empty;
    private string _tags = string.Empty;
    private string _tagColor = "#CBD5E1";
    private string _memo = string.Empty;
    private ShortcutType _shortcutType;
    private bool _isFavorite;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private int _launchCount;
    private DateTime? _lastLaunchedAt;
    private int _sortOrder;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get => _id; set => SetField(ref _id, value); }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (SetField(ref _targetPath, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SearchText));
                OnPropertyChanged(nameof(TypeText));
                OnPropertyChanged(nameof(IconText));
                OnPropertyChanged(nameof(IconImageSource));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsBroken));
            }
        }
    }

    public string Arguments
    {
        get => _arguments;
        set
        {
            if (SetField(ref _arguments, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string OpenApplicationPath
    {
        get => _openApplicationPath;
        set
        {
            if (SetField(ref _openApplicationPath, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(OpenApplicationDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string OpenApplicationArguments
    {
        get => _openApplicationArguments;
        set
        {
            if (SetField(ref _openApplicationArguments, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(OpenApplicationDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public ShortcutType ShortcutType
    {
        get => _shortcutType;
        set
        {
            if (SetField(ref _shortcutType, value))
            {
                OnPropertyChanged(nameof(TypeText));
                OnPropertyChanged(nameof(IconText));
                OnPropertyChanged(nameof(IconImageSource));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsBroken));
                OnPropertyChanged(nameof(OpenApplicationDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetField(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteText));
                OnPropertyChanged(nameof(FavoriteDisplay));
                OnPropertyChanged(nameof(FavoriteActionText));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetField(ref _groupName, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(GroupDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string Tags
    {
        get => _tags;
        set
        {
            if (SetField(ref _tags, NormalizeTagsText(value)))
            {
                OnPropertyChanged(nameof(TagsDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public string TagColor
    {
        get => _tagColor;
        set => SetField(ref _tagColor, string.IsNullOrWhiteSpace(value) ? "#CBD5E1" : value);
    }

    public string Memo
    {
        get => _memo;
        set
        {
            if (SetField(ref _memo, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(MemoDisplay));
                OnPropertyChanged(nameof(SearchText));
            }
        }
    }

    public DateTime CreatedAt { get => _createdAt; set => SetField(ref _createdAt, value); }
    public DateTime UpdatedAt { get => _updatedAt; set => SetField(ref _updatedAt, value); }
    public int LaunchCount { get => _launchCount; set => SetField(ref _launchCount, value); }
    public DateTime? LastLaunchedAt { get => _lastLaunchedAt; set => SetField(ref _lastLaunchedAt, value); }
    public int SortOrder { get => _sortOrder; set => SetField(ref _sortOrder, value); }

    [JsonIgnore]
    public string TypeText => ShortcutType switch
    {
        ShortcutType.Folder => "フォルダ",
        ShortcutType.Url => "URL",
        ShortcutType.File => "ファイル",
        _ => "不明"
    };

    [JsonIgnore]
    public string IconText => ShortcutType switch
    {
        ShortcutType.Folder => "📁",
        ShortcutType.Url => "🌐",
        ShortcutType.File => "📄",
        _ => "□"
    };

    [JsonIgnore]
    public ImageSource? IconImageSource => FileIconService.GetIcon(this);

    [JsonIgnore]
    public string FavoriteText => IsFavorite ? "★" : "☆";
    [JsonIgnore]
    public string FavoriteActionText => IsFavorite ? "★ 解除" : "☆ 登録";
    [JsonIgnore]
    public string FavoriteDisplay => IsFavorite ? "お気に入り" : "通常";

    [JsonIgnore]
    public string GroupDisplay => string.IsNullOrWhiteSpace(GroupName) ? "未分類" : GroupName;
    [JsonIgnore]
    public string TagsDisplay => string.IsNullOrWhiteSpace(Tags) ? "-" : Tags;
    [JsonIgnore]
    public string MemoDisplay => string.IsNullOrWhiteSpace(Memo) ? "-" : Memo;

    [JsonIgnore]
    public bool IsBroken => ShortcutType switch
    {
        ShortcutType.Folder => !System.IO.Directory.Exists(TargetPath),
        ShortcutType.File => !System.IO.File.Exists(TargetPath),
        _ => false
    };

    [JsonIgnore]
    public string StatusText => IsBroken ? "未検出" : "OK";

    [JsonIgnore]
    public string LastLaunchedDisplay => LastLaunchedAt.HasValue ? LastLaunchedAt.Value.ToString("yyyy/MM/dd HH:mm") : "-";

    [JsonIgnore]
    public string OpenApplicationDisplay
    {
        get
        {
            var path = OpenApplicationPath;

            if (string.IsNullOrWhiteSpace(path) && ShortcutType == ShortcutType.Folder)
            {
                return "エクスプローラー";
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return "既定のアプリ";
            }

            if (string.Equals(path, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(System.IO.Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                return "エクスプローラー";
            }

            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }
    }

    [JsonIgnore]
    public string SearchText => $"{Name} {TargetPath} {Arguments} {OpenApplicationPath} {OpenApplicationArguments} {OpenApplicationDisplay} {FavoriteDisplay} {TypeText} {GroupDisplay} {TagsDisplay} {Memo}".ToLowerInvariant();

    public IReadOnlyList<string> GetTagList()
    {
        return SplitTags(Tags).ToList();
    }

    public bool HasTag(string tag)
    {
        return SplitTags(Tags).Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
    }

    public void TouchUpdated()
    {
        UpdatedAt = DateTime.Now;
        OnPropertyChanged(nameof(UpdatedAt));
        RefreshStatus();
    }

    public void TouchLaunched()
    {
        LaunchCount++;
        LastLaunchedAt = DateTime.Now;
        OnPropertyChanged(nameof(LaunchCount));
        OnPropertyChanged(nameof(LastLaunchedAt));
        OnPropertyChanged(nameof(LastLaunchedDisplay));
    }

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SearchText));
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

    private static string NormalizeTagsText(string? tags)
    {
        return string.Join(", ", SplitTags(tags));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

