namespace DefaultAppLocker.Core;

public sealed record CommandLineResult(bool Handled, int ExitCode, string Message);

public sealed class DefaultAppLockerCommandLine
{
    private readonly ConfigurationStore _store;
    private readonly AssociationService _associationService;
    private readonly IDefaultAppScanner _scanner;

    public DefaultAppLockerCommandLine(ConfigurationStore store, AssociationService associationService, IDefaultAppScanner scanner)
    {
        _store = store;
        _associationService = associationService;
        _scanner = scanner;
    }

    public bool HasCommandLineMode(string[] args) => args.Any(a => a.StartsWith("--", StringComparison.Ordinal));

    public async Task<CommandLineResult> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (!HasCommandLineMode(args)) return new CommandLineResult(false, 0, string.Empty);
        try
        {
            var map = Parse(args);
            if (map.ContainsKey("help") || map.ContainsKey("?")) return new CommandLineResult(true, 0, Usage);

            if (map.TryGetValue("export-all", out var exportAll))
            {
                _store.ExportPackage(RequireValue("--export-all", exportAll), includeSnapshots: true, includeTemplateProfiles: true);
                return new CommandLineResult(true, 0, "已导出全部配置。");
            }
            if (map.TryGetValue("export-snapshots", out var exportSnapshots))
            {
                _store.ExportPackage(RequireValue("--export-snapshots", exportSnapshots), includeSnapshots: true, includeTemplateProfiles: false);
                return new CommandLineResult(true, 0, "已导出配置快照。");
            }
            if (map.TryGetValue("export-templates", out var exportTemplates))
            {
                _store.ExportPackage(RequireValue("--export-templates", exportTemplates), includeSnapshots: false, includeTemplateProfiles: true);
                return new CommandLineResult(true, 0, "已导出默认应用模板方案。");
            }
            if (map.TryGetValue("import", out var importPath))
            {
                var result = _store.ImportPackage(RequireValue("--import", importPath));
                return new CommandLineResult(true, 0, $"导入完成：{result.Snapshots} 个快照配置，{result.TemplateProfiles} 个默认应用模板方案。");
            }
            if (map.TryGetValue("capture-snapshot", out var captureAlias))
            {
                var snapshot = _scanner.ScanCurrentUser();
                var profile = _store.CreateSnapshotProfile(snapshot, string.IsNullOrWhiteSpace(captureAlias) ? null : captureAlias);
                return new CommandLineResult(true, 0, $"已保存配置快照：{profile.Alias}");
            }
            if (map.TryGetValue("apply-snapshot", out var snapshotSelector))
            {
                var profile = ResolveSnapshotProfile(RequireValue("--apply-snapshot", snapshotSelector));
                var result = await new SetUserFtaService(_store, _associationService).ApplyAsync(profile.Snapshot, cancellationToken).ConfigureAwait(false);
                return new CommandLineResult(true, result.Success ? 0 : Math.Max(1, result.ExitCode), result.Success ? $"已应用配置快照：{profile.Alias}" : result.Error);
            }
            if (map.TryGetValue("apply-template", out var templateSelector))
            {
                var profile = ResolveTemplateProfile(RequireValue("--apply-template", templateSelector));
                var config = _store.LoadConfig();
                var target = _associationService.Merge(config.Snapshot, profile.Overrides);
                var result = await new SetUserFtaService(_store, _associationService).ApplyAsync(target, cancellationToken).ConfigureAwait(false);
                return new CommandLineResult(true, result.Success ? 0 : Math.Max(1, result.ExitCode), result.Success ? $"已应用默认应用模板方案：{profile.Alias}" : result.Error);
            }
            if (map.ContainsKey("restore"))
            {
                var result = await new SetUserFtaService(_store, _associationService).ApplyAsync(_store.LoadConfig().Snapshot, cancellationToken).ConfigureAwait(false);
                return new CommandLineResult(true, result.Success ? 0 : Math.Max(1, result.ExitCode), result.Success ? "已恢复当前配置快照。" : result.Error);
            }

            return new CommandLineResult(true, 2, "未知命令行参数。" + Environment.NewLine + Usage);
        }
        catch (Exception ex)
        {
            _store.AppendLog(ex.ToString());
            return new CommandLineResult(true, 1, ex.Message);
        }
    }

    public static IReadOnlyDictionary<string, string?> Parse(string[] args)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = arg[2..];
            string? value = null;
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                value = key[(eq + 1)..].Trim('"');
                key = key[..eq];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i].Trim('"');
            }
            map[key] = value;
        }
        return map;
    }

    private SnapshotProfile ResolveSnapshotProfile(string selector)
    {
        var profiles = _store.LoadSnapshotProfiles();
        if (selector.Equals("latest", StringComparison.OrdinalIgnoreCase)) return profiles.FirstOrDefault() ?? throw new InvalidOperationException("没有可用的配置快照。");
        return profiles.FirstOrDefault(p => p.Id.Equals(selector, StringComparison.OrdinalIgnoreCase) || p.Alias.Equals(selector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到配置快照：{selector}");
    }

    private QuickTemplateProfile ResolveTemplateProfile(string selector)
    {
        var profiles = _store.LoadQuickTemplateProfiles();
        if (selector.Equals("latest", StringComparison.OrdinalIgnoreCase)) return profiles.FirstOrDefault() ?? throw new InvalidOperationException("没有可用的默认应用模板方案。");
        return profiles.FirstOrDefault(p => p.Id.Equals(selector, StringComparison.OrdinalIgnoreCase) || p.Alias.Equals(selector, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到默认应用模板方案：{selector}");
    }

    private static string RequireValue(string option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{option} 需要参数值。");
        return value;
    }

    public const string Usage = """
DefaultAppLocker 命令行用法（所有命令均静默运行，不启动 GUI）：
  DefaultAppLocker.exe --capture-snapshot [别名]
      扫描当前用户默认应用并保存为配置快照。
  DefaultAppLocker.exe --export-all <path.json>
      导出全部配置快照和默认应用模板方案。
  DefaultAppLocker.exe --export-snapshots <path.json>
      仅导出配置快照。
  DefaultAppLocker.exe --export-templates <path.json>
      仅导出默认应用模板方案。
  DefaultAppLocker.exe --import <path.json>
      导入配置包。
  DefaultAppLocker.exe --apply-snapshot <id|alias|latest>
      一键应用指定配置快照。
  DefaultAppLocker.exe --apply-template <id|alias|latest>
      一键应用指定默认应用模板方案。
  DefaultAppLocker.exe --restore
      静默恢复 Config.json 中的当前快照。
  DefaultAppLocker.exe --help
      显示命令行说明。
""";
}
