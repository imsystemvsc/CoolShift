using System;

namespace CoolShift.Monitoring;

internal sealed class SensorPreferenceEntity
{
    public string SensorId { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
