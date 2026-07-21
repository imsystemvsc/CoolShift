using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ParkToggleWpf.Monitoring;

internal sealed class MonitoringRepository
{
    private readonly DbContextOptions<MonitoringDbContext> _options;

    public MonitoringRepository(MonitoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseSqlite($"Data Source={options.DatabasePath}")
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false);

        _options = builder.Options;

        using var context = new MonitoringDbContext(_options);
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS sensor_preferences (SensorId TEXT PRIMARY KEY, IsSelected INTEGER NOT NULL, Category TEXT, SortOrder INTEGER NOT NULL DEFAULT 0);");
        try
        {
            context.Database.ExecuteSqlRaw("ALTER TABLE sensor_preferences ADD COLUMN Category TEXT;");
        }
        catch
        {
        }

        try
        {
            context.Database.ExecuteSqlRaw("ALTER TABLE sensor_preferences ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
        }

        context.Database.ExecuteSqlRaw("UPDATE sensor_preferences SET Category = '' WHERE Category IS NULL;");
        context.Database.ExecuteSqlRaw("UPDATE sensor_preferences SET SortOrder = 0 WHERE SortOrder IS NULL;");
    }

    public async Task SaveSampleAsync(MonitoringSample sample, CancellationToken cancellationToken)
    {
        if (sample.Samples.Count == 0)
        {
            return;
        }

        await using var context = new MonitoringDbContext(_options);
        var timestamp = sample.Timestamp;

        var entities = new List<SensorReadingEntity>(sample.Samples.Count);
        foreach (var sensor in sample.Samples)
        {
            double? value = sensor.Value;
            if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
            {
                value = null;
            }

            entities.Add(new SensorReadingEntity
            {
                SensorId = sensor.SensorId,
                SensorName = sensor.SensorName,
                HardwareId = sensor.HardwareId,
                HardwareName = sensor.HardwareName,
                HardwareType = sensor.HardwareType.ToString(),
                SensorType = sensor.SensorType.ToString(),
                Unit = sensor.Unit,
                Value = value,
                Timestamp = timestamp
            });
        }

        await context.SensorReadings.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SensorReadingEntity>> GetReadingsAsync(
        string sensorId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        await using var context = new MonitoringDbContext(_options);

        var readings = await context.SensorReadings
            .Where(r => r.SensorId == sensorId && r.Timestamp >= from && r.Timestamp <= to)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (maxPoints <= 0 || readings.Count <= maxPoints)
        {
            return readings;
        }

        var step = Math.Max(1, readings.Count / maxPoints);
        var trimmed = new List<SensorReadingEntity>(Math.Min(maxPoints, readings.Count));

        for (var i = 0; i < readings.Count; i += step)
        {
            trimmed.Add(readings[i]);
        }

        if (trimmed.Count > maxPoints)
        {
            trimmed.RemoveRange(maxPoints, trimmed.Count - maxPoints);
        }

        return trimmed;
    }

    public async Task<IReadOnlyList<SensorMetadata>> GetLatestSensorMetadataAsync(CancellationToken cancellationToken)
    {
        await using var context = new MonitoringDbContext(_options);

        var latest = await context.SensorReadings
            .GroupBy(r => r.SensorId)
            .Select(g => g.OrderByDescending(r => r.Timestamp).First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return latest
            .Select(r => new SensorMetadata(r.SensorId, r.SensorName, r.HardwareName, r.HardwareType, r.SensorType, r.Unit))
            .ToList();
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var context = new MonitoringDbContext(_options);
        return await context.SensorReadings
            .Where(r => r.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SensorPreferenceEntity>> GetSensorPreferencesAsync(CancellationToken cancellationToken)
    {
        await using var context = new MonitoringDbContext(_options);
        return await context.SensorPreferences
            .Where(p => p.IsSelected)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SaveSensorPreferencesAsync(IEnumerable<(string SensorId, bool IsSelected, string Category, int SortOrder)> preferences, CancellationToken cancellationToken)
    {
        await using var context = new MonitoringDbContext(_options);
        await context.SensorPreferences.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var selected = preferences
            .Where(p => p.IsSelected)
            .Select(p => new SensorPreferenceEntity
            {
                SensorId = p.SensorId,
                IsSelected = true,
                Category = p.Category,
                SortOrder = p.SortOrder
            })
            .ToList();

        if (selected.Count > 0)
        {
            await context.SensorPreferences.AddRangeAsync(selected, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
