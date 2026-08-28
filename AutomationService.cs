using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CoolShift;

public class AutomationOptions
{
    public bool SmartBatteryEnabled { get; set; } = true;
    public List<string> TargetExecutables { get; set; } = new();
    public CoolIdleTier SelectedCoolIdleTier { get; set; } = CoolIdleTier.Balanced;
}

public class AutomationService : IAsyncDisposable, IDisposable
{
    private readonly PowerPlanService _powerPlanService;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private AutomationOptions _options;
    private Task? _loopTask;
    private bool _isCurrentlyOnBattery;
    private bool _wasRunningTargetProcess;

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
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
        _ = TriggerEvaluationAsync();
    }

    public void UpdateOptions(AutomationOptions options)
    {
        _options = options;
        _ = TriggerEvaluationAsync();
    }

    public async Task TriggerEvaluationAsync()
    {
        try { await EvaluateConditionsAsync(_cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"Automation evaluation error: {ex}");
            _powerPlanService.Log($"Automation evaluation error: {ex.Message}", "ERROR");
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await TriggerEvaluationAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
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
                ActiveTargetName = null;
                _wasRunningTargetProcess = false;
                await SwitchToCoolIdleAsync("Battery Auto-Switch", "Battery Detected", token);
                return;
            }

            var manual = FindManualTargetProcess();
            if (manual is not null)
            {
                if (!_wasRunningTargetProcess || ActiveTargetName is null)
                {
                    var snapshot = await _powerPlanService.GetModeSnapshotAsync(BasePlanGuid, token);
                    if (snapshot.Mode != ParkMode.AlwaysOn)
                    {
                        await _powerPlanService.SetModeAsync(BasePlanGuid, "Game Auto-Switch", ParkMode.AlwaysOn, _options.SelectedCoolIdleTier, token);
                        var message = $"Switched to Always-On (Detected: {manual})";
                        AutomationTriggered?.Invoke(this, message);
                        _powerPlanService.Log($"Automation triggered: {message}");
                    }
                    ActiveTargetName = manual;
                    _wasRunningTargetProcess = true;
                }
            }
            else if (_wasRunningTargetProcess || ActiveTargetName is not null)
            {
                await SwitchToCoolIdleAsync("Game Auto-Switch", "Selected app closed", token);
                ActiveTargetName = null;
                _wasRunningTargetProcess = false;
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

    private string? FindManualTargetProcess()
    {
        if (_options.TargetExecutables.Count == 0) return null;
        var targets = new HashSet<string>(_options.TargetExecutables.Select(t => System.IO.Path.GetFileNameWithoutExtension(t.Trim('"', ' '))), StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (targets.Contains(process.ProcessName)) return process.ProcessName + ".exe";
                }
                catch { }
            }
        }
        return null;
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
        await StopAsync();
        _evaluationLock.Dispose();
        _cts.Dispose();
    }

}
