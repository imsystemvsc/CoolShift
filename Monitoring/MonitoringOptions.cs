using System;
using System.IO;

namespace CoolShift.Monitoring;

internal sealed class MonitoringOptions
{
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(1);
    public string DatabasePath { get; init; } = BuildDefaultDatabasePath();

    public static MonitoringOptions CreateDefault()
    {
        return new MonitoringOptions();
    }

    private static string BuildDefaultDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var storageRoot = Path.Combine(appData, "CoolShift", "Monitoring");
        Directory.CreateDirectory(storageRoot);

        var databasePath = Path.Combine(storageRoot, "monitoring.db");
        var legacyDatabasePath = Path.Combine(appData, "ParkToggle", "Monitoring", "monitoring.db");
        if (!File.Exists(databasePath) && File.Exists(legacyDatabasePath))
        {
            File.Copy(legacyDatabasePath, databasePath, overwrite: false);
        }

        return databasePath;
    }
}
