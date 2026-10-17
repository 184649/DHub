using System.Text.Json;
using ShortcutList.Models;

namespace ShortcutList.Services;

internal static class StorageSnapshotDiff
{
    public static string Describe(AppData combined, AppData separated, JsonSerializerOptions options)
    {
        var differences = new List<string>();
        void Compare<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, string> id, Func<T, string> description, string kind)
        {
            var left = a.ToDictionary(id, StringComparer.OrdinalIgnoreCase);
            var right = b.ToDictionary(id, StringComparer.OrdinalIgnoreCase);
            foreach (var item in a)
            {
                if (!right.TryGetValue(id(item), out var other)) differences.Add($"統合のみにある{kind}: {description(item)}");
                else if (JsonSerializer.Serialize(item, options) != JsonSerializer.Serialize(other, options))
                    differences.Add($"{kind}の変更: 統合 [{description(item)}] → 分離 [{description(other)}]");
            }
            foreach (var item in b)
                if (!left.ContainsKey(id(item))) differences.Add($"分離のみにある{kind}: {description(item)}");
            if (a.Count == b.Count && a.All(item => right.ContainsKey(id(item))) &&
                !a.Select(id).SequenceEqual(b.Select(id), StringComparer.OrdinalIgnoreCase)) differences.Add($"{kind}の並び順が異なります。");
        }
        Compare(combined.Items, separated.Items, x => x.Id,
            x => $"{Short(x.Name)} / {Short(x.TargetPath)} / 引数={Short(x.Arguments)} / 開くアプリ={Short(x.OpenApplicationPath)} / アプリ引数={Short(x.OpenApplicationArguments)}", "登録");
        Compare(combined.Workspaces, separated.Workspaces, x => x.Id,
            x => $"{Short(x.Name)} / 参照={Short(string.Join(",", x.ShortcutIds))} / 待機={x.DelaySeconds}", "作業セット");
        Compare(combined.Commands, separated.Commands, x => x.Id,
            x => $"{Short(x.Name)} / {Short(x.Command)} / 引数={Short(x.Arguments)} / 作業場所={Short(x.WorkingDirectory)}", "コマンド");
        var aSettings = JsonSerializer.SerializeToElement(combined.Settings, options);
        var bSettings = JsonSerializer.SerializeToElement(separated.Settings, options);
        foreach (var property in aSettings.EnumerateObject())
            if (bSettings.TryGetProperty(property.Name, out var other) && property.Value.ToString() != other.ToString())
                differences.Add($"設定 {property.Name}: 統合 {Short(property.Value.ToString())} → 分離 {Short(other.ToString())}");
        if (JsonSerializer.Serialize(combined.Logs, options) != JsonSerializer.Serialize(separated.Logs, options))
            differences.Add($"操作ログが異なります (統合 {combined.Logs.Count} 件 / 分離 {separated.Logs.Count} 件)。");
        const int limit = 20;
        return $"差分 {differences.Count} 件 (統合 → 分離):\n" + string.Join("\n", differences.Take(limit).Select(x => "・" + x)) +
            (differences.Count > limit ? $"\nほか {differences.Count - limit} 件。元ファイルは選択前に保全します。" : string.Empty);
    }

    private static string Short(string? text)
    {
        var value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return value.Length <= 100 ? value : value[..100] + "…";
    }
}
