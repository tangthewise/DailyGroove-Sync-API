using System.Buffers.Binary;
using System.Collections;
using DailyGroove.Sync.Api.Data;
using DailyGroove.Sync.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DailyGroove.Sync.Api;

/// <summary>
/// DELIBERATELY INSECURE baseline sync endpoints. See README.md for the catalogue of flaws.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sync/push", PushAsync);
        app.MapGet("/sync/pull", PullAsync);
    }

    // ── Push ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> PushAsync(PushRequest request, SyncDbContext db)
    {
        int applied = 0, ignored = 0;

        void Tally(bool wasApplied) { if (wasApplied) applied++; else ignored++; }

        foreach (var dto in request.Exercises)
            Tally(await UpsertAsync(db.Exercises, dto.Id, dto.UpdatedAtUtc,
                () => new ExerciseEntity { Id = dto.Id },
                e => { Map(dto, e); }));

        foreach (var dto in request.SessionRecords)
            Tally(await UpsertAsync(db.SessionRecords, dto.Id, dto.UpdatedAtUtc,
                () => new SessionRecordEntity { Id = dto.Id },
                e => { Map(dto, e); }));

        foreach (var dto in request.ActivityResults)
            Tally(await UpsertAsync(db.ActivityResults, dto.Id, dto.UpdatedAtUtc,
                () => new ActivityResultEntity { Id = dto.Id },
                e => { Map(dto, e); }));

        await db.SaveChangesAsync();

        return Results.Ok(new PushResponse
        {
            Applied = applied,
            Ignored = ignored,
            Cursor = await MaxVersionAsync(db, request.UserId),
        });
    }

    /// <summary>Last-write-wins upsert: apply only if the incoming timestamp is not older.</summary>
    private static async Task<bool> UpsertAsync<TEntity>(
        DbSet<TEntity> set, Guid id, DateTime incomingUpdatedAtUtc,
        Func<TEntity> create, Action<TEntity> apply)
        where TEntity : SyncEntity
    {
        var existing = await set.FirstOrDefaultAsync(e => e.Id == id);

        if (existing is null)
        {
            var created = create();
            apply(created);
            set.Add(created);
            return true;
        }

        if (incomingUpdatedAtUtc < existing.UpdatedAtUtc)
            return false;

        apply(existing);
        return true;
    }

    // ── Pull ─────────────────────────────────────────────────────────────────

    private static async Task<IResult> PullAsync(Guid userId, long since, SyncDbContext db)
    {
        var sinceBytes = ToVersionBytes(since);

        var exercises = await db.Exercises
            .Where(e => e.UserId == userId && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0)
            .OrderBy(e => e.RowVersion).ToListAsync();
        var records = await db.SessionRecords
            .Where(e => e.UserId == userId && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0)
            .OrderBy(e => e.RowVersion).ToListAsync();
        var results = await db.ActivityResults
            .Where(e => e.UserId == userId && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0)
            .OrderBy(e => e.RowVersion).ToListAsync();

        var cursor = new[] { since }
            .Concat(exercises.Select(e => ToVersion(e.RowVersion)))
            .Concat(records.Select(e => ToVersion(e.RowVersion)))
            .Concat(results.Select(e => ToVersion(e.RowVersion)))
            .Max();

        return Results.Ok(new PullResponse
        {
            Cursor = cursor,
            Exercises = exercises.Select(ToDto).ToList(),
            SessionRecords = records.Select(ToDto).ToList(),
            ActivityResults = results.Select(ToDto).ToList(),
        });
    }

    private static async Task<long> MaxVersionAsync(SyncDbContext db, Guid userId)
    {
        var versions = new List<long>();
        foreach (var rv in await db.Exercises.Where(e => e.UserId == userId).Select(e => e.RowVersion).ToListAsync()) versions.Add(ToVersion(rv));
        foreach (var rv in await db.SessionRecords.Where(e => e.UserId == userId).Select(e => e.RowVersion).ToListAsync()) versions.Add(ToVersion(rv));
        foreach (var rv in await db.ActivityResults.Where(e => e.UserId == userId).Select(e => e.RowVersion).ToListAsync()) versions.Add(ToVersion(rv));
        return versions.Count == 0 ? 0 : versions.Max();
    }

    // ── rowversion <-> long cursor (8 bytes, big-endian) ─────────────────────

    private static long ToVersion(byte[] rowVersion) =>
        rowVersion.Length == 8 ? BinaryPrimitives.ReadInt64BigEndian(rowVersion) : 0;

    private static byte[] ToVersionBytes(long version)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, version);
        return bytes;
    }

    // ── DTO <-> entity mapping ───────────────────────────────────────────────
    // INSECURE BY DESIGN: every client-supplied field, including UserId and UpdatedAtUtc, is
    // copied straight onto the entity.

    private static void Map(ExerciseDto d, ExerciseEntity e)
    {
        e.UserId = d.UserId; e.UpdatedAtUtc = d.UpdatedAtUtc; e.IsDeleted = d.IsDeleted;
        e.Title = d.Title; e.Category = d.Category; e.Weight = d.Weight; e.BodyParts = d.BodyParts;
        e.BodyweightFraction = d.BodyweightFraction; e.CreatedAtUtc = d.CreatedAtUtc;
    }

    private static void Map(SessionRecordDto d, SessionRecordEntity e)
    {
        e.UserId = d.UserId; e.UpdatedAtUtc = d.UpdatedAtUtc; e.IsDeleted = d.IsDeleted;
        e.SessionId = d.SessionId; e.StartedAtUtc = d.StartedAtUtc; e.CompletedAtUtc = d.CompletedAtUtc;
        e.SenderFriendName = d.SenderFriendName;
    }

    private static void Map(ActivityResultDto d, ActivityResultEntity e)
    {
        e.UserId = d.UserId; e.UpdatedAtUtc = d.UpdatedAtUtc; e.IsDeleted = d.IsDeleted;
        e.SessionRecordId = d.SessionRecordId; e.ExerciseId = d.ExerciseId; e.Title = d.Title;
        e.SetIteration = d.SetIteration; e.TargetReps = d.TargetReps; e.ActualReps = d.ActualReps;
        e.TargetWeight = d.TargetWeight; e.ActualWeight = d.ActualWeight; e.Completed = d.Completed;
        e.Volume = d.Volume; e.UserBodyWeightKg = d.UserBodyWeightKg;
        e.BodyweightFraction = d.BodyweightFraction; e.Notes = d.Notes;
    }

    private static ExerciseDto ToDto(ExerciseEntity e) => new()
    {
        Id = e.Id, UserId = e.UserId, UpdatedAtUtc = e.UpdatedAtUtc, IsDeleted = e.IsDeleted, Version = ToVersion(e.RowVersion),
        Title = e.Title, Category = e.Category, Weight = e.Weight, BodyParts = e.BodyParts,
        BodyweightFraction = e.BodyweightFraction, CreatedAtUtc = e.CreatedAtUtc,
    };

    private static SessionRecordDto ToDto(SessionRecordEntity e) => new()
    {
        Id = e.Id, UserId = e.UserId, UpdatedAtUtc = e.UpdatedAtUtc, IsDeleted = e.IsDeleted, Version = ToVersion(e.RowVersion),
        SessionId = e.SessionId, StartedAtUtc = e.StartedAtUtc, CompletedAtUtc = e.CompletedAtUtc,
        SenderFriendName = e.SenderFriendName,
    };

    private static ActivityResultDto ToDto(ActivityResultEntity e) => new()
    {
        Id = e.Id, UserId = e.UserId, UpdatedAtUtc = e.UpdatedAtUtc, IsDeleted = e.IsDeleted, Version = ToVersion(e.RowVersion),
        SessionRecordId = e.SessionRecordId, ExerciseId = e.ExerciseId, Title = e.Title,
        SetIteration = e.SetIteration, TargetReps = e.TargetReps, ActualReps = e.ActualReps,
        TargetWeight = e.TargetWeight, ActualWeight = e.ActualWeight, Completed = e.Completed,
        Volume = e.Volume, UserBodyWeightKg = e.UserBodyWeightKg,
        BodyweightFraction = e.BodyweightFraction, Notes = e.Notes,
    };
}
