namespace ShortcutList.Models;

public class AppSettings
{
    public int SchemaVersion { get; set; } = 4;

    // Legacy presentation values are retained for export/rollback only. The hidden launcher never reads them.
    public bool ShowLauncherBar { get; set; } = false;

    // Desktop coordinates from GetWindowRect, kept in the same coordinate space as Screen.WorkingArea.
    // Null means first use: place the bar above the primary taskbar without moving the OS taskbar.
    public int? LauncherBarLeft { get; set; }
    public int? LauncherBarTop { get; set; }

    public bool StartMinimizedToTray { get; set; } = true;

    public bool ShowNameColumn { get; set; } = true;
    public bool ShowPathColumn { get; set; } = true;
    public bool ShowArgumentsColumn { get; set; } = false;
    public bool ShowOpenApplicationColumn { get; set; } = true;
    public bool ShowGroupColumn { get; set; } = true;
    public bool ShowTagsColumn { get; set; } = true;
    public bool ShowLaunchCountColumn { get; set; } = true;
    public bool ShowLastLaunchColumn { get; set; } = true;
    public bool ShowCreatedAtColumn { get; set; } = false;
    public bool ShowUpdatedAtColumn { get; set; } = true;
    public bool ShowStatusColumn { get; set; } = true;

    public string UpdateVersionUrl { get; set; } = "https://raw.githubusercontent.com/184649/DHub/main/version.json";
    public bool CheckUpdateOnStartup { get; set; } = true;

    public bool EnableGlobalHotkeys { get; set; } = true;
    public string QuickSearchHotkey { get; set; } = "Ctrl+Shift+Space";
    public bool EnableAutoBackup { get; set; } = true;
    public int AutoBackupKeepCount { get; set; } = 20;

    public bool EnableSafeSave { get; set; } = true;
    public bool AutoRecoverOnStartup { get; set; } = true;
    public bool ShowRestoreMessageAfterRecovery { get; set; } = true;
    public bool UseRealFileIcons { get; set; } = true;

    // Legacy startup preference is retained, but hidden startup does not display the dashboard.
    public bool ShowHomeOnStartup { get; set; } = false;
    public bool EnableOperationLog { get; set; } = true;
    public int OperationLogKeepCount { get; set; } = 1000;
    public bool EnableSeparatedDataFiles { get; set; } = true;
}

