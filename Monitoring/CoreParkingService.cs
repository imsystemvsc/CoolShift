using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ParkToggleWpf.Monitoring;

public class CoreParkingService : IDisposable
{
    private readonly Dictionary<string, PerformanceCounter> _counters = new();

    public CoreParkingService()
    {
        InitializeCounters();
    }

    private void InitializeCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("Processor Information");
            var instances = category.GetInstanceNames()
                .Where(name => !name.Contains("_Total", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name =>
                {
                    // Sort instance strings like "0,0", "0,1", "0,10" numerically by the second part
                    var parts = name.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int num))
                        return num;
                    return 0;
                })
                .ToList();

            foreach (var instance in instances)
            {
                if (category.InstanceExists(instance) && category.CounterExists("Parking Status"))
                {
                    var counter = new PerformanceCounter("Processor Information", "Parking Status", instance, true);
                    
                    // Call NextValue once to initialize the counter.
                    _ = counter.NextValue();
                    
                    _counters.Add(instance, counter);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to initialize CoreParkingService: {ex}");
        }
    }

    public Dictionary<string, bool> GetParkedStates()
    {
        var states = new Dictionary<string, bool>();
        foreach (var kvp in _counters)
        {
            try
            {
                float val = kvp.Value.NextValue();
                states[kvp.Key] = val > 0;
            }
            catch
            {
                states[kvp.Key] = false;
            }
        }
        return states;
    }

    public void Dispose()
    {
        foreach (var counter in _counters.Values)
        {
            counter.Dispose();
        }
        _counters.Clear();
    }
}
