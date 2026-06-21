namespace ShortcutList.Models;

public enum UnifiedSearchResultKind
{
    Shortcut,
    Workspace,
    Command,
    Action,
    Log
}

public class UnifiedSearchResult
{
    public UnifiedSearchResultKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public Action? ExecuteAction { get; set; }

    public string KindText => Kind switch
    {
        UnifiedSearchResultKind.Shortcut => "ショートカット",
        UnifiedSearchResultKind.Workspace => "ワークスペース",
        UnifiedSearchResultKind.Command => "コマンド",
        UnifiedSearchResultKind.Action => "操作",
        UnifiedSearchResultKind.Log => "ログ",
        _ => "その他"
    };

    public string DisplayText => $"[{KindText}] {Title}";
    public override string ToString() => string.IsNullOrWhiteSpace(Subtitle) ? DisplayText : $"{DisplayText} - {Subtitle}";
}
