using DailyGroove.Sync.Contracts;
using Microsoft.Data.SqlClient;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>
/// Protocol-correctness bugs that have nothing to do with the deliberate security flaws. They are
/// fixed in the baseline so they don't survive into the secured version.
/// </summary>
public sealed class SyncCorrectnessTests : IClassFixture<SyncApiFactory>
{
    private readonly SyncApiFactory _factory;
    private readonly HttpClient _client;

    public SyncCorrectnessTests(SyncApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(); // starts the host, which creates the database
    }

    [Fact]
    public async Task Pulled_timestamps_are_utc()
    {
        var user = Guid.NewGuid();
        await _client.PushOkAsync(TestData.PushOf(user, TestData.Exercise(user)));

        var pull = await _client.PullOkAsync(user);

        // EF materialises datetime2 as Kind=Unspecified, which serialises without a 'Z' and lets a
        // client read the time as local. Utc kind means a trailing 'Z' on the wire.
        Check.That(pull.Exercises.Single().UpdatedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
        Check.That(pull.Exercises.Single().CreatedAtUtc.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Pull_pages_through_a_large_history_without_gaps_or_duplicates()
    {
        var user = Guid.NewGuid();
        var pushed = Enumerable.Range(0, 5).Select(i => TestData.Exercise(user, $"E{i}")).ToArray();
        await _client.PushOkAsync(TestData.PushOf(user, pushed));

        var seen = new List<Guid>();
        var versions = new List<long>();
        long cursor = 0;
        var pages = 0;
        PullResponse page;
        do
        {
            page = await _client.PullOkAsync(user, since: cursor, limit: 2);
            seen.AddRange(page.Exercises.Select(e => e.Id));
            versions.AddRange(page.Exercises.Select(e => e.Version));
            Check.That(page.Exercises.Count).IsLessOrEqualThan(2);
            cursor = page.Cursor;
            pages++;
        } while (page.HasMore && pages < 20);

        Check.That(pages).IsGreaterOrEqualThan(3); // 5 records at 2 per page
        Check.That(seen).HasSize(5);
        Check.That(seen.Distinct()).HasSize(5);
        Check.That(seen).IsEquivalentTo(pushed.Select(e => e.Id).ToArray());
        // Oldest version first, across page boundaries (EF doesn't guarantee insert order within a
        // batch, so compare versions rather than the order the records were pushed in).
        Check.That(versions).IsInAscendingOrder();
    }

    [Fact]
    public async Task Pull_never_skips_a_row_that_commits_after_a_later_version()
    {
        // Mirror Azure SQL's default (readers don't block on uncommitted rows).
        await ExecuteOnMasterAsync($"ALTER DATABASE [{DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");

        var user = Guid.NewGuid();
        var slow = TestData.Exercise(user, "slow transaction");
        var fast = TestData.Exercise(user, "fast push");

        // Device A's write takes an earlier rowversion but hasn't committed yet...
        await using var slowConnection = new SqlConnection(_factory.ConnectionString);
        await slowConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = (SqlTransaction)await slowConnection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await InsertExerciseAsync(slowConnection, transaction, slow);

        // ...Device B's push takes a LATER rowversion and commits first.
        await _client.PushOkAsync(TestData.PushOf(user, fast));
        var first = await _client.PullOkAsync(user);

        // Now the slow write commits.
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        var second = await _client.PullOkAsync(user, since: first.Cursor);

        var everything = first.Exercises.Concat(second.Exercises).Select(e => e.Title).ToList();
        Check.That(everything).Contains("slow transaction"); // would be lost forever if the cursor jumped past it
        Check.That(everything).Contains("fast push");
    }

    private string DatabaseName => new SqlConnectionStringBuilder(_factory.ConnectionString).InitialCatalog;

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var master = new SqlConnection("Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true");
        await master.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = master.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task InsertExerciseAsync(SqlConnection connection, SqlTransaction transaction, ExerciseDto e)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Exercises (Id, UserId, UpdatedAtUtc, IsDeleted, Title, Category, Weight, BodyParts, BodyweightFraction, CreatedAtUtc)
            VALUES (@id, @user, @updated, 0, @title, 0, NULL, N'', 0.5, @created)
            """;
        cmd.Parameters.AddWithValue("@id", e.Id);
        cmd.Parameters.AddWithValue("@user", e.UserId);
        cmd.Parameters.AddWithValue("@updated", e.UpdatedAtUtc);
        cmd.Parameters.AddWithValue("@title", e.Title);
        cmd.Parameters.AddWithValue("@created", e.CreatedAtUtc);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
