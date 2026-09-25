using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using CoolShift;
using LibreHardwareMonitor.Hardware;

namespace CoolShift.Monitoring;

public readonly record struct CoreTemperature(string Name, double? Celsius);

public readonly record struct CpuTemperatureSnapshot(
    double? PackageCelsius,
    double? CpuLoad,
    IReadOnlyList<CoreTemperature> Cores,
    double? GpuCelsius,
    double? GpuLoad,
    double? CpuPowerWatts,
    double? CpuClockMhz,
    double? CpuVoltageV,
    double? GpuPowerWatts,
    double? GpuClockMhz,
    double? GpuVoltageV
)
{
    public static readonly CpuTemperatureSnapshot Empty = new(null, null, Array.Empty<CoreTemperature>(), null, null, null, null, null, null, null, null);
}

internal sealed class HardwareMonitorService : IDisposable
{
    private static readonly SensorType[] AllowedTypes =
    {
        SensorType.Temperature,
        SensorType.Load,
        SensorType.Clock,
        SensorType.Power,
        SensorType.Fan,
        SensorType.Flow,
        SensorType.Voltage
    };

    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _sync = new();
    private bool _disposed;
    private bool _deepMonitoringEnabled;

    private ISensor? _packageSensor;
    private ISensor? _cpuLoadSensor;
    private List<ISensor> _coreSensors = new();
    private ISensor? _gpuSensor;
    private ISensor? _gpuLoadSensor;
    private ISensor? _cpuPowerSensor;
    private ISensor? _cpuClockSensor;
    private ISensor? _cpuVoltageSensor;
    private ISensor? _gpuPowerSensor;
    private ISensor? _gpuClockSensor;
    private ISensor? _gpuVoltageSensor;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false
        };

        _computer.Open();
        RefreshTelemetrySensorReferences();
    }

    public void EnableDeepMonitoring(bool enabled)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_deepMonitoringEnabled == enabled) return;
            _deepMonitoringEnabled = enabled;

            _computer.IsMemoryEnabled = enabled;
            _computer.IsMotherboardEnabled = enabled;
            _computer.IsControllerEnabled = enabled;
            _computer.IsStorageEnabled = enabled;
            _computer.IsNetworkEnabled = enabled;
            _computer.IsBatteryEnabled = enabled;
            _computer.IsPsuEnabled = enabled;

            RefreshTelemetrySensorReferences();
        }
    }

    public CpuTemperatureSnapshot GetTelemetrySnapshot(bool temperaturesOnly = false)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            if (_computer.Hardware.Count == 0)
            {
                return CpuTemperatureSnapshot.Empty;
            }

            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu ||
                    hardware.HardwareType == HardwareType.GpuNvidia ||
                    hardware.HardwareType == HardwareType.GpuAmd ||
                    hardware.HardwareType == HardwareType.GpuIntel)
                {
                    try
                    {
                        hardware.Update();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Sensor update skipped for {hardware.Name}: {ex.Message}");
                    }
                }
            }

            if (_packageSensor is null && _gpuSensor is null)
            {
                RefreshTelemetrySensorReferences();
            }

            double? package = _packageSensor?.Value;
            double? gpu = _gpuSensor?.Value;

            if (temperaturesOnly)
            {
                return new CpuTemperatureSnapshot(
                    package, null, Array.Empty<CoreTemperature>(), gpu, null,
                    null, null, null, null, null, null);
            }

            double? cpuLoad = _cpuLoadSensor?.Value;
            double? gpuLoad = _gpuLoadSensor?.Value;
            double? cpuPower = _cpuPowerSensor?.Value;
            double? cpuClock = _cpuClockSensor?.Value;
            double? cpuVoltage = _cpuVoltageSensor?.Value;
            double? gpuPower = _gpuPowerSensor?.Value;
            double? gpuClock = _gpuClockSensor?.Value;
            double? gpuVoltage = _gpuVoltageSensor?.Value ?? MsiAfterburnerReader.GetGpuVoltage();

            var cores = _coreSensors
                .Select((sensor, index) => new CoreTemperature(NormalizeCoreName(sensor.Name, index), sensor.Value))
                .ToList();

            return new CpuTemperatureSnapshot(
                package, cpuLoad, cores, gpu, gpuLoad,
                cpuPower, cpuClock, cpuVoltage,
                gpuPower, gpuClock, gpuVoltage
            );
        }
    }

    public IReadOnlyList<SensorSample> GetSamples()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var results = new List<SensorSample>();

            foreach (var hardware in _computer.Hardware)
            {
                CollectHardwareSamples(hardware, results);
            }

            return results;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _computer.Close();
            }
            catch
            {
                // Ignore shutdown errors
            }
        }
    }

    private void RefreshTelemetrySensorReferences()
    {
        _packageSensor = null;
        _cpuLoadSensor = null;
        _coreSensors = new List<ISensor>();
        _gpuSensor = null;
        _gpuLoadSensor = null;
        _cpuPowerSensor = null;
        _cpuClockSensor = null;
        _cpuVoltageSensor = null;
        _gpuPowerSensor = null;
        _gpuClockSensor = null;
        _gpuVoltageSensor = null;

        foreach (var hardware in _computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.Cpu &&
                hardware.HardwareType != HardwareType.GpuNvidia &&
                hardware.HardwareType != HardwareType.GpuAmd &&
                hardware.HardwareType != HardwareType.GpuIntel)
            {
                continue;
            }

            hardware.Accept(_visitor);
            hardware.Update();

            var tempSensors = hardware.Sensors.Where(static s => s.SensorType == SensorType.Temperature).ToList();
            var loadSensors = hardware.Sensors.Where(static s => s.SensorType == SensorType.Load).ToList();
            var powerSensors = hardware.Sensors.Where(static s => s.SensorType == SensorType.Power).ToList();
            var clockSensors = hardware.Sensors.Where(static s => s.SensorType == SensorType.Clock).ToList();
            var voltageSensors = hardware.Sensors.Where(static s => s.SensorType == SensorType.Voltage).ToList();

            if (hardware.HardwareType == HardwareType.Cpu)
            {
                _packageSensor ??= FindPackageSensor(tempSensors);
                _cpuLoadSensor ??= FindCpuLoadSensor(loadSensors);
                _cpuPowerSensor ??= FindPowerSensor(powerSensors);
                _cpuClockSensor ??= FindClockSensor(clockSensors);
                _cpuVoltageSensor ??= FindCpuVoltageSensor(voltageSensors);

                if (_coreSensors.Count == 0)
                {
                    _coreSensors = FindCoreSensors(tempSensors);
                }
            }
            else
            {
                _gpuSensor ??= FindGpuSensor(tempSensors);
                _gpuLoadSensor ??= FindGpuLoadSensor(loadSensors);
                _gpuPowerSensor ??= FindPowerSensor(powerSensors);
                _gpuClockSensor ??= FindClockSensor(clockSensors);
                _gpuVoltageSensor ??= FindGpuVoltageSensor(voltageSensors);
            }
        }
    }

    private static ISensor? FindPowerSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static ISensor? FindClockSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static ISensor? FindCpuVoltageSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("CPU Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("VCore", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("VID", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static ISensor? FindGpuVoltageSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU Voltage", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("VDDC", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static ISensor? FindPackageSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Die", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
    }

    private static ISensor? FindCpuLoadSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
    }

    private static ISensor? FindGpuSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static ISensor? FindGpuLoadSensor(IEnumerable<ISensor> sensors)
    {
        return sensors.FirstOrDefault(static s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault(static s => s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
               ?? sensors.FirstOrDefault();
    }

    private static List<ISensor> FindCoreSensors(IEnumerable<ISensor> sensors)
    {
        return sensors
            .Where(IsCoreTemperatureSensor)
            .OrderBy(static s => s.Identifier.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCoreTemperatureSensor(ISensor sensor)
    {
        if (sensor.SensorType != SensorType.Temperature) return false;
        var name = sensor.Name;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TjMax", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Average", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Min", StringComparison.OrdinalIgnoreCase)) return false;
        if (!name.Contains("Core", StringComparison.OrdinalIgnoreCase)) return false;
        return name.IndexOf('#') >= 0 || name.Any(char.IsDigit);
    }

    private static string NormalizeCoreName(string rawName, int index)
    {
        var baseName = $"Core {index + 1}";
        if (string.IsNullOrWhiteSpace(rawName)) return baseName;
        var trimmed = rawName.Trim();
        if (trimmed.Contains("Distance", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Average", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Min", StringComparison.OrdinalIgnoreCase)) return baseName;

        string? details = null;
        var parenStart = trimmed.IndexOf('(');
        var parenEnd = trimmed.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            details = trimmed.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
        }
        else if (!trimmed.StartsWith("Core", true, CultureInfo.InvariantCulture))
        {
            details = trimmed;
        }

        if (!string.IsNullOrWhiteSpace(details))
        {
            return $"{baseName} ({details})";
        }

        return baseName;
    }

    private void CollectHardwareSamples(IHardware hardware, List<SensorSample> results)
    {
        try
        {
            hardware.Accept(_visitor);
            hardware.Update();

            foreach (var sensor in hardware.Sensors)
            {
                if (!IsAllowed(sensor))
                {
                    continue;
                }

                results.Add(CreateSample(hardware, sensor));
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                CollectHardwareSamples(subHardware, results);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to collect sample for {hardware.Name}: {ex.Message}");
        }
    }

    private static bool IsAllowed(ISensor sensor)
    {
        return Array.IndexOf(AllowedTypes, sensor.SensorType) >= 0;
    }

    private static SensorSample CreateSample(IHardware hardware, ISensor sensor)
    {
        return new SensorSample(
            sensor.Identifier.ToString(),
            sensor.Name,
            hardware.Identifier.ToString(),
            hardware.Name,
            hardware.HardwareType,
            sensor.SensorType,
            sensor.Value,
            GetUnit(sensor.SensorType));
    }

    private static string? GetUnit(SensorType sensorType)
    {
        return sensorType switch
        {
            SensorType.Temperature => "°C",
            SensorType.Load => "%",
            SensorType.Clock => "MHz",
            SensorType.Power => "W",
            SensorType.Fan => "RPM",
            SensorType.Flow => "L/h",
            SensorType.Voltage => "V",
            _ => null
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HardwareMonitorService));
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            foreach (var hardware in computer.Hardware)
            {
                hardware.Accept(this);
            }
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();

            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
