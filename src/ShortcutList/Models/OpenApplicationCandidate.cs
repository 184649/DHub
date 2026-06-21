namespace ShortcutList.Models;

public sealed class OpenApplicationCandidate
{
    public OpenApplicationCandidate(
        string name,
        string applicationPath,
        string category,
        string description = "",
        bool isDefault = false)
    {
        Name = name;
        ApplicationPath = applicationPath?.Trim() ?? string.Empty;
        Category = category;
        Description = description;
        IsDefault = isDefault;
    }

    public string Name { get; }
    public string ApplicationPath { get; }
    public string Category { get; }
    public string Description { get; }
    public bool IsDefault { get; }

    public string DisplayPath => string.IsNullOrWhiteSpace(ApplicationPath)
        ? "OS既定のアプリ"
        : ApplicationPath;

    public string SearchText => $"{Name} {ApplicationPath} {Category} {Description}".ToLowerInvariant();

    public override string ToString() => Name;
}
