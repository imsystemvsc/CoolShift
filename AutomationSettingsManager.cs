using System;
using System.IO;
using System.Text.Json;

namespace CoolShift;

public static class AutomationSettingsManager
{
    private static readonly string SettingsPath = PrepareSettingsPath();

    public static AutomationOptions Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AutomationOptions>(json) ?? new AutomationOptions();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to load automation settings: {ex.Message}");
        }
        
        return new AutomationOptions();
    }

    public static void Save(AutomationOptions options)
    {
        try
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to save automation settings: {ex.Message}");
        }
    }

    private static string PrepareSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "CoolShift");
        Directory.CreateDirectory(settingsDirectory);

        var settingsPath = Path.Combine(settingsDirectory, "automation_settings.json");
        if (File.Exists(settingsPath))
        {
            return settingsPath;
        }

        var legacyCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "automation_settings.json"),
            Path.Combine(appData, "ParkToggle", "automation_settings.json"),
        };

        var legacyPath = legacyCandidates.FirstOrDefault(File.Exists);
        if (legacyPath is not null)
        {
            File.Copy(legacyPath, settingsPath, overwrite: false);
        }

        return settingsPath;
    }
}
