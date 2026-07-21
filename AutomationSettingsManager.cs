using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ParkToggleWpf;

public static class AutomationSettingsManager
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "automation_settings.json");

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
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to save automation settings: {ex.Message}");
        }
    }
}
