using DailyGroove.Sync.Contracts;
using System.Net;
using System.Net.Http.Json;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>
/// Each test is an exploit against the DELIBERATELY INSECURE baseline and ASSERTS THE ATTACK
/// SUCCEEDS. In the secured repo the same tests are inverted into permanent regression tests
/// that prove the attack now fails.
///
/// OWASP API Security Top 10 (2023) mapping is noted on each test.
/// </summary>
public sealed class AttackTests : IClassFixture<SyncApiFactory>
{
    private readonly HttpClient _client;

    public AttackTests(SyncApiFactory factory) => _client = factory.CreateClient();

    // API1:2023 Broken Object Level Authorization / API2 Broken Authentication
    [Fact]
    public async Task Attack_read_another_users_records_by_changing_the_user_id()
    {
        var victim = Guid.NewGuid();
        await _client.PushOkAsync(TestData.PushOf(victim, TestData.Exercise(victim, "Victim's private lift")));

        // The attacker has no credentials at all — just the victim's (guessable/leaked) id.
        var stolen = await _client.PullOkAsync(victim);

        Check.That(stolen.Exercises.Select(e => e.Title)).ContainsExactly("Victim's private lift");
    }

    // API1:2023 Broken Object Level Authorization
    [Fact]
    public async Task Attack_overwrite_another_users_record()
    {
        var victim = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var exercise = TestData.Exercise(victim, "Original", DateTime.UtcNow.AddMinutes(-1));
        await _client.PushOkAsync(TestData.PushOf(victim, exercise));

        // Same record Id, attacker's identity, newer timestamp.
        var hijack = TestData.Exercise(attacker, "Pwned", DateTime.UtcNow);
        hijack.Id = exercise.Id;
        await _client.PushOkAsync(TestData.PushOf(attacker, hijack));

        var attackerView = await _client.PullOkAsync(attacker);
        Check.That(attackerView.Exercises.Select(e => e.Title)).ContainsExactly("Pwned");
        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises).IsEmpty(); // the victim's record was taken over
    }

    // API1:2023 Broken Object Level Authorization
    [Fact]
    public async Task Attack_delete_another_users_record()
    {
        var victim = Guid.NewGuid();
        var exercise = TestData.Exercise(victim, "Precious", DateTime.UtcNow.AddMinutes(-1));
        await _client.PushOkAsync(TestData.PushOf(victim, exercise));

        var tombstone = TestData.Exercise(victim, "Precious", DateTime.UtcNow);
        tombstone.Id = exercise.Id;
        tombstone.IsDeleted = true;
        await _client.PushOkAsync(TestData.PushOf(Guid.NewGuid() /* attacker */, tombstone));

        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises.Single().IsDeleted).IsTrue();
    }

    // API3:2023 Broken Object Property Level Authorization (mass assignment)
    [Fact]
    public async Task Attack_set_server_owned_fields_to_lock_a_record_with_a_far_future_timestamp()
    {
        var attacker = Guid.NewGuid();
        var exercise = TestData.Exercise(attacker, "Locked", updatedAtUtc: DateTime.UtcNow.AddYears(50));
        await _client.PushOkAsync(TestData.PushOf(attacker, exercise));

        // A legitimate later edit loses last-write-wins forever, because the client chose the clock.
        exercise.Title = "Legit edit";
        exercise.UpdatedAtUtc = DateTime.UtcNow;
        var push = await _client.PushOkAsync(TestData.PushOf(attacker, exercise));

        Check.That(push.Ignored).IsEqualTo(1);
        var view = await _client.PullOkAsync(attacker);
        Check.That(view.Exercises.Single().Title).IsEqualTo("Locked");
    }

    // API3:2023 Broken Object Property Level Authorization (mass assignment of the owner)
    [Fact]
    public async Task Attack_plant_a_record_in_another_users_account_by_setting_the_owner_field()
    {
        var victim = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var planted = TestData.Exercise(victim, "Planted by attacker");

        await _client.PushOkAsync(TestData.PushOf(attacker, planted)); // record's UserId = victim

        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises.Select(e => e.Title)).ContainsExactly("Planted by attacker");
    }

    // API4:2023 Unrestricted Resource Consumption (no validation / size limits)
    [Fact]
    public async Task Attack_oversized_and_malformed_payload_is_accepted()
    {
        var user = Guid.NewGuid();
        var garbage = TestData.Exercise(user, new string('A', 2_000_000));
        garbage.Weight = -1e12;
        garbage.Category = 9999;
        garbage.BodyweightFraction = double.MaxValue;
        garbage.CreatedAtUtc = DateTime.MinValue;

        var response = await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(user, garbage), TestContext.Current.CancellationToken);

        Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var stored = await _client.PullOkAsync(user);
        Check.That(stored.Exercises.Single().Title.Length).IsEqualTo(2_000_000);
    }

    // API4:2023 Unrestricted Resource Consumption (no rate limiting)
    [Fact]
    public async Task Attack_unlimited_request_rate_is_accepted()
    {
        var user = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 200; i++)
            statuses.Add((await _client.GetAsync($"/sync/pull?userId={user}&since=0", TestContext.Current.CancellationToken)).StatusCode);

        Check.That(statuses).Not.Contains(HttpStatusCode.TooManyRequests);
        Check.That(statuses.All(s => s == HttpStatusCode.OK)).IsTrue();
    }

    // API8:2023 Security Misconfiguration (CORS)
    [Fact]
    public async Task Attack_cross_origin_request_from_an_arbitrary_origin_succeeds()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/sync/pull?userId={Guid.NewGuid()}&since=0");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Check.That(response.Headers.GetValues("Access-Control-Allow-Origin")).ContainsExactly("*");
    }

    // API8:2023 Security Misconfiguration (CORS preflight)
    [Fact]
    public async Task Attack_cors_preflight_from_an_arbitrary_origin_is_approved()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/sync/push");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Check.That(response.Headers.GetValues("Access-Control-Allow-Origin")).ContainsExactly("*");
    }

    // API8:2023 Security Misconfiguration (verbose errors)
    [Fact]
    public async Task Attack_error_response_leaks_internal_details()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user);
        // Two records with the same key in one batch makes the server throw.
        var response = await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(user, exercise, exercise), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        Check.That(body).Contains("System.InvalidOperationException");
        Check.That(body).Contains("   at "); // a stack trace
        Check.That(body).Contains("DailyGroove.Sync.Api"); // internal namespaces
    }

    // API8:2023 Security Misconfiguration (secrets in source)
    [Fact]
    public void Attack_database_password_is_committed_in_appsettings_json()
    {
        var appsettings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Check.That(appsettings).Contains("Password=");
        Check.That(appsettings).Contains("User Id=sa");
    }
}
