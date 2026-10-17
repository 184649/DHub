namespace ShortcutList.Models;

// No WPF, shell or filesystem dependencies: the bar's order and bounds are testable off Windows.
public sealed record LauncherPin(string Id, string Name, int SortOrder);
public sealed record LauncherPinRecipe(string TargetPath, string Arguments, string OpenApplicationPath,
    string OpenApplicationArguments, int ShortcutType);
public readonly record struct LauncherBounds(double Left, double Top, double Width, double Height);
public readonly record struct LauncherPosition(double Left, double Top);

public static class LauncherBarLayout
{
    public static IReadOnlyList<string> OrderedIds(IEnumerable<LauncherPin> pins) => pins
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.Id, StringComparer.Ordinal)
        .Select(x => x.Id).ToArray();

    public static string CompactName(string name, int limit)
    {
        limit = Math.Max(3, limit);
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        if (starts.Length <= limit) return name;
        var prefixCount = (limit - 1) / 2;
        var suffixCount = limit - 1 - prefixCount;
        return name[..starts[prefixCount]] + "…" + name[starts[starts.Length - suffixCount]..];
    }

    public static string Label(string id, string name, int limit, IEnumerable<LauncherPin> entries)
    {
        var compact = CompactName(name, limit);
        var peers = entries.Where(x => string.Equals(CompactName(x.Name, limit), compact, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id).ToArray();
        // Prefix remains visible even when a narrow button trims the end of a long Japanese name.
        return peers.Length > 1 ? $"[{Array.IndexOf(peers, id) + 1}] {compact}" : compact;
    }

    public static IReadOnlyList<string> Move(IReadOnlyList<string> orderedIds, string id, int direction)
    {
        var result = orderedIds.ToArray();
        var index = Array.IndexOf(result, id);
        if (direction is not (-1 or 1) || index < 0) return result;
        var destination = index + direction;
        if (destination < 0 || destination >= result.Length) return result;
        (result[index], result[destination]) = (result[destination], result[index]);
        return result;
    }

    public static LauncherPosition Place(LauncherBounds workArea, double width, double height,
        double? left = null, double? top = null)
    {
        if (!double.IsFinite(workArea.Left) || !double.IsFinite(workArea.Top) ||
            !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) ||
            workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        width = double.IsFinite(width) ? Math.Clamp(width, 0, workArea.Width) : workArea.Width;
        height = double.IsFinite(height) ? Math.Clamp(height, 0, workArea.Height) : workArea.Height;
        var maxLeft = workArea.Left + workArea.Width - width;
        var maxTop = workArea.Top + workArea.Height - height;
        var x = left.HasValue && double.IsFinite(left.Value) ? left.Value : workArea.Left + 8;
        var y = top.HasValue && double.IsFinite(top.Value) ? top.Value : maxTop - 8;
        return new(Math.Clamp(x, workArea.Left, maxLeft), Math.Clamp(y, workArea.Top, maxTop));
    }

    public static bool SameInstances<T>(IReadOnlyList<T> previous, IReadOnlyList<T> current) where T : class
    {
        if (previous.Count != current.Count) return false;
        for (var i = 0; i < previous.Count; i++)
            if (!ReferenceEquals(previous[i], current[i])) return false;
        return true;
    }

    public static Exception? SaveEdit(Action change, Action save, Action rollback)
    {
        try { change(); save(); return null; }
        catch (Exception error)
        {
            try { rollback(); }
            catch (Exception rollbackError) { return new AggregateException(error, rollbackError); }
            return error; // The caller must disclose uncertain disk state; never retry writes automatically.
        }
    }

    public static int VisiblePinCount(int count, double availableWidth)
    {
        // 248 DIP reserved for the grip, menu, add, overflow and close controls.
        if (count <= 0 || !double.IsFinite(availableWidth)) return 0;
        return (int)Math.Clamp(Math.Floor(Math.Max(0, availableWidth - 248) / 124), 0, Math.Min(count, 8));
    }
}
