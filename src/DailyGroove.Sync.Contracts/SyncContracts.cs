using System.Text.Json.Serialization;

namespace DailyGroove.Sync.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// Shared wire contracts for the Daily Groove sync API.
//
// Every synced record has a client-generated GUID Id, a UserId (the owner), an
// UpdatedAtUtc timestamp (used for last-write-wins) and an IsDeleted tombstone flag.
//
// DELIBERATELY INSECURE (baseline repo): UserId and UpdatedAtUtc are supplied by the
// client and trusted by the server. See README.md.
// ─────────────────────────────────────────────────────────────────────────────

public class ExerciseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Category { get; set; }
    public double? Weight { get; set; }
    public string BodyParts { get; set; } = string.Empty;
    public double BodyweightFraction { get; set; } = 0.5;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>Server-assigned change cursor. Ignored on push, populated on pull.</summary>
    public long Version { get; set; }
}

public class SessionRecordDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? SenderFriendName { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public long Version { get; set; }
}

public class ActivityResultDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionRecordId { get; set; }
    public Guid? ExerciseId { get; set; }
    public string? Title { get; set; }
    public int SetIteration { get; set; }
    public int TargetReps { get; set; }
    public int ActualReps { get; set; }
    public double? TargetWeight { get; set; }
    public double? ActualWeight { get; set; }
    public bool Completed { get; set; }
    public int? Volume { get; set; }
    public double? UserBodyWeightKg { get; set; }
    public double? BodyweightFraction { get; set; }
    public string? Notes { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public long Version { get; set; }
}

/// <summary>POST /sync/push body: a batch of created/updated/deleted records.</summary>
public class PushRequest
{
    /// <summary>Who is pushing. (Insecure: taken on trust from the body.)</summary>
    public Guid UserId { get; set; }

    public List<ExerciseDto> Exercises { get; set; } = [];
    public List<SessionRecordDto> SessionRecords { get; set; } = [];
    public List<ActivityResultDto> ActivityResults { get; set; } = [];
}

public class PushResponse
{
    /// <summary>Number of records applied (a stale last-write-wins loser is not counted).</summary>
    public int Applied { get; set; }

    /// <summary>Number of records ignored because the server copy was newer.</summary>
    public int Ignored { get; set; }

    // Deliberately no cursor here: the client keeps its own pull cursor, otherwise adopting a
    // push-time cursor could skip changes another device made since the client's last pull.
}

/// <summary>GET /sync/pull response: everything changed after the supplied cursor.</summary>
public class PullResponse
{
    /// <summary>Resume point: pass as <c>since</c> on the next pull.</summary>
    public long Cursor { get; set; }

    /// <summary>True when more changes remain; pull again from <see cref="Cursor"/>.</summary>
    public bool HasMore { get; set; }
    public List<ExerciseDto> Exercises { get; set; } = [];
    public List<SessionRecordDto> SessionRecords { get; set; } = [];
    public List<ActivityResultDto> ActivityResults { get; set; } = [];
}

/// <summary>
/// Source-generated JSON metadata so the contracts are trim-safe in Blazor WASM / MAUI.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(PushResponse))]
[JsonSerializable(typeof(PullResponse))]
public partial class SyncJsonContext : JsonSerializerContext;
