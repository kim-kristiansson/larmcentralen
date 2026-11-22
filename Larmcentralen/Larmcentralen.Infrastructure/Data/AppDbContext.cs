using Larmcentralen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Larmcentralen.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<Solution> Solutions => Set<Solution>();
    public DbSet<AlarmSolution> AlarmSolutions => Set<AlarmSolution>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite stores dates as text without a time zone. Every date in this app is written
        // as UTC, so mark them as UTC when read back and the API keeps sending "Z" times.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Area>(e =>
        {
            e.HasIndex(a => a.Title).IsUnique();
            e.Property(a => a.Title).HasMaxLength(100);
        });

        modelBuilder.Entity<Equipment>(e =>
        {
            e.Property(eq => eq.Title).HasMaxLength(200);
            e.Property(eq => eq.Category).HasMaxLength(50);
            e.Property(eq => eq.EquipmentType).HasMaxLength(50);

            e.HasOne(eq => eq.Area)
                .WithMany(a => a.Equipment)
                .HasForeignKey(eq => eq.AreaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Alarm>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.AlarmCode).HasMaxLength(50);
            e.Property(a => a.Severity).HasMaxLength(20);

            e.HasOne(a => a.Equipment)
                .WithMany(eq => eq.Alarms)
                .HasForeignKey(a => a.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(a => new { a.AlarmCode, a.EquipmentId }).IsUnique();
        });

        modelBuilder.Entity<Solution>(e =>
        {
            e.Property(s => s.Title).HasMaxLength(300);
            e.Property(s => s.EstimatedTime).HasMaxLength(50);

            // Many-to-many: one solution can be shared by several alarms (the same fault on several units).
            e.HasMany(s => s.Alarms)
                .WithMany(a => a.Solutions)
                .UsingEntity<AlarmSolution>(
                    l => l.HasOne(x => x.Alarm).WithMany().HasForeignKey(x => x.AlarmId).OnDelete(DeleteBehavior.Cascade),
                    r => r.HasOne(x => x.Solution).WithMany().HasForeignKey(x => x.SolutionId).OnDelete(DeleteBehavior.Cascade),
                    j =>
                    {
                        j.ToTable("AlarmSolutions");
                        j.HasKey(x => new { x.AlarmId, x.SolutionId });
                        j.HasIndex(x => x.SolutionId);
                    });
        });
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

internal sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
