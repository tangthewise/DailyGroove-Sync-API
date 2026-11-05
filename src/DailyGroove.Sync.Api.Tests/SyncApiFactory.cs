using DailyGroove.Sync.Api;
using DailyGroove.Sync.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using System.Net.Http.Json;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>
/// Boots the real API in-process against a throwaway SQL Server LocalDB database
/// (real <c>rowversion</c> semantics — SQLite can't generate them). One database per factory
/// instance; dropped on dispose.
/// </summary>
public sealed class SyncApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"DailyGrooveSync_Test_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try
        {
            SqlConnection.ClearAllPools();
            using var master = new SqlConnection("Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true");
            master.Open();
            using var cmd = master.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort cleanup of a throwaway test database.
        }
    }
}

/// <summary>Small helpers so tests read as the HTTP conversations they are.</summary>
public static class SyncClientExtensions
{
    public static async Task<PushResponse> PushOkAsync(this HttpClient client, PushRequest request)
    {
        var response = await client.PostAsJsonAsync("/sync/push", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PushResponse>())!;
    }

    public static async Task<PullResponse> PullOkAsync(this HttpClient client, Guid userId, long since = 0, int? limit = null)
    {
        var limitQuery = limit is null ? "" : $"&limit={limit}";
        var response = await client.GetAsync($"/sync/pull?userId={userId}&since={since}{limitQuery}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PullResponse>())!;
    }
}

public static class TestData
{
    public static ExerciseDto Exercise(Guid userId, string title = "Bench Press", DateTime? updatedAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = title,
        Category = 0,
        Weight = 60,
        BodyParts = "Chest, Triceps",
        BodyweightFraction = 0.5,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow,
    };

    public static PushRequest PushOf(Guid userId, params ExerciseDto[] exercises) => new()
    {
        UserId = userId,
        Exercises = [.. exercises],
    };
}
