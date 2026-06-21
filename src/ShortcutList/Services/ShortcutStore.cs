using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ShortcutList.Models;

namespace ShortcutList.Services;

public class ShortcutStore
{
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private bool _isSavingBackup;

    public ObservableCollection<ShortcutItem> Items { get; } = new();
    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    public ObservableCollection<CommandItem> Commands { get; } = new();
    public ObservableCollection<OperationLogItem> Logs { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public string LastLoadMessage { get; private set; } = string.Empty;
    public bool WasRecoveredOnLoad { get; private set; }

    public string StorageFolder => _storageFolder;
    public string StoragePath => _storagePath;
    public string BackupFolder => Path.Combine(_storageFolder, "Backups");
    public string ShortcutsDataPath => Path.Combine(_storageFolder, "shortcuts.data.json");
    public string WorkspacesDataPath => Path.Combine(_storageFolder, "workspaces.data.json");
    public string CommandsDataPath => Path.Combine(_storageFolder, "commands.data.json");
    public string LogsDataPath => Path.Combine(_storageFolder, "logs.data.json");
    public string SettingsDataPath => Path.Combine(_storageFolder, "settings.data.json");

    public ShortcutStore()
    {
        _storageFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHub");
        Directory.CreateDirectory(_storageFolder);
        _storagePath = Path.Combine(_storageFolder, "shortcuts.json");
    }

    public void Load()
    {
        Items.Clear();
        Workspaces.Clear();
        Commands.Clear();
        Logs.Clear();
        LastLoadMessage = string.Empty;
        WasRecoveredOnLoad = false;

        if (!File.Exists(_storagePath))
        {
            LoadSeparatedDataFilesIfExists();
            MigrateIfNeeded();
            Save();
            return;
        }

        try
        {
            LoadFromData(ReadDataFromFile(_storagePath));
            LoadSeparatedDataFilesIfExists();
            MigrateIfNeeded();
        }
        catch (Exception ex)
        {
            LastLoadMessage = "設定ファイルの読み込みに失敗しました: " + ex.Message;

            if (TryRecoverFromBackup(out var recoveredFrom, out var recoverError))
            {
                WasRecoveredOnLoad = true;
                LastLoadMessage = $"設定ファイルをバックアップから復元しました。\n復元元: {recoveredFrom}";
                Save();
                return;
            }

            LastLoadMessage += "\nバックアップからの復元にも失敗しました: " + recoverError;
            Settings = new AppSettings();
            Save();
        }
    }

    private void LoadFromData(AppData data)
    {
        Items.Clear();
        Workspaces.Clear();
        Commands.Clear();
        Logs.Clear();

        Settings = data.Settings ?? new AppSettings();
        if (Settings.SchemaVersion <= 0) Settings.SchemaVersion = 1;

        var index = 0;
        foreach (var item in data.Items ?? new List<ShortcutItem>())
        {
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
            if (item.SortOrder <= 0) item.SortOrder = index;
            if (string.IsNullOrWhiteSpace(item.OpenApplicationPath))
            {
                item.OpenApplicationPath = ShortcutRunner.GetDefaultOpenApplicationPath(item.ShortcutType);
            }
            Items.Add(item);
            index++;
        }

        foreach (var workspace in data.Workspaces ?? new List<WorkspaceItem>())
        {
            if (string.IsNullOrWhiteSpace(workspace.Id)) workspace.Id = Guid.NewGuid().ToString("N");
            workspace.ShortcutIds = workspace.ShortcutIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Workspaces.Add(workspace);
        }


        foreach (var command in data.Commands ?? new List<CommandItem>())
        {
            if (string.IsNullOrWhiteSpace(command.Id)) command.Id = Guid.NewGuid().ToString("N");
            Commands.Add(command);
        }

        foreach (var log in data.Logs ?? new List<OperationLogItem>())
        {
            Logs.Add(log);
        }
    }

    private void MigrateIfNeeded()
    {
        Settings.SchemaVersion = Math.Max(Settings.SchemaVersion, 4);
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.GroupName)) item.GroupName = string.Empty;
            if (string.IsNullOrWhiteSpace(item.TagColor)) item.TagColor = "#CBD5E1";
        }
    }

    private bool TryRecoverFromBackup(out string recoveredFrom, out string error)
    {
        recoveredFrom = string.Empty;
        error = string.Empty;

        var candidates = new List<string>();

        var bak = _storagePath + ".bak";
        if (File.Exists(bak))
        {
            candidates.Add(bak);
        }

        if (Directory.Exists(BackupFolder))
        {
            candidates.AddRange(Directory.GetFiles(BackupFolder, "shortcuts_*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var data = ReadDataFromFile(candidate);
                LoadFromData(data);
                recoveredFrom = candidate;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        return false;
    }

    public void Save()
    {
        if (Settings.EnableAutoBackup && !_isSavingBackup && File.Exists(_storagePath))
        {
            CreateAutoBackup();
        }

        var data = CreateAppData();
        data.SchemaVersion = 4;
        data.Settings.SchemaVersion = 4;

        var json = JsonSerializer.Serialize(data, _options);

        if (Settings.EnableSafeSave)
        {
            SafeWrite(_storagePath, json);
        }
        else
        {
            File.WriteAllText(_storagePath, json, Encoding.UTF8);
        }

        if (Settings.EnableSeparatedDataFiles)
        {
            SaveSeparatedDataFiles();
        }
    }

    private void SafeWrite(string path, string json)
    {
        var tempPath = path + ".tmp";
        var bakPath = path + ".bak";

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _storageFolder);
        File.WriteAllText(tempPath, json, Encoding.UTF8);

        // 書き込み後にJSONとして読み直せることを確認してから置き換えます。
        _ = JsonSerializer.Deserialize<AppData>(File.ReadAllText(tempPath, Encoding.UTF8), _options)
            ?? throw new InvalidOperationException("保存検証に失敗しました。");

        if (File.Exists(path))
        {
            File.Copy(path, bakPath, overwrite: true);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }


    private void LoadSeparatedDataFilesIfExists()
    {
        try
        {
            if (File.Exists(SettingsDataPath))
            {
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsDataPath), _options) ?? Settings;
                Settings.SchemaVersion = Math.Max(Settings.SchemaVersion, 4);
            }

            if (File.Exists(ShortcutsDataPath))
            {
                Items.Clear();
                foreach (var item in JsonSerializer.Deserialize<List<ShortcutItem>>(File.ReadAllText(ShortcutsDataPath), _options) ?? new List<ShortcutItem>())
                {
                    if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                    Items.Add(item);
                }
            }

            if (File.Exists(WorkspacesDataPath))
            {
                Workspaces.Clear();
                foreach (var workspace in JsonSerializer.Deserialize<List<WorkspaceItem>>(File.ReadAllText(WorkspacesDataPath), _options) ?? new List<WorkspaceItem>())
                {
                    if (string.IsNullOrWhiteSpace(workspace.Id)) workspace.Id = Guid.NewGuid().ToString("N");
                    Workspaces.Add(workspace);
                }
            }

            if (File.Exists(CommandsDataPath))
            {
                Commands.Clear();
                foreach (var command in JsonSerializer.Deserialize<List<CommandItem>>(File.ReadAllText(CommandsDataPath), _options) ?? new List<CommandItem>())
                {
                    if (string.IsNullOrWhiteSpace(command.Id)) command.Id = Guid.NewGuid().ToString("N");
                    Commands.Add(command);
                }
            }

            if (File.Exists(LogsDataPath))
            {
                Logs.Clear();
                foreach (var log in JsonSerializer.Deserialize<List<OperationLogItem>>(File.ReadAllText(LogsDataPath), _options) ?? new List<OperationLogItem>())
                {
                    Logs.Add(log);
                }
            }
        }
        catch
        {
            // 分離データの読込に失敗した場合は、互換用のshortcuts.jsonに含まれるデータで起動します。
        }
    }

    private void SaveSeparatedDataFiles()
    {
        Directory.CreateDirectory(_storageFolder);
        File.WriteAllText(ShortcutsDataPath, JsonSerializer.Serialize(Items.ToList(), _options), Encoding.UTF8);
        File.WriteAllText(WorkspacesDataPath, JsonSerializer.Serialize(Workspaces.ToList(), _options), Encoding.UTF8);
        File.WriteAllText(CommandsDataPath, JsonSerializer.Serialize(Commands.ToList(), _options), Encoding.UTF8);
        File.WriteAllText(LogsDataPath, JsonSerializer.Serialize(Logs.ToList(), _options), Encoding.UTF8);
        File.WriteAllText(SettingsDataPath, JsonSerializer.Serialize(Settings, _options), Encoding.UTF8);
    }

    public void Add(ShortcutItem item)
    {
        if (item.SortOrder <= 0) item.SortOrder = Items.Count;
        Items.Add(item);
        Save();
    }

    public void Remove(ShortcutItem item)
    {
        Items.Remove(item);
        foreach (var workspace in Workspaces)
        {
            workspace.ShortcutIds.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        }
        Save();
    }

    public void AddWorkspace(WorkspaceItem workspace)
    {
        Workspaces.Add(workspace);
        Save();
    }

    public void RemoveWorkspace(WorkspaceItem workspace)
    {
        Workspaces.Remove(workspace);
        Save();
    }

    public void AddCommand(CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(command.Id)) command.Id = Guid.NewGuid().ToString("N");
        Commands.Add(command);
        Save();
    }

    public void RemoveCommand(CommandItem command)
    {
        Commands.Remove(command);
        Save();
    }

    public void AddLog(string category, string message, string detail = "", string level = "Info")
    {
        if (!Settings.EnableOperationLog) return;
        Logs.Insert(0, new OperationLogItem
        {
            CreatedAt = DateTime.Now,
            Category = category,
            Message = message,
            Detail = detail,
            Level = level
        });

        var keep = Math.Max(10, Settings.OperationLogKeepCount);
        while (Logs.Count > keep)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        Save();
    }

    public void ClearLogs()
    {
        Logs.Clear();
        Save();
    }

    public IReadOnlyList<ShortcutItem> GetWorkspaceItems(WorkspaceItem workspace)
    {
        return workspace.ShortcutIds
            .Select(id => Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x is not null)
            .Cast<ShortcutItem>()
            .ToList();
    }

    public bool ContainsTarget(string target, string? ignoreId = null)
    {
        return Items.Any(x =>
            !string.Equals(x.Id, ignoreId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeTarget(x.TargetPath), NormalizeTarget(target), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IGrouping<string, ShortcutItem>> GetDuplicateGroups()
    {
        return Items
            .GroupBy(x => NormalizeTarget(x.TargetPath), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .ToList();
    }

    public int RemoveDuplicateExtras()
    {
        var removeTargets = GetDuplicateGroups().SelectMany(g => g.Skip(1)).ToList();
        foreach (var item in removeTargets) Items.Remove(item);
        if (removeTargets.Count > 0) Save();
        return removeTargets.Count;
    }

    public void ExportToFile(string path) => File.WriteAllText(path, JsonSerializer.Serialize(CreateAppData(), _options));

    public void ExportShortcutsToFile(string path)
    {
        var data = new AppData { Items = Items.ToList(), Workspaces = Workspaces.ToList(), Commands = Commands.ToList(), Settings = new AppSettings() };
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public void ImportShortcutsFromFile(string path)
    {
        var data = ReadDataFromFile(path);
        Items.Clear();
        Workspaces.Clear();
        foreach (var item in data.Items ?? new List<ShortcutItem>()) Items.Add(item);
        foreach (var workspace in data.Workspaces ?? new List<WorkspaceItem>()) Workspaces.Add(workspace);
        Commands.Clear();
        foreach (var command in data.Commands ?? new List<CommandItem>()) Commands.Add(command);
        Save();
    }

    public void ExportSettingsToFile(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Settings, _options));

    public void ImportSettingsFromFile(string path)
    {
        var json = File.ReadAllText(path);
        Settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
        Settings.SchemaVersion = 4;
        Save();
    }

    public void ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,TargetPath,Arguments,OpenApplicationPath,OpenApplicationArguments,Type,IsFavorite,GroupName,Tags,TagColor,SortOrder");
        foreach (var item in Items)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(item.Name), Csv(item.TargetPath), Csv(item.Arguments), Csv(item.OpenApplicationPath), Csv(item.OpenApplicationArguments),
                Csv(item.ShortcutType.ToString()), Csv(item.IsFavorite.ToString()), Csv(item.GroupName), Csv(item.Tags), Csv(item.TagColor), Csv(item.SortOrder.ToString(CultureInfo.InvariantCulture))
            }));
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public int ImportCsv(string path, bool replace)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (replace) Items.Clear();
        var count = 0;
        foreach (var line in lines)
        {
            var columns = SplitCsv(line).ToList();
            if (columns.Count < 2) continue;
            var target = columns.ElementAtOrDefault(1) ?? string.Empty;
            var detected = ShortcutDetector.DetectType(target);
            if (detected is null) continue;
            if (!replace && ContainsTarget(target)) continue;
            Enum.TryParse(columns.ElementAtOrDefault(5), true, out ShortcutType type);
            if (!Enum.IsDefined(typeof(ShortcutType), type)) type = detected.Value;
            var item = new ShortcutItem
            {
                Name = string.IsNullOrWhiteSpace(columns.ElementAtOrDefault(0)) ? ShortcutDetector.GuessName(target) : columns[0],
                TargetPath = target,
                Arguments = columns.ElementAtOrDefault(2) ?? string.Empty,
                OpenApplicationPath = columns.ElementAtOrDefault(3) ?? ShortcutRunner.GetDefaultOpenApplicationPath(type),
                OpenApplicationArguments = columns.ElementAtOrDefault(4) ?? string.Empty,
                ShortcutType = type,
                IsFavorite = bool.TryParse(columns.ElementAtOrDefault(6), out var fav) && fav,
                GroupName = columns.ElementAtOrDefault(7) ?? string.Empty,
                Tags = columns.ElementAtOrDefault(8) ?? string.Empty,
                TagColor = columns.ElementAtOrDefault(9) ?? "#CBD5E1",
                SortOrder = int.TryParse(columns.ElementAtOrDefault(10), out var order) ? order : Items.Count,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            Items.Add(item);
            count++;
        }
        Save();
        return count;
    }

    public IEnumerable<string> GetGroups() => Items.Select(x => x.GroupDisplay).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);

    public IEnumerable<string> GetTags() => Items.SelectMany(x => x.GetTagList()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);

    public int RenameGroup(string oldName, string newName)
    {
        var count = 0;
        foreach (var item in Items.Where(x => string.Equals(x.GroupDisplay, oldName, StringComparison.OrdinalIgnoreCase)))
        {
            item.GroupName = newName;
            item.TouchUpdated();
            count++;
        }
        if (count > 0) Save();
        return count;
    }

    public int DeleteGroupName(string groupName)
    {
        var count = 0;
        foreach (var item in Items.Where(x => string.Equals(x.GroupDisplay, groupName, StringComparison.OrdinalIgnoreCase)))
        {
            item.GroupName = string.Empty;
            item.TouchUpdated();
            count++;
        }
        if (count > 0) Save();
        return count;
    }

    public IReadOnlyList<FileInfo> GetBackupFiles()
    {
        if (!Directory.Exists(BackupFolder)) return Array.Empty<FileInfo>();
        return Directory.GetFiles(BackupFolder, "shortcuts_*.json")
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();
    }

    public void RestoreFromBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("バックアップファイルが見つかりません。", backupPath);
        }

        CreateManualBackup();
        var data = ReadDataFromFile(backupPath);
        LoadFromData(data);
        MigrateIfNeeded();
        Save();
    }

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        Settings.SchemaVersion = 4;
        Save();
    }

    public void SaveChanges()
    {
        Save();
    }

    public string CreateManualBackup()
    {
        Directory.CreateDirectory(BackupFolder);
        var path = Path.Combine(BackupFolder, $"shortcuts_manual_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(CreateAppData(), _options));
        return path;
    }

    private void CreateAutoBackup()
    {
        try
        {
            _isSavingBackup = true;
            Directory.CreateDirectory(BackupFolder);
            var path = Path.Combine(BackupFolder, $"shortcuts_auto_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Copy(_storagePath, path, overwrite: true);
            CleanupOldBackups();
        }
        catch
        {
            // バックアップに失敗しても保存自体は止めません。
        }
        finally
        {
            _isSavingBackup = false;
        }
    }

    private void CleanupOldBackups()
    {
        var keepCount = Math.Max(1, Settings.AutoBackupKeepCount);
        var backups = Directory.GetFiles(BackupFolder, "shortcuts_*.json")
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.CreationTimeUtc)
            .Skip(keepCount)
            .ToList();
        foreach (var backup in backups)
        {
            try { backup.Delete(); } catch { }
        }
    }

    private AppData CreateAppData() => new()
    {
        SchemaVersion = 4,
        Items = Items.ToList(),
        Workspaces = Workspaces.ToList(),
        Commands = Commands.ToList(),
        Logs = Logs.ToList(),
        Settings = Settings
    };

    private AppData ReadDataFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppData>(json, _options) ?? new AppData();
    }

    private static string NormalizeTarget(string target)
    {
        return (target ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Csv(string? value)
    {
        var v = value ?? string.Empty;
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }

    private static IEnumerable<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
