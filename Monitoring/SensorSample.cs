using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf.Monitoring;

internal readonly record struct SensorSample(
    string SensorId,
    string SensorName,
    string HardwareId,
    string HardwareName,
    HardwareType HardwareType,
    SensorType SensorType,
    double? Value,
    string? Unit);
