using System.Net;

namespace DailyGroove.Sync.Api.Tests;

/// <summary>
/// Own class (so its own factory and client): once throttling exists, this burst of requests would
/// starve any neighbouring test that shared the host and cause flaky failures.
/// </summary>
public sealed class AbuseLimitTests : IClassFixture<SyncApiFactory>
{
    private readonly HttpClient _client;

    public AbuseLimitTests(SyncApiFactory factory) => _client = factory.CreateClient();

    // API4:2023 Unrestricted Resource Consumption (no rate limiting) — CWE-770
    [Fact]
    [Trait("Stage", "AbuseLimits")]
    public async Task Excessive_request_rate_is_throttled()
    {
        var user = Guid.NewGuid();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 200; i++)
            statuses.Add((await _client.GetAsync($"/sync/pull?userId={user}&since=0", TestContext.Current.CancellationToken)).StatusCode);

        Check.That(statuses).Contains(HttpStatusCode.TooManyRequests);
    }
}
