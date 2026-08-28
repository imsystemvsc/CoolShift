using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CoolShift;

public class AutomationOptions
{
    public bool SmartBatteryEnabled { get; set; } = true;
    public bool AutomaticGameDetectionEnabled { get; set; }
    public List<string> TargetExecutables { get; set; } = new();
    public List<string> IgnoredApplications { get; set; } = new();
    public CoolIdleTier SelectedCoolIdleTier { get; set; } = CoolIdleTier.Balanced;
}

public class AutomationService : IAsyncDisposable, IDisposable
{
    private readonly PowerPlanService _powerPlanService;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private readonly GameActivationTracker _activationTracker = new();
    private AutomationOptions _options;
    private Task? _loopTask;
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;
    private GameInstallationCatalog? _gameCatalog;
    private bool _isCurrentlyOnBattery;
    private bool _wmiMonitoringAvailable;

    public string? ActiveTargetName { get; private set; }
    public string? BasePlanGuid { get; set; }
    public event EventHandler<string>? AutomationTriggered;

    public AutomationService(PowerPlanService powerPlanService, AutomationOptions options)
    {
        _powerPlanService = powerPlanService;
        _options = options;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        CheckBatteryStatus();
    }

    public void Start()
    {
        if (_loopTask is not null) return;
        StartProcessWatchers();
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
        _ = TriggerEvaluationAsync();
        _ = TriggerAfterActivationDelayAsync();
    }

    public void UpdateOptions(AutomationOptions options)
    {
        _options = options;
        if (options.AutomaticGameDetectionEnabled && _gameCatalog is null) _gameCatalog = GameInstallationCatalog.Load();
        _ = TriggerEvaluationAsync();
    }

    public async Task TriggerEvaluationAsync()
    {
        try { await EvaluateConditionsAsync(_cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Trace.WriteLine($"Automation evaluation error: {ex}"); }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await TriggerEvaluationAsync();
            try { await Task.Delay(_wmiMonitoringAvailable ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task EvaluateConditionsAsync(CancellationToken token)
    {
        await _evaluationLock.WaitAsync(token);
        try
        {
            if (BasePlanGuid is null) BasePlanGuid = (await _powerPlanService.GetActivePlanAsync(token)).Guid;

            if (_options.SmartBatteryEnabled && _isCurrentlyOnBattery)
            {
                _activationTracker.Reset();
                ActiveTargetName = null;
                await SwitchToCoolIdleAsync("Battery Auto-Switch", "Battery Detected", token);
                return;
            }

            var manual = FindManualTargetProcess();
            var automatic = manual is null && _options.AutomaticGameDetectionEnabled ? FindAutomaticGameProcess() : null;
            var action = _activationTracker.Update(DateTimeOffset.UtcNow, manual, automatic, _isCurrentlyOnBattery);
            ActiveTargetName = _activationTracker.ActiveGameName ?? manual?.Name ?? automatic?.Name;

            if (action == GameAutomationAction.ActivateAlwaysOn)
            {
                var snapshot = await _powerPlanService.GetModeSnapshotAsync(BasePlanGuid, token);
                if (snapshot.Mode != ParkMode.AlwaysOn)
                {
                    await _powerPlanService.SetModeAsync(BasePlanGuid, "Game Auto-Switch", ParkMode.AlwaysOn, _options.SelectedCoolIdleTier, token);
                    var message = $"Switched to Always-On (Detected: {ActiveTargetName})";
                    AutomationTriggered?.Invoke(this, message);
                    _powerPlanService.Log($"Automation triggered: {message}");
                }
            }
            else if (action == GameAutomationAction.RestoreCoolIdle)
            {
                await SwitchToCoolIdleAsync("Game Auto-Switch", "All detected games closed", token);
                ActiveTargetName = null;
            }
        }
        finally { _evaluationLock.Release(); }
    }

    private async Task SwitchToCoolIdleAsync(string planName, string reason, CancellationToken token)
    {
        var snapshot = await _powerPlanService.GetModeSnapshotAsync(BasePlanGuid!, token);
        if (snapshot.Mode == ParkMode.CoolIdle) return;
        await _powerPlanService.SetModeAsync(BasePlanGuid!, planName, ParkMode.CoolIdle, _options.SelectedCoolIdleTier, token);
        var message = $"Switched to Cool Idle [{PowerPlanService.TierToDisplay(_options.SelectedCoolIdleTier)}] ({reason})";
        AutomationTriggered?.Invoke(this, message);
        _powerPlanService.Log($"Automation triggered: {message}");
    }

    private DetectedGame? FindManualTargetProcess()
    {
        if (_options.TargetExecutables.Count == 0) return null;
        var targets = new HashSet<string>(_options.TargetExecutables.Select(t => System.IO.Path.GetFileNameWithoutExtension(t.Trim('"', ' '))), StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (targets.Contains(process.ProcessName)) return new DetectedGame(process.Id.ToString(), System.IO.Path.GetFileName(process.MainModule?.FileName ?? process.ProcessName + ".exe"), true);
                }
                catch { }
            }
        }
        return null;
    }

    private DetectedGame? FindAutomaticGameProcess()
    {
        _gameCatalog ??= GameInstallationCatalog.Load();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var path = process.MainModule?.FileName;
                    if (path is not null && _gameCatalog.IsGameExecutable(path, _options.IgnoredApplications)) return new DetectedGame(process.Id.ToString(), System.IO.Path.GetFileNameWithoutExtension(path), false);
                }
                catch { }
            }
        }
        return null;
    }

    private void StartProcessWatchers()
    {
        try
        {
            _processStartWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _processStopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _processStartWatcher.EventArrived += OnProcessEventArrived;
            _processStopWatcher.EventArrived += OnProcessEventArrived;
            _processStartWatcher.Start();
            _processStopWatcher.Start();
            _wmiMonitoringAvailable = true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"WMI process monitoring unavailable; using polling fallback: {ex.Message}");
            DisposeProcessWatchers();
            _wmiMonitoringAvailable = false;
        }
    }

    private void OnProcessEventArrived(object sender, EventArrivedEventArgs e)
    {
        _ = TriggerEvaluationAsync();
        _ = TriggerAfterActivationDelayAsync();
    }

    private async Task TriggerAfterActivationDelayAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token);
            await TriggerEvaluationAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { CheckBatteryStatus(); _ = TriggerEvaluationAsync(); }
    private void CheckBatteryStatus() => _isCurrentlyOnBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;

    public async Task StopAsync()
    {
        if (_loopTask is null) return;
        _cts.Cancel();
        try { await _loopTask; } catch (OperationCanceledException) { } finally { _loopTask = null; }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        DisposeProcessWatchers();
        await StopAsync();
        _evaluationLock.Dispose();
        _cts.Dispose();
    }

    private void DisposeProcessWatchers()
    {
        foreach (var watcher in new[] { _processStartWatcher, _processStopWatcher })
        {
            if (watcher is null) continue;
            try { watcher.Stop(); } catch { }
            watcher.Dispose();
        }
        _processStartWatcher = null;
        _processStopWatcher = null;
    }
}
