using System.IO;

namespace ShortcutList.Services;

internal enum ActivationCommand : byte { Show = 1, Settings = 2 }
internal readonly record struct StartupOptions(string DataDirectory, ActivationCommand? Activation);

internal static class AppProfile
{
    internal const string UsageMessage = "使用方法: DHub.exe [--data-dir 保存先フォルダー] [--show | --settings]";

    internal static string ResolveDataDirectory(string[] args, bool isPreviewBuild) => Parse(args, isPreviewBuild).DataDirectory;

    internal static StartupOptions Parse(string[] args, bool isPreviewBuild)
    {
        string? directory = null;
        ActivationCommand? activation = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data-dir" when directory is null && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    directory = args[++i];
                    break;
                case "--show" when activation is null:
                    activation = ActivationCommand.Show;
                    break;
                case "--settings" when activation is null:
                    activation = ActivationCommand.Settings;
                    break;
                default: throw new ArgumentException(UsageMessage);
            }
        }
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            isPreviewBuild ? "DHub-Preview" : "DHub");
        return new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), activation);
    }
}
