using Microsoft.EntityFrameworkCore;

namespace ParkToggleWpf.Monitoring;

internal sealed class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
        : base(options)
    {
    }

    public DbSet<SensorReadingEntity> SensorReadings => Set<SensorReadingEntity>();
    public DbSet<SensorPreferenceEntity> SensorPreferences => Set<SensorPreferenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SensorReadingEntity>(entity =>
        {
            entity.ToTable("sensor_readings");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.SensorId)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(e => e.HardwareId)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(e => e.HardwareType)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.SensorType)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.Unit)
                .HasMaxLength(32);

            entity.HasIndex(e => new { e.SensorId, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
        });
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
