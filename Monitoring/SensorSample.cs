using LibreHardwareMonitor.Hardware;

namespace CoolShift.Monitoring;

internal readonly record struct SensorSample(
    string SensorId,
    string SensorName,
    string HardwareId,
    string HardwareName,
    HardwareType HardwareType,
    SensorType SensorType,
    double? Value,
    string? Unit);
