using System.Diagnostics;
using ShortcutList.Models;

namespace ShortcutList.Services;

public static class ShortcutRunner
{
    private const string TargetPlaceholder = "{target}";

    public static void Run(ShortcutItem item)
    {
        var openApplicationPath = GetEffectiveOpenApplicationPath(item);

        if (!string.IsNullOrWhiteSpace(openApplicationPath))
        {
            RunWithApplication(item, openApplicationPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = item.TargetPath,
            Arguments = item.Arguments ?? string.Empty,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    public static string GetDefaultOpenApplicationPath(ShortcutType shortcutType)
    {
        return shortcutType == ShortcutType.Folder ? "explorer.exe" : string.Empty;
    }

    public static string GetEffectiveOpenApplicationPath(ShortcutItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.OpenApplicationPath))
        {
            return item.OpenApplicationPath.Trim();
        }

        return GetDefaultOpenApplicationPath(item.ShortcutType);
    }

    public static string QuoteArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        var escaped = value.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var folder = path;

        if (System.IO.File.Exists(path))
        {
            folder = System.IO.Path.GetDirectoryName(path) ?? path;
        }

        if (!System.IO.Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }


    public static void RunCommand(CommandItem command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Command,
            Arguments = command.Arguments ?? string.Empty,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            startInfo.WorkingDirectory = command.WorkingDirectory;
        }

        if (command.RunAsAdministrator)
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }

    private static void RunWithApplication(ShortcutItem item, string openApplicationPath)
    {
        var arguments = BuildOpenApplicationArguments(item);

        Process.Start(new ProcessStartInfo
        {
            FileName = openApplicationPath,
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static string BuildOpenApplicationArguments(ShortcutItem item)
    {
        var target = QuoteArgument(item.TargetPath);
        var openArguments = item.OpenApplicationArguments?.Trim() ?? string.Empty;
        var itemArguments = item.Arguments?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(openArguments) &&
            openArguments.Contains(TargetPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            var replaced = openArguments.Replace(TargetPlaceholder, target, StringComparison.OrdinalIgnoreCase);
            return string.IsNullOrWhiteSpace(itemArguments) ? replaced : $"{replaced} {itemArguments}";
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(openArguments))
        {
            parts.Add(openArguments);
        }

        parts.Add(target);

        if (!string.IsNullOrWhiteSpace(itemArguments))
        {
            parts.Add(itemArguments);
        }

        return string.Join(" ", parts);
    }
}
