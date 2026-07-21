using System;

namespace ParkToggleWpf.Monitoring;

internal sealed class SensorReadingEntity
{
    public long Id { get; set; }
    public required string SensorId { get; set; }
    public string? SensorName { get; set; }
    public required string HardwareId { get; set; }
    public string? HardwareName { get; set; }
    public required string HardwareType { get; set; }
    public required string SensorType { get; set; }
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
