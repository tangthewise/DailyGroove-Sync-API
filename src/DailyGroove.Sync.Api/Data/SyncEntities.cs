using System.ComponentModel.DataAnnotations;

namespace DailyGroove.Sync.Api.Data;

/// <summary>
/// Common columns for every synced table. <see cref="RowVersion"/> is a SQL Server
/// <c>rowversion</c>: the database bumps it on every insert/update, so it doubles as the
/// monotonically increasing pull cursor.
/// </summary>
public abstract class SyncEntity
{
    public Guid Id { get; set; }

    /// <summary>Owner of the record.</summary>
    public Guid UserId { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Soft-delete tombstone so deletions propagate to other devices.</summary>
    public bool IsDeleted { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

public class ExerciseEntity : SyncEntity
{
    public string Title { get; set; } = string.Empty;
    public int Category { get; set; }
    public double? Weight { get; set; }
    public string BodyParts { get; set; } = string.Empty;
    public double BodyweightFraction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class SessionRecordEntity : SyncEntity
{
    public Guid? SessionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? SenderFriendName { get; set; }
}

public class ActivityResultEntity : SyncEntity
{
    // No FK to SessionRecordEntity on purpose: a client may push children before parents.
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
}
