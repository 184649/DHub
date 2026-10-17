using System.IO;
using ShortcutList.Models;

namespace ShortcutList.Services;

internal static class StorageIntegrity
{
    public static void Validate(AppData data)
    {
        if (data.SchemaVersion > 4 || data.SchemaVersion < 0) throw new InvalidDataException("未対応のデータ形式です。新しいバージョンで確認してください。");
        if (data.Settings is null || data.Settings.SchemaVersion > 4 || data.Settings.SchemaVersion < 0)
            throw new InvalidDataException("設定データの形式が不正、または未対応です。");
        if (data.Items is null || data.Workspaces is null || data.Commands is null || data.Logs is null || data.BookmarkSources is null)
            throw new InvalidDataException("一覧に null が含まれています。");
        var itemIds = ValidateIds(data.Items, item => item.Id, "登録");
        ValidateIds(data.Workspaces, item => item.Id, "作業セット");
        ValidateIds(data.Commands, item => item.Id, "コマンド");
        ValidateIds(data.BookmarkSources, item => item.Id, "フォルダー参照");
        foreach (var item in data.Items)
            if (!Enum.IsDefined(typeof(ShortcutType), item.ShortcutType)) throw new InvalidDataException("登録の種類が不正です。");
        foreach (var workspace in data.Workspaces)
        {
            if (workspace.ShortcutIds is null) throw new InvalidDataException("作業セットの参照が不正です。");
            foreach (var id in workspace.ShortcutIds)
                if (string.IsNullOrWhiteSpace(id) || !itemIds.Contains(id))
                    throw new InvalidDataException($"作業セット「{workspace.Name}」に見つからない登録 ID があります。元データは削除しません。");
        }
        if (data.Logs.Any(item => item is null)) throw new InvalidDataException("ログに null が含まれています。");
        foreach (var source in data.BookmarkSources)
            if (string.IsNullOrWhiteSpace(source.FolderPath) || source.HiddenPaths is null || source.HiddenPaths.Any(path => path is null))
                throw new InvalidDataException("フォルダー参照の設定が不正です。");
    }

    private static HashSet<string> ValidateIds<T>(IEnumerable<T> items, Func<T, string> id, string category) where T : class
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item is null) throw new InvalidDataException($"{category}に null が含まれています。");
            var value = id(item);
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new InvalidDataException($"{category}の ID が不正です。");
            if (!seen.Add(value)) throw new InvalidDataException($"{category}の ID が重複しています: {value}。元データは削除しません。");
        }
        return seen;
    }
}
