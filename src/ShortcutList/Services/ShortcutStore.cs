using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ShortcutList.Models;

namespace ShortcutList.Services;

public class ShortcutStore
{
    // Ignore UI-derived properties so comparing snapshots never depends on file existence,
    // display culture, or other transient presentation state.
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    private readonly string _storageFolder;
    private readonly string _storagePath;
    private readonly object _saveLock = new();
    private AppData? _combinedCandidate;
    private AppData? _separatedCandidate;
    private Dictionary<string, string?>? _observedFiles;
    private bool _preserveBeforeSave;
    private bool _hasLoaded;

    public ObservableCollection<ShortcutItem> Items { get; } = new();
    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    public ObservableCollection<CommandItem> Commands { get; } = new();
    public ObservableCollection<OperationLogItem> Logs { get; } = new();
    public ObservableCollection<BookmarkSource> BookmarkSources { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public string LastLoadMessage { get; private set; } = string.Empty;
    public bool WasRecoveredOnLoad { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool CanUseCombinedData => _combinedCandidate is not null;
    public bool CanUseSeparatedData => _separatedCandidate is not null;
    public string StorageConflictSummary { get; private set; } = string.Empty;

    public string StorageFolder => _storageFolder;
    public string StoragePath => _storagePath;
    public string BackupFolder => Path.Combine(_storageFolder, "Backups");
    public string ShortcutsDataPath => Path.Combine(_storageFolder, "shortcuts.data.json");
    public string WorkspacesDataPath => Path.Combine(_storageFolder, "workspaces.data.json");
    public string CommandsDataPath => Path.Combine(_storageFolder, "commands.data.json");
    public string LogsDataPath => Path.Combine(_storageFolder, "logs.data.json");
    public string SettingsDataPath => Path.Combine(_storageFolder, "settings.data.json");

    public ShortcutStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHub")) { }

    /// <summary>Use a separate root for tests or an explicitly selected data directory.</summary>
    public ShortcutStore(string storageFolder)
    {
        if (string.IsNullOrWhiteSpace(storageFolder)) throw new ArgumentException("保存先が空です。", nameof(storageFolder));
        _storageFolder = Path.GetFullPath(storageFolder);
        _storagePath = Path.Combine(_storageFolder, "shortcuts.json");
    }

    public void Load()
    {
        lock (_saveLock)
        {
            _hasLoaded = true;
            LastLoadMessage = string.Empty;
            StorageConflictSummary = string.Empty;
            WasRecoveredOnLoad = false;
            IsReadOnly = false;
            _combinedCandidate = null;
            _separatedCandidate = null;
            _preserveBeforeSave = false;
            _observedFiles = null;
            try
            {
                AppData? data = null;
                string mainError = string.Empty;
                try
                {
                    if (StorageFile.Exists(_storagePath)) data = ReadDataFromFile(_storagePath);
                }
                catch (Exception ex) when (IsStorageException(ex)) { mainError = ex.Message; }

                if (data is null)
                {
                    if (TryRecoverFromBackup(out var recovered, out var recoveredFrom, out var recoverError))
                    {
                        data = recovered;
                        WasRecoveredOnLoad = true;
                        _preserveBeforeSave = true;
                        LastLoadMessage = $"バックアップから読み取り回復しました。元ファイルは変更していません。\n復元元: {recoveredFrom}";
                    }
                    else if (mainError.Length != 0 || StorageFile.Exists(_storagePath))
                    {
                        throw new InvalidDataException($"設定ファイルを読み込めません。{mainError}\n有効なバックアップがありません。{recoverError}");
                    }
                    else
                    {
                        // Only a genuinely empty data directory is a new installation.
                        // A leftover backup/tmp/split is evidence of an interrupted or existing store.
                        var split = ReadSeparatedSnapshot(null);
                        if (split is not null)
                        {
                            data = split;
                            WasRecoveredOnLoad = true;
                            _preserveBeforeSave = true;
                            LastLoadMessage = "統合ファイルがないため、検証済みの分離ファイルから読み取り回復しました。元ファイルは変更していません。";
                        }
                        else if (HasStorageEvidence())
                        {
                            throw new InvalidDataException("既存データの痕跡がありますが、有効な保存データを読み込めません。空データで上書きしません。");
                        }
                        else data = new AppData();
                    }
                }

                if (data is null) throw new InvalidDataException("有効な保存データがありません。");
                _combinedCandidate = Clone(data);
                // The preference in the selected combined/backup snapshot is authoritative for
                // whether split files may be read at all. A stale settings.data.json cannot enable it.
                if (data.Settings.EnableSeparatedDataFiles)
                {
                    try
                    {
                        _separatedCandidate = ReadSeparatedSnapshot(data);
                        if (_separatedCandidate is not null && !SameSnapshot(data, _separatedCandidate))
                        {
                            MarkConflict("統合ファイルと分離ファイルの内容が一致しません。統合側の内容を表示しています。保存を停止しました。");
                        }
                    }
                    catch (Exception ex) when (IsStorageException(ex))
                    {
                        MarkConflict("分離ファイルの検証に失敗しました。統合側の内容を表示しています。保存を停止しました。\n" + ex.Message);
                    }
                }
                LoadFromData(data);
                _observedFiles = CaptureFiles(data.Settings.EnableSeparatedDataFiles);
            }
            catch (Exception ex) when (IsStorageException(ex))
            {
                // Do not clear live collections here: Reload must retain the last usable view.
                IsReadOnly = true;
                LastLoadMessage = "読み取り専用で起動します。ファイルは変更していません。\n" + ex.Message;
                StorageConflictSummary = LastLoadMessage;
            }
        }
    }

    private void MarkConflict(string message)
    {
        IsReadOnly = true;
        StorageConflictSummary = message + $"\n統合: {SnapshotCounts(_combinedCandidate)}\n分離: {SnapshotCounts(_separatedCandidate)}";
        if (_combinedCandidate is not null && _separatedCandidate is not null)
            StorageConflictSummary += "\n" + StorageSnapshotDiff.Describe(_combinedCandidate, _separatedCandidate, _options);
        LastLoadMessage = string.IsNullOrEmpty(LastLoadMessage) ? StorageConflictSummary : LastLoadMessage + "\n" + StorageConflictSummary;
    }

    private static string SnapshotCounts(AppData? data) => data is null ? "利用できません" : $"登録 {data.Items.Count}、作業セット {data.Workspaces.Count}、コマンド {data.Commands.Count}";

    private void LoadFromData(AppData data)
    {
        StorageIntegrity.Validate(data);
        // Validation and cloning are complete before any observable collection is changed.
        var staged = Clone(data);
        Items.Clear();
        Workspaces.Clear();
        Commands.Clear();
        Logs.Clear();
        BookmarkSources.Clear();
        Settings = staged.Settings;
        foreach (var item in staged.Items) Items.Add(item);
        foreach (var workspace in staged.Workspaces) Workspaces.Add(workspace);
        foreach (var command in staged.Commands) Commands.Add(command);
        foreach (var log in staged.Logs) Logs.Add(log);
        foreach (var source in staged.BookmarkSources) BookmarkSources.Add(source);
    }

    private bool TryRecoverFromBackup(out AppData? recovered, out string recoveredFrom, out string error)
    {
        recovered = null;
        recoveredFrom = string.Empty;
        error = string.Empty;
        var candidates = new List<string>();
        if (StorageFile.Exists(_storagePath + ".bak")) candidates.Add(_storagePath + ".bak");
        if (Directory.Exists(BackupFolder))
            candidates.AddRange(Directory.GetFiles(BackupFolder, "shortcuts_*.json").OrderByDescending(File.GetLastWriteTimeUtc));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                recovered = ReadDataFromFile(candidate);
                recoveredFrom = candidate;
                return true;
            }
            catch (Exception ex) when (IsStorageException(ex)) { error = ex.Message; }
        }
        return false;
    }

    public void Save()
    {
        lock (_saveLock)
        {
            EnsureWritable();
            VerifyFilesUnchanged();
            VerifySeparatedModeChange();
            // Serialize once to detach the full snapshot from UI objects. Every output is
            // derived from this same validated snapshot, including the separate files.
            var data = Clone(CreateAppData());
            StorageIntegrity.Validate(data);
            var json = JsonSerializer.Serialize(data, _options);
            Directory.CreateDirectory(_storageFolder);
            if (_preserveBeforeSave) PreserveRawFiles();
            var currentIsValid = IsValidDataFile(_storagePath);
            if (Settings.EnableAutoBackup && currentIsValid) CreateAutoBackup();
            try
            {
                StorageFile.AtomicWrite(_storagePath, json, value => ReadData(value), currentIsValid);
                if (data.Settings.EnableSeparatedDataFiles) SaveSeparatedDataFiles(data);
                _observedFiles = CaptureFiles(data.Settings.EnableSeparatedDataFiles);
                _combinedCandidate = Clone(data);
                _separatedCandidate = data.Settings.EnableSeparatedDataFiles ? Clone(data) : null;
                _preserveBeforeSave = false;
            }
            catch
            {
                // ReplaceFile failures may have moved the old destination to .bak. Keep
                // all evidence and force a reload/reconciliation before another write.
                IsReadOnly = true;
                LastLoadMessage = "保存が完了したか確認できません。現在の編集内容は保持しています。再読込または復元で確認してください。";
                throw;
            }
        }
    }

    private void EnsureWritable()
    {
        if (IsReadOnly) throw new InvalidOperationException("データ保護のため読み取り専用です。元データを確認し、復元または分離データの選択を行ってください。");
        if (!_hasLoaded)
        {
            if (HasStorageEvidence())
                throw new InvalidOperationException("既存データを保存する前に Load で読み込んでください。");
            _observedFiles = CaptureFiles(Settings.EnableSeparatedDataFiles);
            _hasLoaded = true;
        }
    }

    private void VerifySeparatedModeChange()
    {
        if (!Settings.EnableSeparatedDataFiles || _combinedCandidate is null || _combinedCandidate.Settings.EnableSeparatedDataFiles) return;
        // Enabling split storage must not overwrite historical/external files without a
        // choice. They were intentionally not read while the option was disabled.
        try
        {
            var proposed = Clone(CreateAppData());
            _separatedCandidate = ReadSeparatedSnapshot(proposed);
            if (_separatedCandidate is null || SameSnapshot(proposed, _separatedCandidate)) return;
            _combinedCandidate = proposed;
            _observedFiles = CaptureFiles(true);
            MarkConflict("分離保存を有効にする前に、既存の分離ファイルとの違いを確認してください。");
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            _combinedCandidate = Clone(CreateAppData());
            _observedFiles = CaptureFiles(true);
            MarkConflict("既存の分離ファイルを検証できません。保存前に統合側を使うか確認してください。\n" + ex.Message);
        }
        throw new InvalidOperationException(StorageConflictSummary);
    }

    private AppData? ReadSeparatedSnapshot(AppData? basis)
    {
        if (basis is not null && !basis.Settings.EnableSeparatedDataFiles) return null;
        var paths = SplitPaths();
        var present = paths.Select(StorageFile.Exists).ToArray();
        if (!present.Any(x => x)) return null;
        if (present.Any(x => !x)) throw new InvalidDataException("分離ファイルの一部が不足しています。混在した内容は取り込みません。");
        var staged = basis is null ? new AppData() : Clone(basis);
        // Parse all five into a detached snapshot; no live collection is cleared on failure.
        staged.Settings = ReadComponent<AppSettings>(SettingsDataPath);
        if (!staged.Settings.EnableSeparatedDataFiles && basis is null)
            throw new InvalidDataException("分離保存が無効な設定です。統合ファイルまたはバックアップから復元してください。");
        staged.Items = ReadComponent<List<ShortcutItem>>(ShortcutsDataPath);
        staged.Workspaces = ReadComponent<List<WorkspaceItem>>(WorkspacesDataPath);
        staged.Commands = ReadComponent<List<CommandItem>>(CommandsDataPath);
        staged.Logs = ReadComponent<List<OperationLogItem>>(LogsDataPath);
        StorageIntegrity.Validate(staged);
        return staged;
    }

    private T ReadComponent<T>(string path) where T : class => JsonSerializer.Deserialize<T>(File.ReadAllText(path), _options)
        ?? throw new InvalidDataException($"空の分離データです: {Path.GetFileName(path)}");

    private void SaveSeparatedDataFiles(AppData data)
    {
        WriteComponent(ShortcutsDataPath, data.Items);
        WriteComponent(WorkspacesDataPath, data.Workspaces);
        WriteComponent(CommandsDataPath, data.Commands);
        WriteComponent(LogsDataPath, data.Logs);
        WriteComponent(SettingsDataPath, data.Settings);
    }

    private void WriteComponent<T>(string path, T value) where T : class
    {
        StorageFile.AtomicWrite(path, JsonSerializer.Serialize(value, _options), json =>
            JsonSerializer.Deserialize<T>(json, _options) ?? throw new InvalidDataException("分離データの保存検証に失敗しました。"), false);
    }

    /// <summary>Explicitly choose a validated source after displaying StorageConflictSummary.</summary>
    public void ResolveSeparatedDataConflict(bool useSeparated)
    {
        lock (_saveLock)
        {
            var candidate = useSeparated ? _separatedCandidate : _combinedCandidate;
            if (candidate is null) throw new InvalidOperationException("選択したデータは検証できていません。");
            var selected = Clone(candidate);
            if (_combinedCandidate is not null) selected.Settings.EnableSeparatedDataFiles = _combinedCandidate.Settings.EnableSeparatedDataFiles;
            VerifyFilesUnchanged();
            PreserveRawFiles();
            LoadFromData(selected);
            _combinedCandidate = Clone(selected);
            IsReadOnly = false;
            _preserveBeforeSave = false;
            try { Save(); }
            catch { IsReadOnly = true; throw; }
            LastLoadMessage = "元ファイルを保全して、選択したデータを保存しました。";
            StorageConflictSummary = string.Empty;
        }
    }

    private string[] SplitPaths() => new[] { ShortcutsDataPath, WorkspacesDataPath, CommandsDataPath, LogsDataPath, SettingsDataPath };

    private Dictionary<string, string?> CaptureFiles(bool includeSplit)
    {
        var paths = new[] { _storagePath }.Concat(includeSplit ? SplitPaths() : Array.Empty<string>());
        return paths.ToDictionary(path => path, StorageFile.Fingerprint, StringComparer.Ordinal);
    }

    private void VerifyFilesUnchanged()
    {
        if (_observedFiles is null) return;
        foreach (var pair in _observedFiles)
            if (!string.Equals(pair.Value, StorageFile.Fingerprint(pair.Key), StringComparison.Ordinal))
            {
                IsReadOnly = true;
                throw new InvalidOperationException("起動後に保存ファイルが変更されました。上書きを避けるため、再読込して内容を確認してください。");
            }
    }

    private bool HasStorageEvidence()
    {
        if (!Directory.Exists(_storageFolder)) return false;
        return Directory.EnumerateFiles(_storageFolder).Any(path =>
            Path.GetFileName(path).StartsWith("shortcuts", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Contains(".data.json", StringComparison.OrdinalIgnoreCase)) ||
            (Directory.Exists(BackupFolder) && Directory.EnumerateFiles(BackupFolder).Any());
    }

    private void PreserveRawFiles()
    {
        if (!Directory.Exists(_storageFolder)) return;
        VerifyFilesUnchanged();
        var original = Directory.EnumerateFiles(_storageFolder).ToDictionary(path => path, StorageFile.Fingerprint, StringComparer.Ordinal);
        var destination = Path.Combine(_storageFolder, "PreservedSnapshots", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff") + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        foreach (var file in original)
        {
            var preserved = Path.Combine(destination, Path.GetFileName(file.Key));
            File.Copy(file.Key, preserved, overwrite: false);
            if (!string.Equals(file.Value, StorageFile.Fingerprint(preserved), StringComparison.Ordinal))
                throw new IOException("保全ファイルの検証に失敗しました。元ファイルは上書きしません。");
        }
        var current = Directory.EnumerateFiles(_storageFolder).ToDictionary(path => path, StorageFile.Fingerprint, StringComparer.Ordinal);
        if (current.Count != original.Count || original.Any(file => !current.TryGetValue(file.Key, out var hash) || hash != file.Value))
            throw new IOException("保全中に元ファイルが変更されました。再読込して確認してください。");
        VerifyFilesUnchanged();
        // Receipt makes the verified preservation inspectable without altering source files.
        StorageFile.AtomicWrite(Path.Combine(destination, "preservation-manifest.json"),
            JsonSerializer.Serialize(original.ToDictionary(file => Path.GetFileName(file.Key), file => file.Value), _options),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? throw new InvalidDataException("保全記録が空です。"), false);
    }

    private bool SameSnapshot(AppData a, AppData b) => JsonSerializer.Serialize(a, _options) == JsonSerializer.Serialize(b, _options);
    private AppData Clone(AppData data) => ReadData(JsonSerializer.Serialize(data, _options));
    private bool IsValidDataFile(string path)
    {
        try { if (!StorageFile.Exists(path)) return false; _ = ReadDataFromFile(path); return true; }
        catch (Exception ex) when (IsStorageException(ex)) { return false; }
    }
    private static bool IsStorageException(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException;

    public void Add(ShortcutItem item)
    {
        EnsureWritable();
        if (item.SortOrder <= 0) item.SortOrder = Items.Count;
        var candidate = CreateAppData();
        candidate.Items.Add(item);
        StorageIntegrity.Validate(candidate);
        Items.Add(item);
        Save();
    }

    public void Remove(ShortcutItem item)
    {
        EnsureWritable();
        Items.Remove(item);
        foreach (var workspace in Workspaces)
        {
            workspace.ShortcutIds.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        }
        Save();
    }

    public void AddWorkspace(WorkspaceItem workspace)
    {
        EnsureWritable();
        var candidate = CreateAppData();
        candidate.Workspaces.Add(workspace);
        StorageIntegrity.Validate(candidate);
        Workspaces.Add(workspace);
        Save();
    }

    public void RemoveWorkspace(WorkspaceItem workspace)
    {
        EnsureWritable();
        Workspaces.Remove(workspace);
        Save();
    }

    public void AddCommand(CommandItem command)
    {
        EnsureWritable();
        if (string.IsNullOrWhiteSpace(command.Id)) command.Id = Guid.NewGuid().ToString("N");
        var candidate = CreateAppData();
        candidate.Commands.Add(command);
        StorageIntegrity.Validate(candidate);
        Commands.Add(command);
        Save();
    }

    public void RemoveCommand(CommandItem command)
    {
        EnsureWritable();
        Commands.Remove(command);
        Save();
    }

    public void AddLog(string category, string message, string detail = "", string level = "Info", bool save = true)
    {
        EnsureWritable();
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

        if (save) Save();
    }

    public void ClearLogs()
    {
        EnsureWritable();
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
        // This helper is only a target lookup, not a duplicate-launch-recipe decision.
        return Items.Any(x => !string.Equals(x.Id, ignoreId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.TargetPath, target, StringComparison.Ordinal));
    }

    public bool ContainsEquivalent(ShortcutItem item, string? ignoreId = null) => Items.Any(x =>
        !string.Equals(x.Id, ignoreId, StringComparison.OrdinalIgnoreCase) && RecipeKey(x) == RecipeKey(item));

    private static string RecipeKey(ShortcutItem item) => JsonSerializer.Serialize(new[]
    {
        item.ShortcutType.ToString(), item.TargetPath, item.Arguments,
        item.OpenApplicationPath, item.OpenApplicationArguments
    });

    private static string DuplicateKey(ShortcutItem item) => JsonSerializer.Serialize(new[]
    {
        RecipeKey(item), item.Name, item.GroupName, item.Tags, item.TagColor,
        item.Memo, item.IsFavorite.ToString()
    });

    public IReadOnlyList<IGrouping<string, ShortcutItem>> GetDuplicateGroups() => Items
        .Where(x => !string.IsNullOrWhiteSpace(x.TargetPath))
        .GroupBy(DuplicateKey, StringComparer.Ordinal)
        .Where(g => g.Count() > 1)
        .ToList();

    public int RemoveDuplicateExtras()
    {
        EnsureWritable();
        StorageIntegrity.Validate(CreateAppData());
        var groups = GetDuplicateGroups();
        var count = 0;
        foreach (var group in groups)
        {
            var retained = group.First();
            foreach (var duplicate in group.Skip(1))
            {
                foreach (var workspace in Workspaces)
                    workspace.ShortcutIds = workspace.ShortcutIds
                        .Select(id => string.Equals(id, duplicate.Id, StringComparison.OrdinalIgnoreCase) ? retained.Id : id)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                // Keep historical information when removing an explicitly selected exact duplicate.
                retained.LaunchCount = Math.Max(retained.LaunchCount, duplicate.LaunchCount);
                retained.CreatedAt = retained.CreatedAt < duplicate.CreatedAt ? retained.CreatedAt : duplicate.CreatedAt;
                retained.UpdatedAt = retained.UpdatedAt > duplicate.UpdatedAt ? retained.UpdatedAt : duplicate.UpdatedAt;
                if (duplicate.LastLaunchedAt.HasValue && (!retained.LastLaunchedAt.HasValue || duplicate.LastLaunchedAt > retained.LastLaunchedAt))
                    retained.LastLaunchedAt = duplicate.LastLaunchedAt;
                Items.Remove(duplicate);
                count++;
            }
        }
        if (count > 0) Save();
        return count;
    }

    public void ExportToFile(string path) => File.WriteAllText(path, JsonSerializer.Serialize(CreateAppData(), _options));

    public void ExportShortcutsToFile(string path)
    {
        var data = new AppData { Items = Items.ToList(), Workspaces = Workspaces.ToList(), Commands = Commands.ToList(), BookmarkSources = BookmarkSources.ToList(), Settings = new AppSettings() };
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public void ImportShortcutsFromFile(string path)
    {
        EnsureWritable();
        var imported = ReadDataFromFile(path);
        var staged = Clone(CreateAppData());
        staged.Items = imported.Items;
        staged.Workspaces = imported.Workspaces;
        staged.Commands = imported.Commands;
        staged.BookmarkSources = imported.BookmarkSources;
        StorageIntegrity.Validate(staged);
        LoadFromData(staged);
        Save();
    }

    public void ExportSettingsToFile(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Settings, _options));

    public void ImportSettingsFromFile(string path)
    {
        EnsureWritable();
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, _options) ?? throw new InvalidDataException("設定が空です。");
        ReplaceSettings(settings);
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
        EnsureWritable();
        var lines = File.ReadAllLines(path, Encoding.UTF8).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var importedItems = replace ? new List<ShortcutItem>() : Items.ToList();
        var count = 0;
        foreach (var line in lines)
        {
            var columns = SplitCsv(line).ToList();
            if (columns.Count < 2) continue;
            var target = columns.ElementAtOrDefault(1) ?? string.Empty;
            var detected = ShortcutDetector.DetectType(target);
            if (detected is null) continue;
            if (!Enum.TryParse(columns.ElementAtOrDefault(5), true, out ShortcutType type) || !Enum.IsDefined(typeof(ShortcutType), type)) type = detected.Value;
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
            if (!replace && importedItems.Any(existing => DuplicateKey(existing) == DuplicateKey(item))) continue;
            importedItems.Add(item);
            count++;
        }
        var staged = Clone(CreateAppData());
        staged.Items = importedItems;
        StorageIntegrity.Validate(staged);
        LoadFromData(staged);
        Save();
        return count;
    }

    public IEnumerable<string> GetGroups() => Items.Select(x => x.GroupDisplay).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);

    public IEnumerable<string> GetTags() => Items.SelectMany(x => x.GetTagList()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x);

    public int RenameGroup(string oldName, string newName)
    {
        EnsureWritable();
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
        EnsureWritable();
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
        lock (_saveLock)
        {
            var data = ReadDataFromFile(backupPath); // Validate before changing or archiving anything.
            VerifyFilesUnchanged();
            PreserveRawFiles();
            LoadFromData(data);
            _combinedCandidate = Clone(data);
            IsReadOnly = false;
            _preserveBeforeSave = false;
            try { Save(); }
            catch { IsReadOnly = true; throw; }
            WasRecoveredOnLoad = true;
            LastLoadMessage = $"元ファイルを保全してバックアップを復元しました。\n復元元: {backupPath}";
            StorageConflictSummary = string.Empty;
        }
    }

    public void ReplaceSettings(AppSettings settings)
    {
        EnsureWritable();
        var staged = Clone(CreateAppData());
        staged.Settings = settings;
        StorageIntegrity.Validate(staged);
        Settings = Clone(staged).Settings;
        Save();
    }

    public void SaveChanges()
    {
        Save();
    }

    public string CreateManualBackup()
    {
        var data = Clone(CreateAppData());
        Directory.CreateDirectory(BackupFolder);
        var path = Path.Combine(BackupFolder, $"shortcuts_manual_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json");
        StorageFile.AtomicWrite(path, JsonSerializer.Serialize(data, _options), ReadData, false);
        return path;
    }

    private void CreateAutoBackup()
    {
        // Never promote an unvalidated/corrupt main file over a known good backup.
        var json = File.ReadAllText(_storagePath);
        _ = ReadData(json);
        Directory.CreateDirectory(BackupFolder);
        var path = Path.Combine(BackupFolder, $"shortcuts_auto_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json");
        StorageFile.AtomicWrite(path, json, ReadData, false);
        CleanupOldBackups();
    }

    private void CleanupOldBackups()
    {
        var keepCount = Math.Max(1, Settings.AutoBackupKeepCount);
        // Manual backups and preserved raw snapshots are never automatic retention targets.
        var backups = Directory.GetFiles(BackupFolder, "shortcuts_auto_*.json")
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ThenByDescending(x => x.Name, StringComparer.Ordinal)
            .Skip(keepCount).ToList();
        foreach (var backup in backups)
        {
            try { backup.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private AppData CreateAppData() => new()
    {
        SchemaVersion = 4,
        Items = Items.ToList(),
        Workspaces = Workspaces.ToList(),
        Commands = Commands.ToList(),
        Logs = Logs.ToList(),
        BookmarkSources = BookmarkSources.ToList(),
        Settings = Settings
    };

    private AppData ReadDataFromFile(string path) => ReadData(File.ReadAllText(path));

    private AppData ReadData(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(nameof(AppData.Items), out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("登録一覧を含む有効な保存データではありません。");
        var data = JsonSerializer.Deserialize<AppData>(json, _options) ?? throw new InvalidDataException("保存データが空です。");
        StorageIntegrity.Validate(data);
        return data;
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

