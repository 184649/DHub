namespace ShortcutList.Models;

public class OperationLogItem
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy/MM/dd HH:mm:ss");
    public string DisplayText => $"{CreatedAtDisplay} [{Level}] {Category} {Message}";
}
