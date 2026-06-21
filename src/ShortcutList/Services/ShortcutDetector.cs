using ShortcutList.Models;

namespace ShortcutList.Services;

public static class ShortcutDetector
{
    public static ShortcutType? DetectType(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        target = target.Trim();

        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return ShortcutType.Url;
        }

        if (System.IO.Directory.Exists(target))
        {
            return ShortcutType.Folder;
        }

        if (System.IO.File.Exists(target))
        {
            return ShortcutType.File;
        }

        return null;
    }

    public static string GuessName(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var type = DetectType(target);

        if (type == ShortcutType.Url && Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return uri.Host.Replace("www.", string.Empty);
        }

        if (type == ShortcutType.Folder || type == ShortcutType.File)
        {
            var trimmed = target.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);

            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }

        return target;
    }
}
