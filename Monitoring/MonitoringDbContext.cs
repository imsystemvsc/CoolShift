using Microsoft.EntityFrameworkCore;

namespace CoolShift.Monitoring;

internal sealed class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
        : base(options)
    {
    }

    public DbSet<SensorPreferenceEntity> SensorPreferences => Set<SensorPreferenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SensorPreferenceEntity>(entity =>
        {
            entity.ToTable("sensor_preferences");
            entity.HasKey(e => e.SensorId);
            entity.Property(e => e.SensorId)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(e => e.IsSelected)
                .IsRequired();
            entity.Property(e => e.Category)
                .HasMaxLength(128);
            entity.Property(e => e.SortOrder)
                .IsRequired();
        });

    }
}
