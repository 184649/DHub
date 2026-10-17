namespace ShortcutList.Models;

// References only: disconnecting or hiding an entry never deletes source files.
public sealed class BookmarkSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public List<string> HiddenPaths { get; set; } = new();
    public override string ToString() => $"{Name} — {FolderPath}";
}
