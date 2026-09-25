using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CoolShift;

public class AutomationOptions
{
    public bool SmartBatteryEnabled { get; set; } = true;
    public List<string> TargetExecutables { get; set; } = new();
    public CoolIdleTier SelectedCoolIdleTier { get; set; } = CoolIdleTier.Balanced;
}

public class AutomationService : IAsyncDisposable, IDisposable
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcesses([Out] uint[] lpidProcess, uint cb, out uint lpcbNeeded);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint SYNCHRONIZE = 0x00100000;

    private readonly PowerPlanService _powerPlanService;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private AutomationOptions _options;
    private Task? _loopTask;
    private bool _isCurrentlyOnBattery;
    private bool _wasRunningTargetProcess;

    private SafeWaitHandle? _activeTargetWaitHandle;
    private RegisteredWaitHandle? _registeredWaitHandle;
    private TaskCompletionSource<bool>? _targetExitTcs;

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

            try
            {
                if (_wasRunningTargetProcess && _targetExitTcs is not null)
                {
                    // Target is active: zero polling loop. Passively wait for OS process exit signal.
                    using var registration = token.Register(() => _targetExitTcs.TrySetCanceled());
                    await _targetExitTcs.Task;
                }
                else
                {
                    // Idle: check every 3 seconds with lightweight EnumProcesses
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
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
                CleanUpProcessWait();
                ActiveTargetName = null;
                _wasRunningTargetProcess = false;
                await SwitchToCoolIdleAsync("Battery Auto-Switch", "Battery Detected", token);
                return;
            }

            var (manual, pid) = FindManualTargetProcess();
            if (manual is not null && pid > 0)
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
                    RegisterProcessExitWait(pid);
                }
            }
            else if (_wasRunningTargetProcess || ActiveTargetName is not null)
            {
                CleanUpProcessWait();
                await SwitchToCoolIdleAsync("Game Auto-Switch", "Selected app closed", token);
                ActiveTargetName = null;
                _wasRunningTargetProcess = false;
            }
        }
        finally { _evaluationLock.Release(); }
    }

    private void RegisterProcessExitWait(uint pid)
    {
        CleanUpProcessWait();

        IntPtr hProcess = OpenProcess(SYNCHRONIZE, false, pid);
        if (hProcess == IntPtr.Zero)
        {
            // Process might have terminated right away
            _ = TriggerEvaluationAsync();
            return;
        }

        _activeTargetWaitHandle = new SafeWaitHandle(hProcess, ownsHandle: true);
        var waitHandle = new AutoResetEvent(false) { SafeWaitHandle = _activeTargetWaitHandle };
        _targetExitTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            (state, timedOut) =>
            {
                _targetExitTcs?.TrySetResult(true);
                _ = TriggerEvaluationAsync();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    private void CleanUpProcessWait()
    {
        if (_registeredWaitHandle is not null)
        {
            _registeredWaitHandle.Unregister(null);
            _registeredWaitHandle = null;
        }

        if (_activeTargetWaitHandle is not null && !_activeTargetWaitHandle.IsClosed)
        {
            _activeTargetWaitHandle.Dispose();
            _activeTargetWaitHandle = null;
        }

        _targetExitTcs?.TrySetResult(false);
        _targetExitTcs = null;
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

    private (string? Name, uint Pid) FindManualTargetProcess()
    {
        if (_options.TargetExecutables.Count == 0) return (null, 0);

        var targets = new HashSet<string>(
            _options.TargetExecutables.Select(t =>
            {
                var name = Path.GetFileName(t.Trim('"', ' '));
                return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            }),
            StringComparer.OrdinalIgnoreCase);

        uint[] processIds = new uint[1024];
        if (!EnumProcesses(processIds, (uint)(processIds.Length * sizeof(uint)), out uint bytesNeeded))
        {
            return (null, 0);
        }

        uint count = bytesNeeded / sizeof(uint);
        var sb = new StringBuilder(1024);

        for (int i = 0; i < count; i++)
        {
            uint pid = processIds[i];
            if (pid <= 4) continue;

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess == IntPtr.Zero) continue;

            try
            {
                uint size = (uint)sb.Capacity;
                sb.Clear();
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    string fullPath = sb.ToString();
                    string fileName = Path.GetFileName(fullPath);
                    if (targets.Contains(fileName))
                    {
                        return (fileName, pid);
                    }
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        return (null, 0);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { CheckBatteryStatus(); _ = TriggerEvaluationAsync(); }
    private void CheckBatteryStatus() => _isCurrentlyOnBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;

    public async Task StopAsync()
    {
        if (_loopTask is null) return;
        _cts.Cancel();
        CleanUpProcessWait();
        try { await _loopTask; } catch (OperationCanceledException) { } finally { _loopTask = null; }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        await StopAsync();
        CleanUpProcessWait();
        _evaluationLock.Dispose();
        _cts.Dispose();
    }
}
