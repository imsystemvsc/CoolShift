using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf;

internal sealed class CpuTemperatureService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _sync = new();

    private ISensor? _packageSensor;
    private ISensor? _cpuLoadSensor;
    private List<ISensor> _coreSensors = new();
    private ISensor? _gpuSensor;
    private ISensor? _gpuLoadSensor;
    private bool _disposed;

    public CpuTemperatureService()
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
            IsBatteryEnabled = false
        };

        _computer.Open();
        RefreshSensorReferences();
    }

    public CpuTemperatureSnapshot GetSnapshot()
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
                    hardware.Update();
                }
            }

            if (_packageSensor is null && _coreSensors.Count == 0 && _gpuSensor is null && _cpuLoadSensor is null && _gpuLoadSensor is null)
            {
                RefreshSensorReferences();
            }

            double? package = _packageSensor?.Value;
            double? cpuLoad = _cpuLoadSensor?.Value;
            double? gpu = _gpuSensor?.Value;
            double? gpuLoad = _gpuLoadSensor?.Value;
            var cores = _coreSensors
                .Select((sensor, index) => new CoreTemperature(NormalizeCoreName(sensor.Name, index), sensor.Value))
                .ToList();

            return new CpuTemperatureSnapshot(package, cpuLoad, cores, gpu, gpuLoad);
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
                // Swallow any shutdown exceptions from LibreHardwareMonitor
            }
        }
    }

    private void RefreshSensorReferences()
    {
        _packageSensor = null;
        _cpuLoadSensor = null;
        _coreSensors = new List<ISensor>();
        _gpuSensor = null;
        _gpuLoadSensor = null;

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

            var temperatureSensors = hardware.Sensors
                .Where(static s => s.SensorType == SensorType.Temperature)
                .ToList();
                
            var loadSensors = hardware.Sensors
                .Where(static s => s.SensorType == SensorType.Load)
                .ToList();

            if (hardware.HardwareType == HardwareType.Cpu)
            {
                _packageSensor ??= FindPackageSensor(temperatureSensors);
                _cpuLoadSensor ??= FindCpuLoadSensor(loadSensors);
                
                if (_coreSensors.Count == 0)
                {
                    _coreSensors = FindCoreSensors(temperatureSensors);
                }

                if (_coreSensors.Count == 0 || _cpuLoadSensor is null)
                {
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Accept(_visitor);
                        subHardware.Update();

                        var subTempSensors = subHardware.Sensors
                            .Where(static s => s.SensorType == SensorType.Temperature)
                            .ToList();
                            
                        var subLoadSensors = subHardware.Sensors
                            .Where(static s => s.SensorType == SensorType.Load)
                            .ToList();

                        _packageSensor ??= FindPackageSensor(subTempSensors);
                        _cpuLoadSensor ??= FindCpuLoadSensor(subLoadSensors);

                        var subCores = FindCoreSensors(subTempSensors);
                        if (subCores.Count > 0)
                        {
                            _coreSensors.AddRange(subCores);
                        }
                    }
                }
            }
            else
            {
                _gpuSensor ??= FindGpuSensor(temperatureSensors);
                _gpuLoadSensor ??= FindGpuLoadSensor(loadSensors);
            }
        }
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
        if (sensor.SensorType != SensorType.Temperature)
        {
            return false;
        }

        var name = sensor.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Contains("Distance", StringComparison.OrdinalIgnoreCase)
            || name.Contains("TjMax", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Average", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Max", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Min", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!name.Contains("Core", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.IndexOf('#') >= 0 || name.Any(char.IsDigit);
    }


    private static string NormalizeCoreName(string rawName, int index)
    {
        var baseName = $"Core {index + 1}";

        if (string.IsNullOrWhiteSpace(rawName))
        {
            return baseName;
        }

        var trimmed = rawName.Trim();

        if (trimmed.Contains("Distance", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Average", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Max", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Min", StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CpuTemperatureService));
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

internal readonly record struct CoreTemperature(string Name, double? Celsius);

internal readonly record struct CpuTemperatureSnapshot(double? PackageCelsius, double? CpuLoad, IReadOnlyList<CoreTemperature> Cores, double? GpuCelsius, double? GpuLoad)
{
    public static readonly CpuTemperatureSnapshot Empty = new(null, null, Array.Empty<CoreTemperature>(), null, null);
}
