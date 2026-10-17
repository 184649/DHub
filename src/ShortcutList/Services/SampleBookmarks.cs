using System.IO;
using System.Text;
using ShortcutList.Models;

namespace ShortcutList.Services;

/// <summary>Opt-in local examples only. No shell calls, downloads or changes to user documents.</summary>
public static class SampleBookmarks
{
    public const string OfficialUrl = "https://support.microsoft.com/ja-jp/windows";
    public const string GroupName = "DHub サンプル";
    public static string DirectoryPath(string profile) => Path.Combine(profile, "Samples", "GettingStarted-v1");

    public static IReadOnlyList<ShortcutItem> Catalog(string profile)
    {
        var root = DirectoryPath(profile);
        return new[]
        {
            Create("file", "サンプル: 使い方メモ", Path.Combine(root, "使い方メモ.txt"), ShortcutType.File,
                "テキストを既定のアプリで開く例。自分のメモを書き加えても、サンプル再追加で上書きしません。"),
            Create("folder", "サンプル: 作業フォルダー", Path.Combine(root, "作業フォルダー"), ShortcutType.Folder,
                "資料をまとめたフォルダーを開く例。専用のサンプルフォルダーです。"),
            Create("url", "サンプル: Windows ヘルプ", OfficialUrl, ShortcutType.Url,
                "Microsoft公式Windowsヘルプを既定のブラウザーで開く例。クリックするまでアクセスしません。")
        };
    }

    private static ShortcutItem Create(string key, string name, string target, ShortcutType type, string memo) => new()
    {
        // Stable IDs also recognize renamed/edited samples without overwriting those edits.
        Id = "dhub.sample.v1." + key, Name = name, TargetPath = target, ShortcutType = type,
        GroupName = GroupName, Tags = "サンプル", Memo = memo, IsFavorite = true
    };

    // Sample-only comparison. The normal editor persists explorer.exe for folders,
    // while older registrations may leave it blank; ShortcutRunner launches both identically.
    // Keep every argument and target exact, and do not broaden the store's general deduplication.
    public static bool SameRecipe(ShortcutItem existing, ShortcutItem sample) =>
        existing.ShortcutType == sample.ShortcutType &&
        string.Equals(existing.TargetPath, sample.TargetPath, StringComparison.Ordinal) &&
        string.Equals(existing.Arguments, sample.Arguments, StringComparison.Ordinal) &&
        string.Equals(existing.OpenApplicationArguments, sample.OpenApplicationArguments, StringComparison.Ordinal) &&
        (sample.ShortcutType == ShortcutType.Folder
            ? string.Equals(ShortcutRunner.GetEffectiveOpenApplicationPath(existing),
                ShortcutRunner.GetEffectiveOpenApplicationPath(sample), StringComparison.OrdinalIgnoreCase)
            : string.Equals(existing.OpenApplicationPath, sample.OpenApplicationPath, StringComparison.Ordinal));

    public static void PrepareFiles(string profile)
    {
        EnsureDirectory(profile);
        EnsureDirectory(Path.Combine(profile, "Samples"));
        var root = DirectoryPath(profile);
        EnsureDirectory(root);
        EnsureDirectory(Path.Combine(root, "作業フォルダー"));
        CreateTextIfMissing(Path.Combine(root, "使い方メモ.txt"),
            "DHub のブックマークサンプル\r\n\r\n" +
            "1. 固定バーのボタンを1回クリックすると、ファイル・フォルダー・URLを開けます。\r\n" +
            "2. 自分の項目を使うには、固定バーへファイルやフォルダー、http/https URLをドロップします。\r\n" +
            "3. ボタンの右クリックで左右に移動、またはバーから外せます。\r\n" +
            "4. サンプルが不要なら管理画面で選んで「削除」。登録だけを削除し、このファイルは残ります。\r\n" +
            "5. 作業の概要は設定メニューから必要なときだけ開けます。日常の起動に必須ではありません。\r\n\r\n" +
            "このメモは自由に編集できます。サンプルを再追加しても既存の内容は上書きしません。\r\n");
        CreateTextIfMissing(Path.Combine(root, "作業フォルダー", "このフォルダーについて.txt"),
            "ここはDHub専用のサンプル作業フォルダーです。\r\n" +
            "普段はよく使う資料フォルダーを同じように登録できます。\r\n" +
            "DHubで登録を削除しても、このフォルダーや中身は削除しません。\r\n");
    }

    private static FileAttributes? Attributes(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("サンプルの保存先にリンクがあります。実フォルダーを使用してください: " + path);
            return attributes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void EnsureDirectory(string path)
    {
        var attributes = Attributes(path);
        if (attributes.HasValue && (attributes.Value & FileAttributes.Directory) == 0)
            throw new IOException("サンプルフォルダーの場所にファイルがあります: " + path);
        Directory.CreateDirectory(path);
    }

    private static void CreateTextIfMissing(string path, string text)
    {
        var attributes = Attributes(path);
        if (attributes.HasValue)
        {
            if ((attributes.Value & FileAttributes.Directory) != 0)
                throw new IOException("サンプルファイルの場所にフォルダーがあります: " + path);
            return; // Never overwrite existing text, including the user's edits.
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }
}
