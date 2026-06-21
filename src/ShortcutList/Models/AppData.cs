namespace ShortcutList.Models;

public class AppData
{
    public int SchemaVersion { get; set; } = 4;
    public List<ShortcutItem> Items { get; set; } = new();
    public List<WorkspaceItem> Workspaces { get; set; } = new();
    public List<CommandItem> Commands { get; set; } = new();
    public List<OperationLogItem> Logs { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
