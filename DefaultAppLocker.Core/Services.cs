using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DefaultAppLocker.Core;

public interface IDefaultAppScanner
{
    DefaultAppSnapshot ScanCurrentUser();
}

public sealed class RegistryDefaultAppScanner : IDefaultAppScanner
{
    private static readonly string[] Protocols =
    [
        "http", "https", "mailto", "ftp", "ms-settings", "msteams", "tel", "callto", "webcal"
    ];

    public DefaultAppSnapshot ScanCurrentUser()
    {
        var associations = new Dictionary<string, AppAssociation>(StringComparer.OrdinalIgnoreCase);
        ScanFileExtensions(associations);
        ScanProtocols(associations);
        return new DefaultAppSnapshot
        {
            Associations = associations.Values
                .OrderBy(a => a.Kind)
                .ThenBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void ScanFileExtensions(Dictionary<string, AppAssociation> associations)
    {
        using var extRoot = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
        if (extRoot is null) return;

        foreach (var ext in extRoot.GetSubKeyNames().Where(n => n.StartsWith(".", StringComparison.Ordinal)))
        {
            var progId = ReadUserChoiceProgId($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
            if (string.IsNullOrWhiteSpace(progId)) continue;

            var normalized = AppAssociation.NormalizeExtension(ext);
            associations[$"file:{normalized}"] = new AppAssociation
            {
                Identifier = normalized,
                Kind = AssociationKind.FileExtension,
                ProgId = progId,
                ApplicationName = ResolveApplicationName(progId)
            };
        }
    }

    private static void ScanProtocols(Dictionary<string, AppAssociation> associations)
    {
        foreach (var protocol in Protocols)
        {
            var progId = ReadUserChoiceProgId($@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{protocol}\UserChoice");
            if (string.IsNullOrWhiteSpace(progId)) continue;

            associations[$"protocol:{protocol.ToLowerInvariant()}"] = new AppAssociation
            {
                Identifier = protocol.ToLowerInvariant(),
                Kind = AssociationKind.Protocol,
                ProgId = progId,
                ApplicationName = ResolveApplicationName(progId)
            };
        }
    }

    private static string? ReadUserChoiceProgId(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue("ProgId") as string;
    }

    private static string? ResolveApplicationName(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
        var name = key?.GetValue("ApplicationName") as string;
        if (!string.IsNullOrWhiteSpace(name)) return name;
        using var root = Registry.ClassesRoot.OpenSubKey(progId);
        return root?.GetValue(null) as string;
    }
}

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string RootDirectory { get; }
    public string SnapshotsDirectory => Path.Combine(RootDirectory, "Snapshots");
    public string SnapshotProfilesDirectory => Path.Combine(RootDirectory, "SnapshotProfiles");
    public string QuickProfilesDirectory => Path.Combine(RootDirectory, "QuickProfiles");
    public string LogsDirectory => Path.Combine(RootDirectory, "Logs");
    public string ConfigPath => Path.Combine(RootDirectory, "Config.json");
    public string DefaultSnapshotPath => Path.Combine(SnapshotsDirectory, "Default.json");

    public ConfigurationStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Environment.GetEnvironmentVariable("DEFAULTAPPLOCKER_CONFIG_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DefaultAppLocker");
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(SnapshotsDirectory);
        Directory.CreateDirectory(SnapshotProfilesDirectory);
        Directory.CreateDirectory(QuickProfilesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath)) return new AppConfig();
        var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions), Encoding.UTF8);
    }

    public void SaveSnapshot(DefaultAppSnapshot snapshot, string? path = null)
    {
        var target = path ?? DefaultSnapshotPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, JsonSerializer.Serialize(snapshot, JsonOptions), Encoding.UTF8);
    }

    public DefaultAppSnapshot LoadSnapshot(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<DefaultAppSnapshot>(json, JsonOptions) ?? new DefaultAppSnapshot();
    }

    public string CreateDatedSnapshotPath()
    {
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(SnapshotsDirectory, $"Snapshot-{stamp}.json");
    }

    public SnapshotProfile CreateSnapshotProfile(DefaultAppSnapshot snapshot, string? alias = null)
    {
        var profile = new SnapshotProfile
        {
            Alias = string.IsNullOrWhiteSpace(alias) ? $"快照 {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}" : alias.Trim(),
            Snapshot = snapshot
        };
        SaveSnapshotProfile(profile);
        return profile;
    }

    public List<SnapshotProfile> LoadSnapshotProfiles()
    {
        return LoadProfiles<SnapshotProfile>(SnapshotProfilesDirectory)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public void SaveSnapshotProfile(SnapshotProfile profile)
    {
        SaveProfile(SnapshotProfilesDirectory, profile.Id, profile);
    }

    public void DeleteSnapshotProfile(SnapshotProfile profile)
    {
        DeleteProfile(SnapshotProfilesDirectory, profile.Id);
    }

    public QuickTemplateProfile CreateQuickTemplateProfile(IEnumerable<OverrideAssociation> overrides, string? alias = null)
    {
        var list = overrides.ToList();
        var profile = new QuickTemplateProfile
        {
            Alias = string.IsNullOrWhiteSpace(alias) ? $"默认应用模板方案 {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}" : alias.Trim(),
            Overrides = list
        };
        SaveQuickTemplateProfile(profile);
        return profile;
    }

    public List<QuickTemplateProfile> LoadQuickTemplateProfiles()
    {
        return LoadProfiles<QuickTemplateProfile>(QuickProfilesDirectory)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public void SaveQuickTemplateProfile(QuickTemplateProfile profile)
    {
        SaveProfile(QuickProfilesDirectory, profile.Id, profile);
    }

    public void DeleteQuickTemplateProfile(QuickTemplateProfile profile)
    {
        DeleteProfile(QuickProfilesDirectory, profile.Id);
    }

    public void ExportPackage(string path, bool includeSnapshots = true, bool includeTemplateProfiles = true)
    {
        var package = new DefaultAppLockerExportPackage
        {
            SnapshotProfiles = includeSnapshots ? LoadSnapshotProfiles() : [],
            TemplateProfiles = includeTemplateProfiles ? LoadQuickTemplateProfiles() : []
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(package, JsonOptions), Encoding.UTF8);
    }

    public (int Snapshots, int TemplateProfiles) ImportPackage(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var package = JsonSerializer.Deserialize<DefaultAppLockerExportPackage>(json, JsonOptions)
            ?? throw new InvalidOperationException("导入文件不是有效的 DefaultAppLocker 配置包。");
        var snapshots = 0;
        var templates = 0;
        foreach (var snapshot in package.SnapshotProfiles)
        {
            SaveSnapshotProfile(snapshot);
            snapshots++;
        }
        foreach (var profile in package.TemplateProfiles)
        {
            SaveQuickTemplateProfile(profile);
            templates++;
        }
        return (snapshots, templates);
    }
    public void AppendLog(string message)
    {
        var path = Path.Combine(LogsDirectory, DateTimeOffset.Now.ToString("yyyyMMdd") + ".log");
        File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}", Encoding.UTF8);
    }

    private static List<T> LoadProfiles<T>(string directory)
    {
        Directory.CreateDirectory(directory);
        var result = new List<T>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file, Encoding.UTF8);
                var item = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (item is not null) result.Add(item);
            }
            catch
            {
                // Ignore malformed user profile files; the log subsystem may not be available here.
            }
        }
        return result;
    }

    private static void SaveProfile<T>(string directory, string id, T profile)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions), Encoding.UTF8);
    }

    private static void DeleteProfile(string directory, string id)
    {
        var path = Path.Combine(directory, id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class AssociationService
{
    public DefaultAppSnapshot Merge(DefaultAppSnapshot snapshot, IEnumerable<OverrideAssociation> overrides)
    {
        var map = snapshot.Associations.ToDictionary(KeyOf, StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides)
        {
            var identifier = item.Kind == AssociationKind.FileExtension
                ? AppAssociation.NormalizeExtension(item.Identifier)
                : item.Identifier.Trim().ToLowerInvariant();
            map[KeyOf(item.Kind, identifier)] = new AppAssociation
            {
                Identifier = identifier,
                Kind = item.Kind,
                ProgId = item.ProgId,
                ApplicationName = item.SourceTemplate
            };
        }

        return snapshot with
        {
            CreatedAt = DateTimeOffset.Now,
            Associations = map.Values.OrderBy(a => a.Kind).ThenBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public IReadOnlyList<AssociationDiff> Compare(DefaultAppSnapshot current, DefaultAppSnapshot target)
    {
        var currentMap = current.Associations.ToDictionary(KeyOf, StringComparer.OrdinalIgnoreCase);
        var targetMap = target.Associations.ToDictionary(KeyOf, StringComparer.OrdinalIgnoreCase);
        var keys = currentMap.Keys.Union(targetMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        var diffs = new List<AssociationDiff>();
        foreach (var key in keys)
        {
            currentMap.TryGetValue(key, out var currentAssoc);
            targetMap.TryGetValue(key, out var targetAssoc);
            if (string.Equals(currentAssoc?.ProgId, targetAssoc?.ProgId, StringComparison.OrdinalIgnoreCase)) continue;
            var assoc = targetAssoc ?? currentAssoc!;
            diffs.Add(new AssociationDiff(assoc.Identifier, assoc.Kind, currentAssoc?.ProgId, targetAssoc?.ProgId));
        }
        return diffs;
    }

    public string GenerateSetUserFtaConfig(DefaultAppSnapshot snapshot)
    {
        var lines = snapshot.Associations
            .Where(a => !string.IsNullOrWhiteSpace(a.ProgId))
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(a => $"{(a.Kind == AssociationKind.FileExtension ? AppAssociation.NormalizeExtension(a.Identifier) : a.Identifier.Trim().ToLowerInvariant())}, {a.ProgId}");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string KeyOf(AppAssociation association) => KeyOf(association.Kind, association.NormalizedIdentifier);
    private static string KeyOf(AssociationKind kind, string identifier) => $"{kind}:{identifier}";
}

public interface IProcessRunner
{
    Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"无法启动进程: {fileName}");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}

public sealed class SetUserFtaService
{
    private readonly ConfigurationStore _store;
    private readonly AssociationService _associationService;
    private readonly IProcessRunner _runner;

    public SetUserFtaService(ConfigurationStore store, AssociationService associationService, IProcessRunner? runner = null)
    {
        _store = store;
        _associationService = associationService;
        _runner = runner ?? new ProcessRunner();
    }

    public string ToolPath => Path.Combine(AppContext.BaseDirectory, "SetUserFTA.exe");
    public bool IsToolAvailable => File.Exists(ToolPath);

    public async Task<ApplyResult> ApplyAsync(DefaultAppSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (!IsToolAvailable)
        {
            var message = $"未找到 SetUserFTA.exe：{ToolPath}";
            _store.AppendLog(message);
            return new ApplyResult(false, -1, string.Empty, message, string.Empty);
        }

        var configPath = Path.Combine(Path.GetTempPath(), $"DefaultAppLocker-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(configPath, _associationService.GenerateSetUserFtaConfig(snapshot), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        _store.AppendLog($"调用 SetUserFTA: \"{ToolPath}\" \"{configPath}\"");
        var result = await _runner.RunAsync(ToolPath, $"\"{configPath}\"", cancellationToken).ConfigureAwait(false);
        _store.AppendLog($"SetUserFTA ExitCode={result.ExitCode}; Output={result.Output.Trim()}; Error={result.Error.Trim()}");
        return new ApplyResult(result.ExitCode == 0, result.ExitCode, result.Output, result.Error, configPath);
    }
}
