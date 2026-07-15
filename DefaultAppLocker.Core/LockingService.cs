using System.Diagnostics;
using System.Text;

namespace DefaultAppLocker.Core;

public sealed class LockingService
{
    public const string RestoreTaskName = "DefaultAppLocker Restore At Logon";
    public const string ContinuousTaskName = "DefaultAppLocker Continuous Lock";
    private readonly ConfigurationStore _store;
    private readonly IProcessRunner _runner;

    public LockingService(ConfigurationStore store, IProcessRunner? runner = null)
    {
        _store = store;
        _runner = runner ?? new ProcessRunner();
    }

    public async Task<(bool Success, string Message)> SetRestoreAtLogonAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return enabled
            ? await CreateTaskAsync(RestoreTaskName, "--restore", "ONLOGON", 0, cancellationToken).ConfigureAwait(false)
            : await DeleteTaskAsync(RestoreTaskName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message)> SetContinuousLockAsync(bool enabled, int intervalSeconds, CancellationToken cancellationToken = default)
    {
        return enabled
            ? await CreateTaskAsync(ContinuousTaskName, "--lock-monitor", "MINUTE", Math.Max(1, intervalSeconds / 60), cancellationToken).ConfigureAwait(false)
            : await DeleteTaskAsync(ContinuousTaskName, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunRestoreOnceAsync(CancellationToken cancellationToken = default)
    {
        var config = _store.LoadConfig();
        var service = new AssociationService();
        var target = service.Merge(config.Snapshot, config.Override);
        var result = await new SetUserFtaService(_store, service).ApplyAsync(target, cancellationToken).ConfigureAwait(false);
        if (!result.Success) throw new InvalidOperationException(result.Error);
    }

    public async Task RunMonitorOnceAsync(IDefaultAppScanner scanner, CancellationToken cancellationToken = default)
    {
        var config = _store.LoadConfig();
        var service = new AssociationService();
        var target = service.Merge(config.Snapshot, config.Override);
        var current = scanner.ScanCurrentUser();
        var diffs = service.Compare(current, target);
        if (diffs.Count == 0) return;
        _store.AppendLog($"持续锁定检测到 {diffs.Count} 项差异，开始恢复。");
        await new SetUserFtaService(_store, service).ApplyAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Success, string Message)> CreateTaskAsync(string name, string args, string schedule, int modifier, CancellationToken cancellationToken)
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppContext.BaseDirectory, "DefaultAppLocker.exe");
        var taskRun = $"\"{exe}\" {args}";
        var builder = new StringBuilder($"/Create /F /TN \"{name}\" /TR \"{taskRun}\" /SC {schedule}");
        if (schedule.Equals("MINUTE", StringComparison.OrdinalIgnoreCase)) builder.Append($" /MO {Math.Max(1, modifier)}");
        var result = await _runner.RunAsync("schtasks.exe", builder.ToString(), cancellationToken).ConfigureAwait(false);
        var ok = result.ExitCode == 0;
        var message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        _store.AppendLog($"计划任务 {(ok ? "创建" : "创建失败")} {name}: {message.Trim()}");
        return (ok, message.Trim());
    }

    private async Task<(bool Success, string Message)> DeleteTaskAsync(string name, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("schtasks.exe", $"/Delete /F /TN \"{name}\"", cancellationToken).ConfigureAwait(false);
        var ok = result.ExitCode == 0 || result.Error.Contains("找不到", StringComparison.OrdinalIgnoreCase) || result.Output.Contains("不存在", StringComparison.OrdinalIgnoreCase);
        var message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        _store.AppendLog($"计划任务 {(ok ? "删除" : "删除失败")} {name}: {message.Trim()}");
        return (ok, message.Trim());
    }
}
