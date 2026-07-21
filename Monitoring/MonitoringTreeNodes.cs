using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace ParkToggleWpf.Monitoring;

public abstract class MonitoringTreeNode
{
    protected MonitoringTreeNode(string name)
    {
        Name = name;
        Children = new ObservableCollection<MonitoringTreeNode>();
    }

    public string Name { get; }

    public ObservableCollection<MonitoringTreeNode> Children { get; }

    public bool HasChildren => Children.Count > 0;
}

public sealed class MonitoringHardwareNode : MonitoringTreeNode
{
    public MonitoringHardwareNode(string hardwareId, string name, HardwareType hardwareType)
        : base(name)
    {
        HardwareId = hardwareId;
        HardwareType = hardwareType;
    }

    public string HardwareId { get; }

    public HardwareType HardwareType { get; }
}

public sealed class MonitoringSensorGroupNode : MonitoringTreeNode
{
    public MonitoringSensorGroupNode(string name, SensorType sensorType)
        : base(name)
    {
        SensorType = sensorType;
    }

    public SensorType SensorType { get; }
}

public sealed class MonitoringSensorLeafNode : MonitoringTreeNode
{
    public MonitoringSensorLeafNode(SensorSelectionViewModel sensor)
        : base(sensor.SensorDisplayName)
    {
        Sensor = sensor;
    }

    public SensorSelectionViewModel Sensor { get; }
}

internal static class MonitoringTreeBuilder
{
    public static IReadOnlyList<MonitoringTreeNode> Build(IEnumerable<SensorSelectionViewModel> sensors)
    {
        var result = new List<MonitoringTreeNode>();

        var hardwareGroups = sensors
            .GroupBy(sensor => sensor.HardwareId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => GetSafeKey(group.FirstOrDefault()?.HardwareDisplayName))
            .ThenBy(group => GetSafeKey(group.Key));

        foreach (var hardwareGroup in hardwareGroups)
        {
            var first = hardwareGroup.FirstOrDefault();
            if (first is null)
            {
                continue;
            }

            var hardwareName = first.HardwareDisplayName;
            if (string.IsNullOrWhiteSpace(hardwareName))
            {
                hardwareName = first.HardwareName;
            }

            if (string.IsNullOrWhiteSpace(hardwareName))
            {
                hardwareName = first.HardwareType.ToString();
            }

            var hardwareNode = new MonitoringHardwareNode(first.HardwareId, hardwareName, first.HardwareType);

            var sensorGroups = hardwareGroup
                .GroupBy(sensor => sensor.SensorType)
                .OrderBy(group => GetSafeKey(SensorDisplayNameFormatter.GetGroupLabel(group.Key)))
                .ThenBy(group => group.Key.ToString());

            foreach (var group in sensorGroups)
            {
                var groupName = SensorDisplayNameFormatter.GetGroupLabel(group.Key);
                var groupNode = new MonitoringSensorGroupNode(groupName, group.Key);

                var orderedSensors = group
                    .OrderBy(sensor => GetSafeKey(sensor.SensorDisplayName))
                    .ThenBy(sensor => sensor.SensorId, StringComparer.OrdinalIgnoreCase);

                foreach (var sensor in orderedSensors)
                {
                    groupNode.Children.Add(new MonitoringSensorLeafNode(sensor));
                }

                hardwareNode.Children.Add(groupNode);
            }

            result.Add(hardwareNode);
        }

        return result;
    }

    private static string GetSafeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }
}

