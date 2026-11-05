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
        var exercises = await UpsertAllAsync(db.Exercises, request.Exercises, d => d.Id, d => d.UpdatedAtUtc,
            d => new ExerciseEntity { Id = d.Id }, Map);
        var records = await UpsertAllAsync(db.SessionRecords, request.SessionRecords, d => d.Id, d => d.UpdatedAtUtc,
            d => new SessionRecordEntity { Id = d.Id }, Map);
        var results = await UpsertAllAsync(db.ActivityResults, request.ActivityResults, d => d.Id, d => d.UpdatedAtUtc,
            d => new ActivityResultEntity { Id = d.Id }, Map);

        await db.SaveChangesAsync();

        // No cursor in the push response on purpose. Adopting "my highest version" after a push would
        // skip changes another device made since this client's last pull. The client keeps its own
        // cursor and pulls; its own rows come back harmlessly.
        return Results.Ok(new PushResponse
        {
            Applied = exercises.Applied + records.Applied + results.Applied,
            Ignored = exercises.Ignored + records.Ignored + results.Ignored,
        });
    }

    /// <summary>
    /// Last-write-wins upsert of a batch: apply only if the incoming timestamp is not older.
    /// Existing rows are loaded with a single query per table, not one query per record.
    /// </summary>
    private static async Task<(int Applied, int Ignored)> UpsertAllAsync<TEntity, TDto>(
        DbSet<TEntity> set, List<TDto> dtos,
        Func<TDto, Guid> id, Func<TDto, DateTime> updatedAtUtc,
        Func<TDto, TEntity> create, Action<TDto, TEntity> map)
        where TEntity : SyncEntity
    {
        if (dtos.Count == 0) return (0, 0);

        var ids = dtos.Select(id).Distinct().ToList();
        var existing = (await set.Where(e => ids.Contains(e.Id)).ToListAsync()).ToDictionary(e => e.Id);

        int applied = 0, ignored = 0;
        foreach (var dto in dtos)
        {
            if (existing.TryGetValue(id(dto), out var current))
            {
                if (updatedAtUtc(dto) < current.UpdatedAtUtc) { ignored++; continue; }
                map(dto, current);
            }
            else
            {
                var created = create(dto);
                map(dto, created);
                set.Add(created);
            }
            applied++;
        }

        return (applied, ignored);
    }

    // ── Pull ─────────────────────────────────────────────────────────────────

    private const int DefaultPageSize = 500;
    private const int MaxPageSize = 500;

    private static async Task<IResult> PullAsync(Guid userId, long since, int? limit, SyncDbContext db)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var sinceBytes = ToVersionBytes(since);

        // rowversion is assigned when a row is WRITTEN, not when its transaction COMMITS. A slower
        // transaction can commit after a later one, so rows at or above MIN_ACTIVE_ROWVERSION() may
        // still appear below a version the client has already seen. Only return rows strictly below
        // it, so the cursor can never move past a write that hasn't committed yet.
        var minActive = await db.Database.SqlQuery<byte[]>($"SELECT MIN_ACTIVE_ROWVERSION() AS [Value]").SingleAsync();

        // Take one extra row per table so we can tell whether more remain after this page.
        var exercises = await db.Exercises
            .Where(e => e.UserId == userId
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, minActive) < 0)
            .OrderBy(e => e.RowVersion).Take(pageSize + 1).ToListAsync();
        var records = await db.SessionRecords
            .Where(e => e.UserId == userId
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, minActive) < 0)
            .OrderBy(e => e.RowVersion).Take(pageSize + 1).ToListAsync();
        var results = await db.ActivityResults
            .Where(e => e.UserId == userId
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, sinceBytes) > 0
                && StructuralComparisons.StructuralComparer.Compare(e.RowVersion, minActive) < 0)
            .OrderBy(e => e.RowVersion).Take(pageSize + 1).ToListAsync();

        // One version-ordered page across the three tables.
        var candidates = new List<(long Version, int Table, int Index)>();
        for (var i = 0; i < exercises.Count; i++) candidates.Add((ToVersion(exercises[i].RowVersion), 0, i));
        for (var i = 0; i < records.Count; i++) candidates.Add((ToVersion(records[i].RowVersion), 1, i));
        for (var i = 0; i < results.Count; i++) candidates.Add((ToVersion(results[i].RowVersion), 2, i));

        var page = candidates.OrderBy(c => c.Version).Take(pageSize).ToList();
        var hasMore = candidates.Count > pageSize;

        var response = new PullResponse { HasMore = hasMore };
        foreach (var (_, table, index) in page)
        {
            switch (table)
            {
                case 0: response.Exercises.Add(ToDto(exercises[index])); break;
                case 1: response.SessionRecords.Add(ToDto(records[index])); break;
                default: response.ActivityResults.Add(ToDto(results[index])); break;
            }
        }

        // More to come: resume exactly after the last row returned. Otherwise everything below the
        // oldest in-flight write has been delivered, so the safe resume point is just under it.
        response.Cursor = hasMore
            ? page[^1].Version
            : Math.Max(since, ToVersion(minActive) - 1);

        return Results.Ok(response);
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
