using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DefaultAppLocker.Core;
using Microsoft.Win32;

namespace DefaultAppLocker;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RegistryDefaultAppScanner _scanner = new();
    private readonly ConfigurationStore _store = new();
    private readonly AssociationService _associationService = new();
    private readonly TemplateCatalog _templates = new();
    private readonly AppCandidateDiscovery _candidateDiscovery = new();
    private AppConfig _config;
    private DefaultAppSnapshot _currentSnapshot = new();
    private string _status = "就绪";
    private string _helpText = "将鼠标放在按钮上查看功能说明。";
    private IQuickTemplate? _selectedTemplate;
    private AppCandidate? _selectedCandidate;
    private string _selectedExecutable = string.Empty;
    private bool _isBusy;
    private SnapshotProfile? _selectedSnapshotProfile;
    private QuickTemplateProfile? _selectedQuickProfile;
    private string _snapshotAlias = string.Empty;
    private string _templateProfileAlias = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private string _themeMode = "Default";
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public MainViewModel()
    {
        _config = _store.LoadConfig();
        Associations = [];
        Differences = [];
        Candidates = [];
        SnapshotProfiles = [];
        QuickProfiles = [];
        Templates = new ObservableCollection<TemplateViewModel>(_templates.AvailableTemplates.Select(t => new TemplateViewModel(t)));

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveSnapshotAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy);
        CompareCommand = new AsyncRelayCommand(CompareAsync, () => !IsBusy);
        BrowseExecutableCommand = new RelayCommand(BrowseExecutable);
        UseCandidateCommand = new RelayCommand(UseSelectedCandidate, () => SelectedCandidate is not null);
        AddTemplateCommand = new RelayCommand(AddTemplateOverride, () => SelectedTemplate is not null);
        QuickApplyTemplateCommand = new AsyncRelayCommand(QuickApplyTemplateAsync, () => !IsBusy && SelectedTemplate is not null);
        ClearOverridesCommand = new RelayCommand(() => { _config.Override.Clear(); SaveConfig(); RefreshAssociations(_currentSnapshot); Status = "已清空快捷覆盖。"; });
        ToggleRestoreTaskCommand = new AsyncRelayCommand(ToggleRestoreTaskAsync, () => !IsBusy);
        ToggleContinuousTaskCommand = new AsyncRelayCommand(ToggleContinuousTaskAsync, () => !IsBusy);
        ShowHelpCommand = new RelayCommand<string>(text => HelpText = string.IsNullOrWhiteSpace(text) ? "将鼠标放在按钮上查看功能说明。" : text);
        SaveSnapshotProfileCommand = new RelayCommand(SaveSnapshotProfile);
        ApplySnapshotProfileCommand = new AsyncRelayCommand(ApplySnapshotProfileAsync, () => !IsBusy && SelectedSnapshotProfile is not null);
        RenameSnapshotProfileCommand = new RelayCommand(RenameSnapshotProfile, () => SelectedSnapshotProfile is not null);
        DeleteSnapshotProfileCommand = new RelayCommand(DeleteSnapshotProfile, () => SelectedSnapshotProfile is not null);
        SaveQuickProfileCommand = new RelayCommand(SaveQuickProfile);
        ApplyQuickProfileCommand = new AsyncRelayCommand(ApplyQuickProfileAsync, () => !IsBusy && SelectedQuickProfile is not null);
        RenameQuickProfileCommand = new RelayCommand(RenameQuickProfile, () => SelectedQuickProfile is not null);
        DeleteQuickProfileCommand = new RelayCommand(DeleteQuickProfile, () => SelectedQuickProfile is not null);
        ExportConfigCommand = new RelayCommand(ExportConfig);
        ImportConfigCommand = new RelayCommand(ImportConfig);
        SaveLockProfileCommand = new RelayCommand(SaveLockProfile, () => SelectedSnapshotProfile is not null);
        ChangeThemeCommand = new RelayCommand<string>(ApplyThemeMode);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); IsToastVisible = false; };

        SelectedTemplate = Templates.FirstOrDefault()?.Template;
        ReloadProfiles();
    }

    public ObservableCollection<AssociationRow> Associations { get; }
    public ObservableCollection<AssociationDiff> Differences { get; }
    public ObservableCollection<TemplateViewModel> Templates { get; }
    public ObservableCollection<AppCandidate> Candidates { get; }
    public ObservableCollection<SnapshotProfile> SnapshotProfiles { get; }
    public ObservableCollection<QuickTemplateProfile> QuickProfiles { get; }

    public ICommand ScanCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand BrowseExecutableCommand { get; }
    public ICommand UseCandidateCommand { get; }
    public ICommand AddTemplateCommand { get; }
    public ICommand QuickApplyTemplateCommand { get; }
    public ICommand ClearOverridesCommand { get; }
    public ICommand ToggleRestoreTaskCommand { get; }
    public ICommand ToggleContinuousTaskCommand { get; }
    public ICommand ShowHelpCommand { get; }
    public ICommand SaveSnapshotProfileCommand { get; }
    public ICommand ApplySnapshotProfileCommand { get; }
    public ICommand RenameSnapshotProfileCommand { get; }
    public ICommand DeleteSnapshotProfileCommand { get; }
    public ICommand SaveQuickProfileCommand { get; }
    public ICommand ApplyQuickProfileCommand { get; }
    public ICommand RenameQuickProfileCommand { get; }
    public ICommand DeleteQuickProfileCommand { get; }
    public ICommand ExportConfigCommand { get; }
    public ICommand ImportConfigCommand { get; }
    public ICommand SaveLockProfileCommand { get; }
    public ICommand ChangeThemeCommand { get; }

    public string Status { get => _status; set => SetField(ref _status, value); }
    public string HelpText { get => _helpText; set => SetField(ref _helpText, value); }
    public string SnapshotAlias { get => _snapshotAlias; set => SetField(ref _snapshotAlias, value); }
    public string TemplateProfileAlias { get => _templateProfileAlias; set => SetField(ref _templateProfileAlias, value); }
    public string ConfigRoot => _store.RootDirectory;
    public string ToastMessage { get => _toastMessage; private set => SetField(ref _toastMessage, value); }
    public bool IsToastVisible { get => _isToastVisible; private set => SetField(ref _isToastVisible, value); }
    public string ThemeMode { get => _themeMode; private set => SetField(ref _themeMode, value); }
    public string SelectedSnapshotProfileDisplay => SelectedSnapshotProfile?.DisplayName ?? "未选择";
    public string SelectedQuickProfileDisplay => SelectedQuickProfile?.DisplayName ?? "未选择";
    public int AssociationCount => Associations.Count;
    public int OverrideCount => _config.Override.Count;
    public string LastSnapshotText => _config.Snapshot.Associations.Count == 0 ? "尚未保存" : _config.Snapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public bool SetUserFtaAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, "SetUserFTA.exe"));
    public string SetUserFtaStatus => SetUserFtaAvailable ? "已检测到 SetUserFTA.exe" : "未检测到 SetUserFTA.exe（请与主程序放在同一目录）";
    public string HelpDocument => """
DefaultAppLocker 软件帮助文档

一、产品定位
DefaultAppLocker 是 Windows 默认应用配置管理器。软件不替代 Windows 默认应用设置页，也不作为通用默认应用编辑器；其职责是保存当前默认应用配置、恢复默认应用配置、比较配置差异、锁定默认应用配置，以及提供少量高频模板化配置。

二、核心概念
1. 配置快照：保存当前用户文件关联和协议关联。快照以时间轴形式保存为多个独立 JSON 配置文件，可设置别名、重命名、删除，并可从下拉框一键应用。
2. 默认应用模板方案：原“七大模板配置”的正式名称。模板方案保存浏览器、PDF、图片、视频、音频、文本编辑器、Microsoft Office 等高频模板产生的覆盖项。
3. 目标配置：目标配置由“配置快照 + 默认应用模板方案/快捷覆盖”合并得到。应用时，软件会生成临时 SetUserFTA 配置并调用同目录 SetUserFTA.exe。

三、推荐工作流
1. 首先在 Windows 设置中完成默认应用选择。
2. 回到本软件点击“重新扫描当前配置”。
3. 在“配置快照”页输入别名并保存为快照配置。
4. 如需批量模板化设置，在“快捷配置”页配置模板覆盖，并保存为“默认应用模板方案”。
5. 需要恢复时，从下拉框选择快照或模板方案并一键应用。

四、导入与导出
“导出配置”会生成一个 JSON 配置包，包含全部配置快照和默认应用模板方案。“导入配置”会读取该配置包，并将其中的快照与模板方案写入本机配置目录。导入不会删除现有配置。

五、运行模式
软件默认不是后台程序。仅当用户主动启用“登录自动恢复”或“持续锁定”时，软件才创建计划任务。

六、文件与目录
主程序 DefaultAppLocker.exe 与 SetUserFTA.exe 应位于同一目录。默认配置目录为 %AppData%\DefaultAppLocker。所有配置均为 JSON 文件，不使用数据库。

七、命令行模式
所有命令行参数均为静默模式，不启动 GUI。常用命令：
DefaultAppLocker.exe --capture-snapshot [别名]：扫描当前用户默认应用关联，并保存为新的配置快照；别名可省略。
DefaultAppLocker.exe --export-all <path.json>：导出全部配置快照和默认应用模板方案到指定 JSON 文件。
DefaultAppLocker.exe --export-snapshots <path.json>：仅导出配置快照到指定 JSON 文件。
DefaultAppLocker.exe --export-templates <path.json>：仅导出默认应用模板方案到指定 JSON 文件。
DefaultAppLocker.exe --import <path.json>：从指定 JSON 配置包导入配置快照和默认应用模板方案。
DefaultAppLocker.exe --apply-snapshot <id|alias|latest>：使用 SetUserFTA 静默应用指定配置快照；latest 表示最近创建的快照。
DefaultAppLocker.exe --apply-template <id|alias|latest>：将指定默认应用模板方案合并到当前快照后使用 SetUserFTA 静默应用；latest 表示最近创建的模板方案。
DefaultAppLocker.exe --restore：使用 SetUserFTA 静默恢复 Config.json 中保存的当前快照。
DefaultAppLocker.exe --lock-monitor：执行一次“持续锁定”检查；扫描当前默认应用，与目标配置比较，发现差异时调用 SetUserFTA 恢复。通常由持续锁定计划任务定时调用。
DefaultAppLocker.exe --help 或 --?：显示命令行帮助。
""";

    public SnapshotProfile? SelectedSnapshotProfile
    {
        get => _selectedSnapshotProfile;
        set
        {
            if (SetField(ref _selectedSnapshotProfile, value))
            {
                SnapshotAlias = value?.Alias ?? string.Empty;
                OnPropertyChanged(nameof(SelectedSnapshotProfileDisplay));
                RaiseCommandStates();
            }
        }
    }

    public QuickTemplateProfile? SelectedQuickProfile
    {
        get => _selectedQuickProfile;
        set
        {
            if (SetField(ref _selectedQuickProfile, value))
            {
                TemplateProfileAlias = value?.Alias ?? string.Empty;
                OnPropertyChanged(nameof(SelectedQuickProfileDisplay));
                RaiseCommandStates();
            }
        }
    }

    public IQuickTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetField(ref _selectedTemplate, value))
            {
                SelectedExecutable = string.Empty;
                OnPropertyChanged(nameof(SelectedTemplateDescription));
                OnPropertyChanged(nameof(IsOfficeTemplate));
                OnPropertyChanged(nameof(IsBrowserTemplate));
                OnPropertyChanged(nameof(IsPdfTemplate));
                OnPropertyChanged(nameof(IsCustomProgramTemplate));
                OnPropertyChanged(nameof(SelectedTemplateTitle));
                RefreshCandidates();
                RaiseCommandStates();
            }
        }
    }

    public string SelectedTemplateTitle => SelectedTemplate?.DisplayName ?? "快捷模板";
    public string SelectedTemplateDescription => SelectedTemplate?.Description ?? string.Empty;
    public bool IsOfficeTemplate => SelectedTemplate is OfficeSuiteTemplate;
    public bool IsBrowserTemplate => SelectedTemplate is BrowserTemplate;
    public bool IsPdfTemplate => SelectedTemplate is PdfTemplate;
    public bool IsCustomProgramTemplate => SelectedTemplate is not null && !IsOfficeTemplate && !IsBrowserTemplate && !IsPdfTemplate;
    public AppCandidate? SelectedCandidate { get => _selectedCandidate; set { if (SetField(ref _selectedCandidate, value)) RaiseCommandStates(); } }
    public string SelectedExecutable { get => _selectedExecutable; set => SetField(ref _selectedExecutable, value); }
    public bool RestoreAtLogon { get => _config.Settings.RestoreAtLogon; set { _config.Settings.RestoreAtLogon = value; OnPropertyChanged(); SaveConfig(); } }
    public bool ContinuousLock { get => _config.Settings.ContinuousLock; set { _config.Settings.ContinuousLock = value; OnPropertyChanged(); SaveConfig(); } }
    public int ContinuousIntervalSeconds { get => _config.Settings.ContinuousLockIntervalSeconds; set { _config.Settings.ContinuousLockIntervalSeconds = Math.Max(60, value); OnPropertyChanged(); SaveConfig(); } }
    public bool IsBusy { get => _isBusy; set { if (SetField(ref _isBusy, value)) RaiseCommandStates(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        ApplyThemeMode("Default");
        if (_config.Snapshot.Associations.Count > 0)
        {
            _currentSnapshot = _config.Snapshot;
            RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override));
            RefreshCandidates();
            Status = $"已读取配置：{_config.Snapshot.Associations.Count} 项关联"; OnPropertyChanged(nameof(AssociationCount)); OnPropertyChanged(nameof(OverrideCount)); OnPropertyChanged(nameof(LastSnapshotText));
            return;
        }
        await ScanAsync().ConfigureAwait(false);
    }

    public void AcceptDroppedExecutable(string path)
    {
        var candidate = path.Trim('"');
        if (File.Exists(candidate) && string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
        { SelectedExecutable = candidate; Status = $"已从拖放获取目标程序：{candidate}"; }
        else Status = "拖放目标不是有效的 exe 文件。";
    }

    private Task ScanAsync() => RunBusyAsync(() =>
    {
        _currentSnapshot = _scanner.ScanCurrentUser();
        _config.Snapshot = _currentSnapshot;
        SaveConfig();
        RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override));
        RefreshCandidates();
        Status = $"重新扫描完成：{_currentSnapshot.Associations.Count} 项关联。";
        ShowToast("已刷新当前配置");
    });

    private Task SaveSnapshotAsync() => RunBusyAsync(() =>
    {
        var snapshot = _currentSnapshot.Associations.Count == 0 ? _scanner.ScanCurrentUser() : _currentSnapshot;
        var path = _store.CreateDatedSnapshotPath();
        _store.SaveSnapshot(snapshot, path);
        _store.SaveSnapshot(snapshot);
        _config.Snapshot = snapshot;
        _config.Settings.LastSnapshotPath = path;
        var profile = _store.CreateSnapshotProfile(snapshot, string.IsNullOrWhiteSpace(SnapshotAlias) ? null : SnapshotAlias);
        _config.Settings.SelectedSnapshotProfileId = profile.Id;
        SaveConfig();
        ReloadProfiles(profile.Id, null);
        Status = $"快照已保存：{profile.Alias}";
        ShowToast("快照已保存");
    });

    private async Task ApplyAsync() => await RunBusyAsync(async () =>
    {
        var target = _associationService.Merge(_config.Snapshot, _config.Override);
        var result = await new SetUserFtaService(_store, _associationService).ApplyAsync(target).ConfigureAwait(true);
        Status = result.Success ? $"应用完成。SetUserFTA ExitCode={result.ExitCode}" : $"应用失败：{result.Error}";
        ShowToast(result.Success ? "配置已应用" : "应用失败");
        if (!result.Success) ShowToast(result.Error);
    }).ConfigureAwait(false);

    private Task CompareAsync() => RunBusyAsync(() =>
    {
        var current = _scanner.ScanCurrentUser();
        var target = _associationService.Merge(_config.Snapshot, _config.Override);
        var diffs = _associationService.Compare(current, target);
        Application.Current.Dispatcher.Invoke(() => { Differences.Clear(); foreach (var diff in diffs) Differences.Add(diff); });
        Status = diffs.Count == 0 ? "当前配置与目标配置一致。" : $"发现 {diffs.Count} 项差异。";
        ShowToast("比较完成");
    });

    private void BrowseExecutable()
    {
        var dialog = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*", Title = "选择目标打开程序" };
        if (dialog.ShowDialog() == true) SelectedExecutable = dialog.FileName;
    }

    private void UseSelectedCandidate() { if (SelectedCandidate is not null) { SelectedExecutable = SelectedCandidate.ProgId; Status = $"已选择候选应用：{SelectedCandidate.DisplayName}"; ShowToast("已读取候选应用"); } }

    private void AddTemplateOverride()
    {
        try { var overrides = BuildSelectedTemplateOverrides(); MergeOverrides(overrides); Status = $"已加入快捷模板：{SelectedTemplate?.DisplayName}，{overrides.Count} 项。"; ShowToast("快捷覆盖已保存"); }
        catch (Exception ex) { Status = ex.Message; ShowToast(ex.Message); }
    }

    private async Task QuickApplyTemplateAsync()
    {
        try
        {
            var overrides = BuildSelectedTemplateOverrides();
            if (overrides.Count == 0) { Status = "该模板没有可应用的关联。"; ShowToast("该模板没有可应用的关联"); return; }
            Status = $"正在应用快捷模板：{SelectedTemplate?.DisplayName}，共 {overrides.Count} 项。";
            ShowToast("正在应用快捷模板");
            MergeOverrides(overrides);
            await ApplyAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { Status = ex.Message; ShowToast(ex.Message); }
    }

    private IReadOnlyList<OverrideAssociation> BuildSelectedTemplateOverrides()
    {
        if (SelectedTemplate is null) return [];
        var target = (IsBrowserTemplate || IsPdfTemplate || SelectedTemplate.RequiresExecutable) && !string.IsNullOrWhiteSpace(SelectedExecutable) ? SelectedExecutable : string.Empty;
        return SelectedTemplate.CreateOverrides(target);
    }

    private void MergeOverrides(IReadOnlyList<OverrideAssociation> overrides)
    {
        var existing = _config.Override.ToDictionary(o => $"{o.Kind}:{(o.Kind == AssociationKind.FileExtension ? AppAssociation.NormalizeExtension(o.Identifier) : o.Identifier.ToLowerInvariant())}", StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides) existing[$"{item.Kind}:{(item.Kind == AssociationKind.FileExtension ? AppAssociation.NormalizeExtension(item.Identifier) : item.Identifier.ToLowerInvariant())}"] = item;
        _config.Override = existing.Values.OrderBy(o => o.Kind).ThenBy(o => o.Identifier).ToList();
        SaveConfig();
        RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override));
    }

    private void RefreshCandidates()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Candidates.Clear(); SelectedCandidate = null;
            if (SelectedTemplate is null) return;
            foreach (var candidate in _candidateDiscovery.FindCandidates(SelectedTemplate.GetExtensions())) Candidates.Add(candidate);
            if (IsBrowserTemplate) EnsureCandidateAtTop(new AppCandidate("Microsoft Edge", ProgIdResolver.DefaultEdgeBrowserProgId, "msedge.exe", 2));
            if (IsPdfTemplate) EnsureCandidateAtTop(new AppCandidate("Microsoft Edge PDF", ProgIdResolver.DefaultEdgePdfProgId, "msedge.exe", 1));
            SelectedCandidate = Candidates.FirstOrDefault();
        });
    }
    private void EnsureCandidateAtTop(AppCandidate candidate) { var existing = Candidates.FirstOrDefault(c => string.Equals(c.ProgId, candidate.ProgId, StringComparison.OrdinalIgnoreCase)); if (existing is not null) Candidates.Remove(existing); Candidates.Insert(0, candidate); }

    private void ReloadProfiles(string? selectedSnapshotId = null, string? selectedQuickId = null)
    {
        SnapshotProfiles.Clear(); foreach (var p in _store.LoadSnapshotProfiles()) SnapshotProfiles.Add(p);
        QuickProfiles.Clear(); foreach (var p in _store.LoadQuickTemplateProfiles()) QuickProfiles.Add(p);
        SelectedSnapshotProfile = SnapshotProfiles.FirstOrDefault(p => p.Id == (selectedSnapshotId ?? _config.Settings.SelectedSnapshotProfileId)) ?? SnapshotProfiles.FirstOrDefault();
        SelectedQuickProfile = QuickProfiles.FirstOrDefault(p => p.Id == (selectedQuickId ?? _config.Settings.SelectedQuickTemplateProfileId)) ?? QuickProfiles.FirstOrDefault();
        OnPropertyChanged(nameof(SnapshotProfiles));
        OnPropertyChanged(nameof(QuickProfiles));
    }

    private void SaveSnapshotProfile()
    {
        var snapshot = _currentSnapshot.Associations.Count == 0 ? _config.Snapshot : _currentSnapshot;
        var p = _store.CreateSnapshotProfile(snapshot, SnapshotAlias);
        _config.Settings.SelectedSnapshotProfileId = p.Id; SaveConfig(); ReloadProfiles(p.Id, null); Status = $"已新建快照配置：{p.Alias}"; ShowToast("快照已保存");
    }
    private async Task ApplySnapshotProfileAsync()
    {
        if (SelectedSnapshotProfile is null) return;
        _config.Snapshot = SelectedSnapshotProfile.Snapshot; _currentSnapshot = SelectedSnapshotProfile.Snapshot; _config.Settings.SelectedSnapshotProfileId = SelectedSnapshotProfile.Id; SaveConfig(); RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override)); await ApplyAsync();
    }
    private void RenameSnapshotProfile()
    {
        if (SelectedSnapshotProfile is null || string.IsNullOrWhiteSpace(SnapshotAlias)) return;
        var id = SelectedSnapshotProfile.Id;
        SelectedSnapshotProfile.Alias = SnapshotAlias.Trim();
        _store.SaveSnapshotProfile(SelectedSnapshotProfile);
        _config.Settings.SelectedSnapshotProfileId = id;
        SaveConfig();
        ReloadProfiles(id, null);
        Status = $"已重命名快照配置：{SnapshotAlias}";
        ShowToast("快照已重命名");
    }
    private void DeleteSnapshotProfile() { if (SelectedSnapshotProfile is null) return; _store.DeleteSnapshotProfile(SelectedSnapshotProfile); _config.Settings.SelectedSnapshotProfileId = string.Empty; SaveConfig(); ReloadProfiles(); Status = "已删除快照配置。"; ShowToast("快照已删除"); }
    private void SaveQuickProfile() { var p = _store.CreateQuickTemplateProfile(_config.Override, TemplateProfileAlias); _config.Settings.SelectedQuickTemplateProfileId = p.Id; SaveConfig(); ReloadProfiles(null, p.Id); Status = $"已新建默认应用模板方案：{p.Alias}"; ShowToast("方案已保存"); }
    private async Task ApplyQuickProfileAsync() { if (SelectedQuickProfile is null) return; _config.Override = SelectedQuickProfile.Overrides.ToList(); _config.Settings.SelectedQuickTemplateProfileId = SelectedQuickProfile.Id; SaveConfig(); RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override)); await ApplyAsync(); }
    private void RenameQuickProfile()
    {
        if (SelectedQuickProfile is null || string.IsNullOrWhiteSpace(TemplateProfileAlias)) return;
        var id = SelectedQuickProfile.Id;
        SelectedQuickProfile.Alias = TemplateProfileAlias.Trim();
        _store.SaveQuickTemplateProfile(SelectedQuickProfile);
        _config.Settings.SelectedQuickTemplateProfileId = id;
        SaveConfig();
        ReloadProfiles(null, id);
        Status = $"已重命名默认应用模板方案：{TemplateProfileAlias}";
        ShowToast("方案已重命名");
    }
    private void DeleteQuickProfile() { if (SelectedQuickProfile is null) return; _store.DeleteQuickTemplateProfile(SelectedQuickProfile); _config.Settings.SelectedQuickTemplateProfileId = string.Empty; SaveConfig(); ReloadProfiles(); Status = "已删除默认应用模板方案。"; ShowToast("方案已删除"); }

    private void ExportConfig()
    {
        var dialog = new SaveFileDialog { Filter = "DefaultAppLocker 配置包 (*.json)|*.json|所有文件 (*.*)|*.*", FileName = $"DefaultAppLocker-Export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json", Title = "导出 DefaultAppLocker 配置" };
        if (dialog.ShowDialog() != true) return;
        _store.ExportPackage(dialog.FileName);
        Status = $"配置已导出：{dialog.FileName}";
        ShowToast("配置已导出");
    }
    private void ImportConfig()
    {
        var dialog = new OpenFileDialog { Filter = "DefaultAppLocker 配置包 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "导入 DefaultAppLocker 配置" };
        if (dialog.ShowDialog() != true) return;
        var result = _store.ImportPackage(dialog.FileName);
        ReloadProfiles();
        Status = $"导入完成：{result.Snapshots} 个快照配置，{result.TemplateProfiles} 个默认应用模板方案。";
        ShowToast("配置已导入");
    }

    private void SaveLockProfile()
    {
        if (SelectedSnapshotProfile is null) return;
        _config.Snapshot = SelectedSnapshotProfile.Snapshot;
        _currentSnapshot = SelectedSnapshotProfile.Snapshot;
        _config.Settings.SelectedSnapshotProfileId = SelectedSnapshotProfile.Id;
        SaveConfig();
        RefreshAssociations(_associationService.Merge(_config.Snapshot, _config.Override));
        Status = $"锁定/恢复使用配置已保存：{SelectedSnapshotProfile.Alias}";
        ShowToast("锁定/恢复配置已保存");
    }

    private async Task ToggleRestoreTaskAsync() => await RunBusyAsync(async () => { var result = await new LockingService(_store).SetRestoreAtLogonAsync(_config.Settings.RestoreAtLogon).ConfigureAwait(true); Status = result.Success ? "登录自动恢复设置已更新。" : $"登录自动恢复设置失败：{result.Message}"; ShowToast(result.Success ? "登录恢复已更新" : "登录恢复更新失败"); }).ConfigureAwait(false);
    private async Task ToggleContinuousTaskAsync() => await RunBusyAsync(async () => { var result = await new LockingService(_store).SetContinuousLockAsync(_config.Settings.ContinuousLock, _config.Settings.ContinuousLockIntervalSeconds).ConfigureAwait(true); Status = result.Success ? "持续锁定设置已更新。" : $"持续锁定设置失败：{result.Message}"; ShowToast(result.Success ? "持续锁定已更新" : "持续锁定更新失败"); }).ConfigureAwait(false);

    private void ApplyThemeMode(string? mode)
    {
        ThemeMode = string.IsNullOrWhiteSpace(mode) ? "Default" : mode;
        var light = ThemeMode.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                    (ThemeMode.Equals("Default", StringComparison.OrdinalIgnoreCase) && IsSystemLightTheme());
        SetBrush("SurfaceBrush", light ? "#F2F3F3F3" : "#F2202020");
        SetBrush("CardBackgroundBrush", light ? "#D9FFFFFF" : "#D92B2B2B");
        SetBrush("CardBackgroundHoverBrush", light ? "#E6FFFFFF" : "#E63A3A3A");
        SetBrush("CardBorderBrush", light ? "#22FFFFFF" : "#30FFFFFF");
        SetBrush("NavPaneBrush", light ? "#66FFFFFF" : "#55202020");
        SetBrush("SubtleHoverBrush", light ? "#1A000000" : "#20FFFFFF");
        SetBrush("DataGridAlternateRowBrush", light ? "#0A000000" : "#12FFFFFF");
        SetBrush("DataGridHeaderBorderBrush", light ? "#14000000" : "#22FFFFFF");
        SetBrush("ScrollThumbBrush", light ? "#33000000" : "#40FFFFFF");
        SetBrush("ScrollThumbHoverBrush", light ? "#55000000" : "#66FFFFFF");
        SetBrush("ControlBackgroundBrush", light ? "#D9FFFFFF" : "#D9303030");
        SetBrush("ControlBorderBrush", light ? "#55FFFFFF" : "#55FFFFFF");
        SetBrush("SecondaryButtonBackgroundBrush", light ? "#66FFFFFF" : "#33FFFFFF");
        SetBrush("SecondaryButtonForegroundBrush", light ? "#E4000000" : "#F2FFFFFF");
        SetBrush("TitleBarBrush", light ? "#99FFFFFF" : "#99202020");
        SetBrush("PrimaryTextBrush", light ? "#E4000000" : "#F2FFFFFF");
        SetBrush("SecondaryTextBrush", light ? "#9A000000" : "#BFFFFFFF");
        SetBrush("TertiaryTextBrush", light ? "#72000000" : "#8AFFFFFF");
        SetBrush("AccentLightBrush", light ? "#DCEEFF" : "#334C9EFF");
        SetBrush("NavSelectedBrush", light ? "#66FFFFFF" : "#55383838");
        ShowToast($"主题已切换：{ThemeMode}");
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) != 0;
        }
        catch { return true; }
    }

    private static void SetBrush(string key, string color)
    {
        var resources = Application.Current.MainWindow?.Resources ?? Application.Current.Resources;
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RefreshAssociations(DefaultAppSnapshot snapshot) => Application.Current.Dispatcher.Invoke(() => { Associations.Clear(); foreach (var assoc in snapshot.Associations) { var source = _config.Override.Any(o => string.Equals(o.Identifier, assoc.Identifier, StringComparison.OrdinalIgnoreCase)) ? "快捷覆盖" : "快照"; Associations.Add(new AssociationRow(assoc.Identifier, assoc.Kind.ToString(), assoc.ProgId, assoc.ApplicationName ?? string.Empty, source)); } OnPropertyChanged(nameof(AssociationCount)); OnPropertyChanged(nameof(OverrideCount)); OnPropertyChanged(nameof(LastSnapshotText)); OnPropertyChanged(nameof(SetUserFtaAvailable)); OnPropertyChanged(nameof(SetUserFtaStatus)); });
    private void SaveConfig() => _store.SaveConfig(_config);
    private async Task RunBusyAsync(Action action) => await RunBusyAsync(() => { action(); return Task.CompletedTask; }).ConfigureAwait(false);
    private async Task RunBusyAsync(Func<Task> action) { if (IsBusy) return; IsBusy = true; try { await action().ConfigureAwait(true); } catch (Exception ex) { _store.AppendLog(ex.ToString()); Status = "错误：" + ex.Message; ShowToast(ex.Message); } finally { IsBusy = false; } }
    private void RaiseCommandStates() { foreach (var c in new ICommand[] { ScanCommand, SaveCommand, ApplyCommand, CompareCommand, QuickApplyTemplateCommand, ToggleRestoreTaskCommand, ToggleContinuousTaskCommand, ApplySnapshotProfileCommand, ApplyQuickProfileCommand }) (c as AsyncRelayCommand)?.RaiseCanExecuteChanged(); foreach (var c in new ICommand[] { UseCandidateCommand, AddTemplateCommand, SaveLockProfileCommand, RenameSnapshotProfileCommand, DeleteSnapshotProfileCommand, RenameQuickProfileCommand, DeleteQuickProfileCommand }) (c as RelayCommand)?.RaiseCanExecuteChanged(); }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(propertyName); return true; }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record AssociationRow(string Identifier, string Kind, string ProgId, string ApplicationName, string Source);
public sealed class TemplateViewModel { public TemplateViewModel(IQuickTemplate template) => Template = template; public IQuickTemplate Template { get; } public string DisplayName => Template.DisplayName; public override string ToString() => DisplayName; }
public sealed class RelayCommand : ICommand { private readonly Action _execute; private readonly Func<bool>? _canExecute; public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; } public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true; public void Execute(object? parameter) => _execute(); public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
public sealed class RelayCommand<T> : ICommand { private readonly Action<T?> _execute; private readonly Func<T?, bool>? _canExecute; public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; } public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true; public void Execute(object? parameter) => _execute((T?)parameter); public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
public sealed class AsyncRelayCommand : ICommand { private readonly Func<Task> _execute; private readonly Func<bool>? _canExecute; public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; } public event EventHandler? CanExecuteChanged; public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true; public async void Execute(object? parameter) => await _execute().ConfigureAwait(true); public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
