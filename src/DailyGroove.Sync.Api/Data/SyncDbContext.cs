using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DailyGroove.Sync.Api.Data;

public class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<ExerciseEntity> Exercises => Set<ExerciseEntity>();
    public DbSet<SessionRecordEntity> SessionRecords => Set<SessionRecordEntity>();
    public DbSet<ActivityResultEntity> ActivityResults => Set<ActivityResultEntity>();

    // EF materialises datetime2 as DateTimeKind.Unspecified, which serialises without a 'Z' and lets
    // clients read a UTC time as local. Every timestamp here is UTC, so say so on the way in and out.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Client-generated GUIDs: never let the database generate keys.
        modelBuilder.Entity<ExerciseEntity>(e => { e.ToTable("Exercises"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); });
        modelBuilder.Entity<SessionRecordEntity>(e => { e.ToTable("SessionRecords"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); });
        modelBuilder.Entity<ActivityResultEntity>(e => { e.ToTable("ActivityResults"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); e.HasIndex(x => x.SessionRecordId); });
    }
}
