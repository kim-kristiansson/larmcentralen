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
    public DbSet<AlarmCode> AlarmCodes => Set<AlarmCode>();
    public DbSet<TrashBatch> TrashBatches => Set<TrashBatch>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite stores dates as text without a time zone. Every date in this app is written
        // as UTC, so mark them as UTC when read back and the API keeps sending "Z" times.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The bin (Papperskorg): rows marked with a TrashBatchId are hidden from every query, so the API, the sync
        // snapshot (and with it every offline PC) and search all leave them out. Only TrashRepository looks past
        // the filters, with IgnoreQueryFilters().
        modelBuilder.Entity<TrashBatch>(e =>
        {
            e.ToTable("TrashBatches");
            e.Property(b => b.Kind).HasMaxLength(20);
            e.Property(b => b.Title).HasMaxLength(200);
            e.Property(b => b.AreaTitle).HasMaxLength(100);
        });
        modelBuilder.Entity<Area>(e =>
        {
            e.HasQueryFilter(a => a.TrashBatchId == null);
            e.HasOne<TrashBatch>().WithMany().HasForeignKey(a => a.TrashBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Equipment>(e =>
        {
            e.HasQueryFilter(x => x.TrashBatchId == null);
            e.HasOne<TrashBatch>().WithMany().HasForeignKey(x => x.TrashBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Alarm>(e =>
        {
            e.HasQueryFilter(a => a.TrashBatchId == null);
            e.HasOne<TrashBatch>().WithMany().HasForeignKey(a => a.TrashBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Solution>(e =>
        {
            e.HasQueryFilter(s => s.TrashBatchId == null);
            e.HasOne<TrashBatch>().WithMany().HasForeignKey(s => s.TrashBatchId).OnDelete(DeleteBehavior.Restrict);
        });

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

        modelBuilder.Entity<AlarmCode>(e =>
        {
            e.ToTable("AlarmCodes");
            // Stored upper case (AlarmText.NormaliseCode), so a plain unique index is case-insensitive in practice.
            e.Property(c => c.Code).HasMaxLength(50);
            e.HasIndex(c => c.Code).IsUnique();
        });

        modelBuilder.Entity<Alarm>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.Severity).HasMaxLength(20);

            e.HasOne(a => a.Equipment)
                .WithMany(eq => eq.Alarms)
                .HasForeignKey(a => a.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.AlarmCode)
                .WithMany(c => c.Alarms)
                .HasForeignKey(a => a.AlarmCodeId)
                .OnDelete(DeleteBehavior.Restrict);

            // One alarm per code on a unit. SQLite treats NULLs as distinct, so alarms without a code are not limited.
            e.HasIndex(a => new { a.EquipmentId, a.AlarmCodeId }).IsUnique();
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
                        // A link counts only while both ends are out of the bin.
                        j.HasQueryFilter(x => x.Alarm.TrashBatchId == null && x.Solution.TrashBatchId == null);
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
