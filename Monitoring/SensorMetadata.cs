namespace ParkToggleWpf.Monitoring;

internal sealed record SensorMetadata(
    string SensorId,
    string? SensorName,
    string? HardwareName,
    string HardwareType,
    string SensorType,
    string? Unit);
