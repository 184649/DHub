namespace ShortcutList.Models;

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;

    // DHub.zip のURL
    public string DownloadUrl { get; set; } = string.Empty;

    // DHubUpdater.zip のURL
    public string UpdaterUrl { get; set; } = string.Empty;

    public string ReleaseNotes { get; set; } = string.Empty;
}
