using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf.Monitoring;

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

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsBatteryEnabled = true,
            IsPsuEnabled = true
        };

        _computer.Open();
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

    private void CollectHardwareSamples(IHardware hardware, List<SensorSample> results)
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

    private static bool IsAllowed(ISensor sensor)
    {
        if (Array.IndexOf(AllowedTypes, sensor.SensorType) < 0)
        {
            return false;
        }

        return true;
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
