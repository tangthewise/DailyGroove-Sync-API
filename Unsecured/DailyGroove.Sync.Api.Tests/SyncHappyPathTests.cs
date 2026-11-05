using DailyGroove.Sync.Contracts;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>The sync protocol working as intended, before any attack is attempted.</summary>
public sealed class SyncHappyPathTests : IClassFixture<SyncApiFactory>
{
    private readonly HttpClient _client;

    public SyncHappyPathTests(SyncApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Pushed_exercise_can_be_pulled_back()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user, "Squat");

        var push = await _client.PushOkAsync(TestData.PushOf(user, exercise));
        var pull = await _client.PullOkAsync(user);

        Check.That(push.Applied).IsEqualTo(1);
        Check.That(pull.Exercises).HasSize(1);
        Check.That(pull.Exercises[0].Id).IsEqualTo(exercise.Id);
        Check.That(pull.Exercises[0].Title).IsEqualTo("Squat");
        Check.That(pull.Exercises[0].Version).IsStrictlyGreaterThan(0);
    }

    [Fact]
    public async Task Full_workout_history_round_trips_with_its_results()
    {
        var user = Guid.NewGuid();
        var record = new SessionRecordDto { Id = Guid.NewGuid(), UserId = user, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var result = new ActivityResultDto { Id = Guid.NewGuid(), UserId = user, SessionRecordId = record.Id, Title = "Deadlift", ActualReps = 5, ActualWeight = 100, Completed = true, UpdatedAtUtc = DateTime.UtcNow };

        await _client.PushOkAsync(new PushRequest { UserId = user, SessionRecords = [record], ActivityResults = [result] });
        var pull = await _client.PullOkAsync(user);

        Check.That(pull.SessionRecords.Select(r => r.Id)).ContainsExactly(record.Id);
        Check.That(pull.ActivityResults.Select(r => r.Id)).ContainsExactly(result.Id);
        Check.That(pull.ActivityResults[0].ActualWeight).IsEqualTo(100.0);
    }

    [Fact]
    public async Task Pull_since_cursor_returns_only_newer_changes()
    {
        var user = Guid.NewGuid();
        await _client.PushOkAsync(TestData.PushOf(user, TestData.Exercise(user, "Old")));
        var first = await _client.PullOkAsync(user);

        var newer = TestData.Exercise(user, "New");
        await _client.PushOkAsync(TestData.PushOf(user, newer));
        var second = await _client.PullOkAsync(user, since: first.Cursor);

        Check.That(second.Exercises.Select(e => e.Title)).ContainsExactly("New");
        Check.That(second.Cursor).IsStrictlyGreaterThan(first.Cursor);
    }

    [Fact]
    public async Task Soft_deleted_exercise_is_pulled_as_a_tombstone()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user, "Doomed", DateTime.UtcNow.AddMinutes(-5));
        await _client.PushOkAsync(TestData.PushOf(user, exercise));
        var before = await _client.PullOkAsync(user);

        exercise.IsDeleted = true;
        exercise.UpdatedAtUtc = DateTime.UtcNow;
        await _client.PushOkAsync(TestData.PushOf(user, exercise));
        var after = await _client.PullOkAsync(user, since: before.Cursor);

        Check.That(after.Exercises).HasSize(1);
        Check.That(after.Exercises[0].IsDeleted).IsTrue();
    }

    [Fact]
    public async Task Last_write_wins_newer_update_is_applied()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user, "v1", DateTime.UtcNow.AddMinutes(-10));
        await _client.PushOkAsync(TestData.PushOf(user, exercise));

        exercise.Title = "v2";
        exercise.UpdatedAtUtc = DateTime.UtcNow;
        var push = await _client.PushOkAsync(TestData.PushOf(user, exercise));
        var pull = await _client.PullOkAsync(user);

        Check.That(push.Applied).IsEqualTo(1);
        Check.That(pull.Exercises.Single().Title).IsEqualTo("v2");
    }

    [Fact]
    public async Task Last_write_wins_older_update_is_ignored()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user, "current", DateTime.UtcNow);
        await _client.PushOkAsync(TestData.PushOf(user, exercise));

        exercise.Title = "stale";
        exercise.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-30);
        var push = await _client.PushOkAsync(TestData.PushOf(user, exercise));
        var pull = await _client.PullOkAsync(user);

        Check.That(push.Ignored).IsEqualTo(1);
        Check.That(pull.Exercises.Single().Title).IsEqualTo("current");
    }

    [Fact]
    public async Task Pull_only_returns_the_requested_users_data()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await _client.PushOkAsync(TestData.PushOf(alice, TestData.Exercise(alice, "Alice's")));
        await _client.PushOkAsync(TestData.PushOf(bob, TestData.Exercise(bob, "Bob's")));

        var pull = await _client.PullOkAsync(alice);

        Check.That(pull.Exercises.Select(e => e.Title)).ContainsExactly("Alice's");
    }
}
