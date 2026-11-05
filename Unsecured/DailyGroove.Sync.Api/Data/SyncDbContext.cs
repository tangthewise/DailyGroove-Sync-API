using Microsoft.EntityFrameworkCore;

namespace DailyGroove.Sync.Api.Data;

public class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<ExerciseEntity> Exercises => Set<ExerciseEntity>();
    public DbSet<SessionRecordEntity> SessionRecords => Set<SessionRecordEntity>();
    public DbSet<ActivityResultEntity> ActivityResults => Set<ActivityResultEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Client-generated GUIDs: never let the database generate keys.
        modelBuilder.Entity<ExerciseEntity>(e => { e.ToTable("Exercises"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); });
        modelBuilder.Entity<SessionRecordEntity>(e => { e.ToTable("SessionRecords"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); });
        modelBuilder.Entity<ActivityResultEntity>(e => { e.ToTable("ActivityResults"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => x.UserId); e.HasIndex(x => x.SessionRecordId); });
    }
}
