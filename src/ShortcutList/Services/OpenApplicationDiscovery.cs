using System.IO;
using Microsoft.Win32;
using ShortcutList.Models;

namespace ShortcutList.Services;

public static class OpenApplicationDiscovery
{
    public static IReadOnlyList<OpenApplicationCandidate> GetCandidates(
        ShortcutType shortcutType,
        string? currentPath = null,
        string? targetPath = null)
    {
        var candidates = new List<OpenApplicationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (shortcutType == ShortcutType.Folder)
        {
            AddCommand(candidates, seen, "エクスプローラー", "explorer.exe", "推奨", "フォルダをWindowsエクスプローラーで開きます。", isDefault: true);
        }
        else
        {
            AddCommand(candidates, seen, "OS既定のアプリ", string.Empty, "推奨", "Windowsの関連付けに従って開きます。", isDefault: true);
        }

        AddCommand(candidates, seen, "エクスプローラー", "explorer.exe", "標準", "フォルダやファイルの場所を開くときに使用します。", isDefault: shortcutType == ShortcutType.Folder);
        AddIfExists(candidates, seen, "メモ帳", Expand(@"%SystemRoot%\System32\notepad.exe"), "標準", "テキストファイルを軽く開く場合に便利です。");
        AddIfExists(candidates, seen, "Windows Terminal", Expand(@"%LocalAppData%\Microsoft\WindowsApps\wt.exe"), "ターミナル", "ターミナルを開きます。");
        AddIfExists(candidates, seen, "PowerShell", Expand(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"), "ターミナル", "PowerShellで開きます。");
        AddIfExists(candidates, seen, "PowerShell 7", Expand(@"%ProgramFiles%\PowerShell\7\pwsh.exe"), "ターミナル", "PowerShell 7で開きます。");
        AddIfExists(candidates, seen, "コマンドプロンプト", Expand(@"%SystemRoot%\System32\cmd.exe"), "ターミナル", "cmd.exeで開きます。");

        AddKnownEditorCandidates(candidates, seen);
        AddKnownBrowserCandidates(candidates, seen);
        AddKnownOfficeCandidates(candidates, seen);
        AddKnownToolCandidates(candidates, seen);
        AddRegistryAppPathCandidates(candidates, seen);
        AddCurrentIfNeeded(candidates, seen, currentPath);

        return candidates
            .OrderBy(x => GetSortPriority(x, shortcutType, targetPath))
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int GetSortPriority(OpenApplicationCandidate candidate, ShortcutType shortcutType, string? targetPath)
    {
        if (candidate.IsDefault)
        {
            return 0;
        }

        var name = candidate.Name.ToLowerInvariant();
        var category = candidate.Category.ToLowerInvariant();
        var extension = string.Empty;

        try
        {
            extension = string.IsNullOrWhiteSpace(targetPath)
                ? string.Empty
                : Path.GetExtension(targetPath).ToLowerInvariant();
        }
        catch
        {
            extension = string.Empty;
        }

        if (shortcutType == ShortcutType.Folder)
        {
            if (name.Contains("code") || name.Contains("cursor") || name.Contains("visual studio")) return 1;
            if (category.Contains("ターミナル")) return 2;
            if (name.Contains("explorer")) return 3;
        }

        if (shortcutType == ShortcutType.Url)
        {
            if (category.Contains("ブラウザ")) return 1;
        }

        if (shortcutType == ShortcutType.File)
        {
            if (extension is ".txt" or ".log" or ".md" or ".json" or ".xml" or ".csv")
            {
                if (name.Contains("code") || name.Contains("cursor") || name.Contains("notepad")) return 1;
            }

            if (extension is ".sln" or ".csproj" or ".vbproj")
            {
                if (name.Contains("visual studio")) return 1;
                if (name.Contains("code") || name.Contains("cursor")) return 2;
            }

            if (extension is ".xlsx" or ".xls" or ".xlsm" or ".csv")
            {
                if (name.Contains("excel")) return 1;
            }

            if (extension is ".docx" or ".doc")
            {
                if (name.Contains("word")) return 1;
            }

            if (extension is ".pptx" or ".ppt")
            {
                if (name.Contains("powerpoint")) return 1;
            }

            if (extension is ".zip" or ".7z" or ".rar")
            {
                if (name.Contains("7-zip")) return 1;
            }
        }

        if (category == "現在の設定") return 4;
        if (category == "推奨") return 5;
        if (category == "エディタ" || category == "開発" || category == "ブラウザ" || category == "office") return 10;
        if (category == "標準") return 20;
        if (category == "ターミナル") return 30;
        if (category == "インストール済み") return 40;

        return 50;
    }

    private static void AddKnownEditorCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen)
    {
        AddIfExists(candidates, seen, "Visual Studio Code", Expand(@"%LocalAppData%\Programs\Microsoft VS Code\Code.exe"), "エディタ", "フォルダやソースコードをVS Codeで開きます。");
        AddIfExists(candidates, seen, "Visual Studio Code", Expand(@"%ProgramFiles%\Microsoft VS Code\Code.exe"), "エディタ", "フォルダやソースコードをVS Codeで開きます。");
        AddIfExists(candidates, seen, "Visual Studio Code", Expand(@"%ProgramFiles(x86)%\Microsoft VS Code\Code.exe"), "エディタ", "フォルダやソースコードをVS Codeで開きます。");
        AddFromPath(candidates, seen, "Visual Studio Code", "Code.exe", "エディタ", "PATHから見つかったVS Codeです。");

        AddIfExists(candidates, seen, "Cursor", Expand(@"%LocalAppData%\Programs\Cursor\Cursor.exe"), "エディタ", "フォルダやソースコードをCursorで開きます。");
        AddIfExists(candidates, seen, "Cursor", Expand(@"%ProgramFiles%\Cursor\Cursor.exe"), "エディタ", "フォルダやソースコードをCursorで開きます。");
        AddIfExists(candidates, seen, "Cursor", Expand(@"%ProgramFiles(x86)%\Cursor\Cursor.exe"), "エディタ", "フォルダやソースコードをCursorで開きます。");
        AddFromPath(candidates, seen, "Cursor", "Cursor.exe", "エディタ", "PATHから見つかったCursorです。");

        AddIfExists(candidates, seen, "Notepad++", Expand(@"%ProgramFiles%\Notepad++\notepad++.exe"), "エディタ", "テキストやソースコードをNotepad++で開きます。");
        AddIfExists(candidates, seen, "Notepad++", Expand(@"%ProgramFiles(x86)%\Notepad++\notepad++.exe"), "エディタ", "テキストやソースコードをNotepad++で開きます。");

        var editions = new[] { "Community", "Professional", "Enterprise", "BuildTools" };
        foreach (var edition in editions)
        {
            AddIfExists(candidates, seen, "Visual Studio 2022 " + edition, Expand($@"%ProgramFiles%\Microsoft Visual Studio\2022\{edition}\Common7\IDE\devenv.exe"), "開発", "Visual Studioでソリューションやプロジェクトを開きます。");
            AddIfExists(candidates, seen, "Visual Studio 2019 " + edition, Expand($@"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\{edition}\Common7\IDE\devenv.exe"), "開発", "Visual Studioでソリューションやプロジェクトを開きます。");
        }
    }

    private static void AddKnownBrowserCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen)
    {
        AddIfExists(candidates, seen, "Google Chrome", Expand(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"), "ブラウザ", "URLをChromeで開きます。");
        AddIfExists(candidates, seen, "Google Chrome", Expand(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"), "ブラウザ", "URLをChromeで開きます。");
        AddIfExists(candidates, seen, "Microsoft Edge", Expand(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"), "ブラウザ", "URLをEdgeで開きます。");
        AddIfExists(candidates, seen, "Microsoft Edge", Expand(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"), "ブラウザ", "URLをEdgeで開きます。");
        AddIfExists(candidates, seen, "Mozilla Firefox", Expand(@"%ProgramFiles%\Mozilla Firefox\firefox.exe"), "ブラウザ", "URLをFirefoxで開きます。");
        AddIfExists(candidates, seen, "Mozilla Firefox", Expand(@"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"), "ブラウザ", "URLをFirefoxで開きます。");
    }

    private static void AddKnownOfficeCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen)
    {
        AddIfExists(candidates, seen, "Excel", Expand(@"%ProgramFiles%\Microsoft Office\root\Office16\EXCEL.EXE"), "Office", "ExcelファイルをExcelで開きます。");
        AddIfExists(candidates, seen, "Word", Expand(@"%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE"), "Office", "WordファイルをWordで開きます。");
        AddIfExists(candidates, seen, "PowerPoint", Expand(@"%ProgramFiles%\Microsoft Office\root\Office16\POWERPNT.EXE"), "Office", "PowerPointファイルをPowerPointで開きます。");
        AddIfExists(candidates, seen, "Excel", Expand(@"%ProgramFiles(x86)%\Microsoft Office\root\Office16\EXCEL.EXE"), "Office", "ExcelファイルをExcelで開きます。");
        AddIfExists(candidates, seen, "Word", Expand(@"%ProgramFiles(x86)%\Microsoft Office\root\Office16\WINWORD.EXE"), "Office", "WordファイルをWordで開きます。");
        AddIfExists(candidates, seen, "PowerPoint", Expand(@"%ProgramFiles(x86)%\Microsoft Office\root\Office16\POWERPNT.EXE"), "Office", "PowerPointファイルをPowerPointで開きます。");
    }

    private static void AddKnownToolCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen)
    {
        AddIfExists(candidates, seen, "Git Bash", Expand(@"%ProgramFiles%\Git\git-bash.exe"), "開発", "Git Bashで開きます。");
        AddIfExists(candidates, seen, "WinMerge", Expand(@"%ProgramFiles%\WinMerge\WinMergeU.exe"), "ツール", "比較ツールWinMergeで開きます。");
        AddIfExists(candidates, seen, "7-Zip File Manager", Expand(@"%ProgramFiles%\7-Zip\7zFM.exe"), "ツール", "圧縮ファイルを7-Zipで開きます。");
    }

    private static void AddRegistryAppPathCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen)
    {
        AddRegistryAppPathCandidates(candidates, seen, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
        AddRegistryAppPathCandidates(candidates, seen, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
        AddRegistryAppPathCandidates(candidates, seen, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths");
    }

    private static void AddRegistryAppPathCandidates(List<OpenApplicationCandidate> candidates, HashSet<string> seen, RegistryKey rootKey, string subKeyPath)
    {
        try
        {
            using var appPaths = rootKey.OpenSubKey(subKeyPath);
            if (appPaths is null)
            {
                return;
            }

            foreach (var subKeyName in appPaths.GetSubKeyNames())
            {
                using var subKey = appPaths.OpenSubKey(subKeyName);
                var rawPath = subKey?.GetValue(null)?.ToString();
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                var path = Expand(rawPath.Trim('"'));
                if (!File.Exists(path))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Path.GetFileNameWithoutExtension(subKeyName);
                }

                AddIfExists(candidates, seen, ToFriendlyName(name), path, GuessCategory(name, path), "Windowsに登録されているアプリです。");
            }
        }
        catch
        {
            // レジストリの読み取り権限や壊れたエントリがあっても、候補表示は継続します。
        }
    }

    private static string GuessCategory(string name, string path)
    {
        var text = $"{name} {path}".ToLowerInvariant();

        if (text.Contains("chrome") || text.Contains("edge") || text.Contains("firefox") || text.Contains("browser")) return "ブラウザ";
        if (text.Contains("code") || text.Contains("cursor") || text.Contains("notepad") || text.Contains("sakura") || text.Contains("emeditor")) return "エディタ";
        if (text.Contains("devenv") || text.Contains("visual studio") || text.Contains("git")) return "開発";
        if (text.Contains("excel") || text.Contains("winword") || text.Contains("powerpnt") || text.Contains("office")) return "Office";
        if (text.Contains("powershell") || text.Contains("cmd") || text.Contains("terminal") || text.Contains("wt.exe")) return "ターミナル";

        return "インストール済み";
    }

    private static string ToFriendlyName(string name)
    {
        return name switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "firefox" => "Mozilla Firefox",
            "Code" => "Visual Studio Code",
            "devenv" => "Visual Studio",
            "EXCEL" => "Excel",
            "WINWORD" => "Word",
            "POWERPNT" => "PowerPoint",
            _ => name
        };
    }

    private static void AddCurrentIfNeeded(List<OpenApplicationCandidate> candidates, HashSet<string> seen, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        var path = currentPath.Trim();
        var key = NormalizeKey(path);
        if (seen.Contains(key))
        {
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        candidates.Add(new OpenApplicationCandidate(name, path, "現在の設定", "現在このショートカットに設定されているアプリです。"));
        seen.Add(key);
    }

    private static void AddFromPath(List<OpenApplicationCandidate> candidates, HashSet<string> seen, string name, string exeName, string category, string description)
    {
        var found = FindExecutableInPath(exeName);
        if (!string.IsNullOrWhiteSpace(found))
        {
            AddIfExists(candidates, seen, name, found, category, description);
        }
    }

    private static void AddIfExists(List<OpenApplicationCandidate> candidates, HashSet<string> seen, string name, string path, string category, string description, bool isDefault = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        AddCommand(candidates, seen, name, path, category, description, isDefault);
    }

    private static void AddCommand(List<OpenApplicationCandidate> candidates, HashSet<string> seen, string name, string path, string category, string description, bool isDefault = false)
    {
        var key = NormalizeKey(path);
        if (seen.Contains(key))
        {
            return;
        }

        candidates.Add(new OpenApplicationCandidate(name, path, category, description, isDefault));
        seen.Add(key);
    }

    private static string NormalizeKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "<default>";
        }

        try
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch
        {
            // 入力中のパスなど正規化できないものは、そのまま比較キーにします。
        }

        return path.Trim();
    }

    private static string Expand(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string? FindExecutableInPath(string exeName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // PATHに不正な値が含まれていても候補表示を止めません。
            }
        }

        return null;
    }
}
