using System.IO;

namespace ShortcutList.Services;

internal static class AppProfile
{
    internal const string UsageMessage = "使用方法: DHub.exe [--data-dir 保存先フォルダー]";

    internal static string ResolveDataDirectory(string[] args, bool isPreviewBuild)
    {
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--data-dir" || string.IsNullOrWhiteSpace(args[1])))
            throw new ArgumentException(UsageMessage);
        var directory = args.Length == 2 ? args[1] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), isPreviewBuild ? "DHub-Preview" : "DHub");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }
}
