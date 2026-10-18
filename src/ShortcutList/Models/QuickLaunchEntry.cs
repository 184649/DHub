namespace ShortcutList.Models;

public enum QuickLaunchKind
{
    Shortcut,
    LinkedBookmark,
    Workspace,
    SourceFolder
}

// A transient view of an existing registration. Payload and RecipeToken retain its
// identity/launch recipe; search never resolves paths, reads files, or executes it.
public sealed record QuickLaunchEntry(string Key, string Name, string TargetPath, string Detail,
    string IconText, bool IsFavorite, int SortOrder, string SearchText, QuickLaunchKind Kind,
    object Payload, string RecipeToken, bool CanLaunch = true)
{
    public string FavoriteText => IsFavorite ? "★" : "☆";
    public string KindDisplay => Kind switch
    {
        QuickLaunchKind.Workspace => "まとめて開く",
        _ => "開く"
    };
}
