using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CoolShift.Monitoring;

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
