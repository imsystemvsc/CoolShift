using System;
using System.IO;

namespace ParkToggleWpf.Monitoring;

internal sealed class MonitoringOptions
{
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(7);
    public int LiveBufferCapacity { get; init; } = 256;
    public int HistoricalMaxPoints { get; init; } = 720;
    public string DatabasePath { get; init; } = BuildDefaultDatabasePath();

    public static MonitoringOptions CreateDefault()
    {
        return new MonitoringOptions();
    }

    private static string BuildDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var storageRoot = Path.Combine(appData, "ParkToggle", "Monitoring");
        Directory.CreateDirectory(storageRoot);
        return Path.Combine(storageRoot, "monitoring.db");
    }
}
