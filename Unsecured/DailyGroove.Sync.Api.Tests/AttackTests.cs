using DailyGroove.Sync.Contracts;
using System.Net;
using System.Net.Http.Json;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>
/// Each test runs an exploit against the API and asserts the PROTECTED behaviour. Against the
/// DELIBERATELY INSECURE baseline they are RED — each failure is a documented, reproducible
/// weakness. A test turns GREEN only once the matching protection is in place, and then stays
/// as a permanent regression test.
///
/// Every test carries a "Stage" trait (see API/SECURITY-JOURNEY.md) so one stage can be run alone:
///   dotnet run --project DailyGroove.Sync.Api.Tests -- -trait "Stage=2-Authentication"
///
/// OWASP API Security Top 10 (2023) mapping is noted on each test.
///
/// Note: until authentication exists the "attacker" is simply an unauthenticated caller, and
/// some assertions deliberately demand the specific protective response (e.g. 400 for bad input,
/// not just any refusal) so a later stage can't turn them green by accident. When Stage 2
/// introduces credentials, these tests gain authenticated variants (an attacker who is signed in
/// as a *different* user).
/// </summary>
public sealed class AttackTests : IClassFixture<SyncApiFactory>
{
    private readonly HttpClient _client;

    public AttackTests(SyncApiFactory factory) => _client = factory.CreateClient();

    // API1:2023 Broken Object Level Authorization / API2 Broken Authentication
    [Fact]
    [Trait("Stage", "2-Authentication")]
    public async Task Caller_without_credentials_is_refused_when_reading_another_users_records()
    {
        var victim = Guid.NewGuid();
        await _client.PushOkAsync(TestData.PushOf(victim, TestData.Exercise(victim, "Victim's private lift")));

        // The attacker has no credentials at all — just the victim's (guessable/leaked) id.
        var response = await _client.GetAsync($"/sync/pull?userId={victim}&since=0", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Check.That(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }).Contains(response.StatusCode);
        Check.That(body).Not.Contains("Victim's private lift");
    }

    // API1:2023 Broken Object Level Authorization
    [Fact]
    [Trait("Stage", "3-ObjectAuthorization")]
    public async Task Another_users_record_cannot_be_overwritten()
    {
        var victim = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var exercise = TestData.Exercise(victim, "Original", DateTime.UtcNow.AddMinutes(-1));
        await _client.PushOkAsync(TestData.PushOf(victim, exercise));

        // Same record Id, attacker's identity, newer timestamp. The response itself is irrelevant:
        // what matters is that the victim's data is untouched.
        var hijack = TestData.Exercise(attacker, "Pwned", DateTime.UtcNow);
        hijack.Id = exercise.Id;
        await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(attacker, hijack), TestContext.Current.CancellationToken);

        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises.Select(e => e.Title)).ContainsExactly("Original");
    }

    // API1:2023 Broken Object Level Authorization
    [Fact]
    [Trait("Stage", "3-ObjectAuthorization")]
    public async Task Another_users_record_cannot_be_deleted()
    {
        var victim = Guid.NewGuid();
        var exercise = TestData.Exercise(victim, "Precious", DateTime.UtcNow.AddMinutes(-1));
        await _client.PushOkAsync(TestData.PushOf(victim, exercise));

        var tombstone = TestData.Exercise(victim, "Precious", DateTime.UtcNow);
        tombstone.Id = exercise.Id;
        tombstone.IsDeleted = true;
        await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(Guid.NewGuid() /* attacker */, tombstone), TestContext.Current.CancellationToken);

        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises.Single().IsDeleted).IsFalse();
    }

    // API3:2023 Broken Object Property Level Authorization (mass assignment)
    [Fact]
    [Trait("Stage", "4-MassAssignmentAndValidation")]
    public async Task Client_supplied_timestamp_cannot_lock_a_record_against_later_edits()
    {
        var attacker = Guid.NewGuid();
        var exercise = TestData.Exercise(attacker, "Locked", updatedAtUtc: DateTime.UtcNow.AddYears(50));
        // Accepted-and-clamped or rejected are both fine; what matters is the outcome below.
        await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(attacker, exercise), TestContext.Current.CancellationToken);

        // A legitimate later edit must still win — the client must not be able to choose the clock.
        exercise.Title = "Legit edit";
        exercise.UpdatedAtUtc = DateTime.UtcNow;
        await _client.PushOkAsync(TestData.PushOf(attacker, exercise));

        var view = await _client.PullOkAsync(attacker);
        Check.That(view.Exercises.Single().Title).IsEqualTo("Legit edit");
    }

    // API3:2023 Broken Object Property Level Authorization (mass assignment of the owner)
    [Fact]
    [Trait("Stage", "4-MassAssignmentAndValidation")]
    public async Task Client_cannot_plant_a_record_in_another_users_account_via_the_owner_field()
    {
        var victim = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var planted = TestData.Exercise(victim, "Planted by attacker");

        // The record claims UserId = victim while the push is made as the attacker.
        await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(attacker, planted), TestContext.Current.CancellationToken);

        var victimView = await _client.PullOkAsync(victim);
        Check.That(victimView.Exercises).IsEmpty();
    }

    // API4:2023 Unrestricted Resource Consumption (no validation / size limits)
    [Fact]
    [Trait("Stage", "4-MassAssignmentAndValidation")]
    public async Task Oversized_and_malformed_payload_is_rejected()
    {
        var user = Guid.NewGuid();
        var garbage = TestData.Exercise(user, new string('A', 2_000_000));
        garbage.Weight = -1e12;
        garbage.Category = 9999;
        garbage.BodyweightFraction = double.MaxValue;
        garbage.CreatedAtUtc = DateTime.MinValue;

        var response = await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(user, garbage), TestContext.Current.CancellationToken);

        // Specifically a validation/size refusal — not 401, 500 or 200.
        Check.That(new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.UnprocessableEntity })
            .Contains(response.StatusCode);
    }

    // API4:2023 Unrestricted Resource Consumption (no rate limiting)
    [Fact]
    [Trait("Stage", "5-AbuseLimits")]
    public async Task Excessive_request_rate_is_throttled()
    {
        var user = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 200; i++)
            statuses.Add((await _client.GetAsync($"/sync/pull?userId={user}&since=0", TestContext.Current.CancellationToken)).StatusCode);

        Check.That(statuses).Contains(HttpStatusCode.TooManyRequests);
    }

    // API8:2023 Security Misconfiguration (CORS)
    [Fact]
    [Trait("Stage", "7-Configuration")]
    public async Task Cross_origin_request_from_an_arbitrary_origin_gets_no_cors_approval()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/sync/pull?userId={Guid.NewGuid()}&since=0");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        // No Access-Control-Allow-Origin header means the browser blocks the cross-origin read.
        Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    // API8:2023 Security Misconfiguration (CORS preflight)
    [Fact]
    [Trait("Stage", "7-Configuration")]
    public async Task Cors_preflight_from_an_arbitrary_origin_is_not_approved()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/sync/push");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    // API8:2023 Security Misconfiguration (verbose errors)
    [Fact]
    [Trait("Stage", "7-Configuration")]
    public async Task Error_response_does_not_leak_internal_details()
    {
        var user = Guid.NewGuid();
        var exercise = TestData.Exercise(user);
        // Two records with the same key in one batch makes the server throw.
        var response = await _client.PostAsJsonAsync("/sync/push", TestData.PushOf(user, exercise, exercise), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The request must genuinely fail (400 or 500), and say nothing about the internals.
        Check.That(new[] { HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError }).Contains(response.StatusCode);
        Check.That(body).Not.Contains("System.InvalidOperationException");
        Check.That(body).Not.Contains("   at "); // a stack trace
        Check.That(body).Not.Contains("DailyGroove.Sync.Api"); // internal namespaces
    }

    // API8:2023 Security Misconfiguration (secrets in source)
    [Fact]
    [Trait("Stage", "6-Secrets")]
    public void Database_password_is_not_committed_in_appsettings_json()
    {
        var appsettings = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Check.That(appsettings).Not.Contains("Password=");
        Check.That(appsettings).Not.Contains("User Id=sa");
    }
}
