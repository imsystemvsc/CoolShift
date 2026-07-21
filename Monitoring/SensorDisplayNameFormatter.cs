using System;
using System.Collections.Generic;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf.Monitoring;

internal readonly record struct SensorDisplayNameSegments(string Hardware, string Group, string Sensor);

internal static class SensorDisplayNameFormatter
{
    private static readonly IReadOnlyDictionary<HardwareType, string> HardwareTypeLabels = new Dictionary<HardwareType, string>
    {
        { HardwareType.Cpu, "CPU" },
        { HardwareType.GpuAmd, "GPU" },
        { HardwareType.GpuIntel, "GPU" },
        { HardwareType.GpuNvidia, "GPU" },
        { HardwareType.Memory, "Memory" },
        { HardwareType.Motherboard, "Motherboard" },
        { HardwareType.SuperIO, "Motherboard Sensors" },
        { HardwareType.Storage, "Storage Drive" },
        { HardwareType.Network, "Network Adapter" },
        { HardwareType.Cooler, "Cooling" },
        { HardwareType.EmbeddedController, "Embedded Controller" },
        { HardwareType.Psu, "Power Supply" },
        { HardwareType.Battery, "Battery" }
    };

    private static readonly IReadOnlyDictionary<SensorType, string> SensorTypeLabels = new Dictionary<SensorType, string>
    {
        { SensorType.Temperature, "Temperature" },
        { SensorType.Load, "Usage" },
        { SensorType.Clock, "Clock Speed" },
        { SensorType.Power, "Power Draw" },
        { SensorType.Fan, "Fan Speed" },
        { SensorType.Flow, "Flow Rate" },
        { SensorType.Voltage, "Voltage" },
        { SensorType.Current, "Current" },
        { SensorType.Level, "Level" },
        { SensorType.Control, "Control" },
        { SensorType.Frequency, "Frequency" },
        { SensorType.Throughput, "Throughput" },
        { SensorType.Data, "Value" },
        { SensorType.SmallData, "Value" },
        { SensorType.Energy, "Energy" },
        { SensorType.Factor, "Factor" },
        { SensorType.Noise, "Noise" },
        { SensorType.TimeSpan, "Duration" },
        { SensorType.Conductivity, "Conductivity" },
        { SensorType.Humidity, "Humidity" }
    };

    private static readonly IReadOnlyDictionary<string, int> CategoryOrderLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "CPU", 0 },
        { "GPU", 1 },
        { "Memory", 2 },
        { "Motherboard", 3 },
        { "Motherboard Sensors", 4 },
        { "Cooling", 5 },
        { "Power Supply", 6 },
        { "Storage Drive", 7 },
        { "Network Adapter", 8 },
        { "Battery", 9 },
        { "Embedded Controller", 10 }
    };

    private static readonly IReadOnlyDictionary<SensorType, string> SensorGroupLabels = new Dictionary<SensorType, string>
    {
        { SensorType.Temperature, "Temperatures" },
        { SensorType.Voltage, "Voltages" },
        { SensorType.Power, "Powers" },
        { SensorType.Clock, "Clocks" },
        { SensorType.Load, "Load" },
        { SensorType.Fan, "Fans" },
        { SensorType.Flow, "Flows" },
        { SensorType.Control, "Control" },
        { SensorType.Level, "Levels" },
        { SensorType.Current, "Currents" },
        { SensorType.Frequency, "Frequencies" },
        { SensorType.Throughput, "Throughput" },
        { SensorType.Data, "Data" },
        { SensorType.SmallData, "Data" },
        { SensorType.Energy, "Energy" },
        { SensorType.Factor, "Factors" },
        { SensorType.Noise, "Noise" },
        { SensorType.TimeSpan, "Time" },
        { SensorType.Conductivity, "Conductivity" },
        { SensorType.Humidity, "Humidity" }
    };
    public static string BuildDisplayName(SensorSample sample)
    {
        var segments = BuildSegments(sample);
        return JoinSegments(segments);
    }

    internal static SensorDisplayNameSegments BuildSegments(SensorSample sample)
    {
        var hardware = BuildHardwareLabel(sample);
        var group = GetGroupLabel(sample.SensorType);
        var sensor = BuildSensorLabel(sample);
        return new SensorDisplayNameSegments(hardware, group, sensor);
    }

    internal static string JoinSegments(in SensorDisplayNameSegments segments)
    {
        var parts = new List<string>(3);
        AddDistinctPart(parts, segments.Hardware);
        AddDistinctPart(parts, segments.Group);
        AddDistinctPart(parts, segments.Sensor);
        return string.Join(" / ", parts);
    }

    internal static string GetGroupLabel(SensorType sensorType)
    {
        if (SensorGroupLabels.TryGetValue(sensorType, out var label))
        {
            return label;
        }

        if (SensorTypeLabels.TryGetValue(sensorType, out var friendly))
        {
            return friendly;
        }

        return SplitCamelCase(sensorType.ToString());
    }

    private static void AddDistinctPart(List<string> parts, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (parts.Count > 0 && string.Equals(parts[^1], value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        parts.Add(value);
    }

    private static string BuildHardwareLabel(SensorSample sample)
    {
        var hardwareName = NormalizeWhitespace(sample.HardwareName);
        if (hardwareName.Length > 0)
        {
            return hardwareName;
        }

        return GetHardwareCategoryLabel(sample.HardwareType);
    }

    internal static (string Category, int Order) GetHardwareCategoryInfo(HardwareType hardwareType)
    {
        var category = GetHardwareCategoryLabel(hardwareType);
        if (!CategoryOrderLookup.TryGetValue(category, out var order))
        {
            order = CategoryOrderLookup.Count + (int)hardwareType;
        }

        return (category, order);
    }

    private static string GetHardwareCategoryLabel(HardwareType hardwareType)
    {
        if (HardwareTypeLabels.TryGetValue(hardwareType, out var friendly))
        {
            return friendly;
        }

        return SplitCamelCase(hardwareType.ToString());
    }

    private static string BuildSensorLabel(SensorSample sample)
    {
        var rawName = sample.SensorName;
        if (!string.IsNullOrWhiteSpace(rawName))
        {
            return NormalizeSensorName(rawName);
        }

        if (SensorTypeLabels.TryGetValue(sample.SensorType, out var friendly))
        {
            return friendly;
        }

        return SplitCamelCase(sample.SensorType.ToString());
    }

    private static string NormalizeSensorName(string value)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length + 4);
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == '#' && i > 0 && normalized[i - 1] != ' ')
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static string SplitCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 4);
        builder.Append(value[0]);

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            var previous = value[i - 1];

            if (char.IsUpper(ch) && !char.IsWhiteSpace(previous) && !char.IsUpper(previous))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}


