using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Infrastructure.Migrations;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class TypeSafeRateLimitTests
{
    [Fact]
    public async Task Text_state_budget_counts_full_utf8_input_without_transport_escape_expansion()
    {
        var state = string.Concat(Enumerable.Repeat("{\"name\":\"Māori\\n\"}", 2500));
        var body = JsonSerializer.Serialize(new { state });
        Assert.True(Encoding.UTF8.GetByteCount(body) > 65_536);
        var budget = new RecordingBudget();
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            Assert.Equal(body, await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK);
        });
        using var client = Client(budget, api);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request);
        var expected = Encoding.UTF8.GetByteCount(state) + Encoding.UTF8.GetByteCount("{\"state\":\"\"}") + 1024;
        Assert.Equal(expected, Assert.Single(budget.Costs));
        Assert.True(expected < 65_536);
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(529, true)]
    public async Task Retry_honors_header_reacquires_budget_and_preserves_request(int status, bool dateHeader)
    {
        var budget = new RecordingBudget();
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            Assert.Equal("Bearer test-secret", request.Headers.Authorization!.ToString());
            Assert.Equal("{\"state\":\"data\"}", await request.Content!.ReadAsStringAsync(ct));
            if (budget.Costs.Count > 1) return new(HttpStatusCode.OK);
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = dateHeader
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        using var client = Client(budget, api);
        using var request = Request();
        using var result = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, budget.Costs.Count);
        Assert.All(budget.Costs, cost => Assert.True(cost >= 1024 + 16));
        Assert.InRange(Assert.Single(budget.Delays).TotalSeconds, 28, 31);
    }

    [Fact]
    public async Task Retry_count_is_bounded_and_permanent_errors_are_not_retried()
    {
        foreach (var status in new[] { 401, 422, 429, 529 })
        {
            var budget = new RecordingBudget();
            using var api = new ModelReviewTestApi((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
            using var client = Client(budget, api);
            using var request = Request();
            using var response = await client.SendAsync(request);
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(status is 429 or 529 ? 4 : 1, api.Requests);
            Assert.Equal(status is 429 or 529 ? 4 : 0, budget.Delays.Count);
            for (var attempt = 0; attempt < budget.Delays.Count; attempt++)
                Assert.InRange(budget.Delays[attempt].TotalSeconds, Math.Pow(2, attempt), Math.Pow(2, attempt) + 1);
        }
    }

    [Fact]
    public async Task Cancellation_while_waiting_does_not_send()
    {
        var budget = new RecordingBudget { Wait = true };
        using var api = new ModelReviewTestApi((_, _) => throw new Exception("Must not send"));
        using var client = Client(budget, api);
        using var request = Request();
        using var cancellation = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(request, cancellation.Token));
        Assert.Equal(0, api.Requests);
    }

    [ChainInputResumeTests.PostgresFact]
    public async Task Replicas_share_request_token_limits_and_cooldown_with_cancellation()
    {
        var schema = "typesafe_test_" + Guid.NewGuid().ToString("N");
        var cs = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES")) { SearchPath = schema }.ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await setup.ExecuteNonQueryAsync();
        try
        {
            foreach (var operation in new AddTypeSafeRateBudget().UpOperations.Cast<SqlOperation>())
            {
                await using var setup = new NpgsqlCommand(operation.Sql, connection);
                await setup.ExecuteNonQueryAsync();
            }
            await using (var audit = new NpgsqlCommand("""
                CREATE TABLE admission_log (scope text, admitted_at timestamptz);
                CREATE FUNCTION record_admission() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO admission_log VALUES (NEW.scope, NEW.admitted_at);
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER record_admission AFTER INSERT ON typesafe_rate_admissions
                    FOR EACH ROW EXECUTE FUNCTION record_admission();
                """, connection))
            {
                await audit.ExecuteNonQueryAsync();
            }
            await using var first = Services(cs);
            await using var second = Services(cs);
            var limits = new ModelReviewOptions { RequestsPerSecond = 2, TokensPerSecond = 100 };
            TypeSafeRateBudget Budget(ServiceProvider provider) => new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(limits));
            var a = Budget(first);
            var b = Budget(second);
            foreach (var tokenLimited in new[] { false, true })
            {
                limits.RateLimitScope = tokenLimited ? "token-test" : "request-test";
                limits.RequestsPerSecond = tokenLimited ? 100 : 2;
                limits.TokensPerSecond = tokenLimited ? 2 : 100;
                await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? a : b).AcquireAsync(1, default)));
                // Measure database admission time, not thread scheduling after each await returns.
                await using var history = new NpgsqlCommand("SELECT admitted_at FROM admission_log WHERE scope = @scope ORDER BY admitted_at", connection);
                history.Parameters.AddWithValue("scope", limits.RateLimitScope);
                await using var reader = await history.ExecuteReaderAsync();
                var admissions = new List<DateTime>();
                while (await reader.ReadAsync()) admissions.Add(reader.GetDateTime(0));
                Assert.Equal(6, admissions.Count);
                for (var i = 2; i < admissions.Count; i++) Assert.True((admissions[i] - admissions[i - 2]).TotalSeconds >= .95);
            }
            limits.RateLimitScope = "cooldown-test";
            await a.CooldownAsync(TimeSpan.FromSeconds(2), default);
            await b.CooldownAsync(TimeSpan.FromMilliseconds(10), default);
            using var cancelled = new CancellationTokenSource(150);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.AcquireAsync(1, cancelled.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.AcquireAsync(3, default));
            limits.RequestsPerSecond = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => b.AcquireAsync(1, default));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static ServiceProvider Services(string cs) => new ServiceCollection()
        .AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options, new FakeCurrentTenant(Guid.NewGuid())))
        .BuildServiceProvider();
    private static HttpClient Client(ITypeSafeRateBudget budget, HttpMessageHandler api) => new(new TypeSafeRateLimitHandler(budget, NullLogger<TypeSafeRateLimitHandler>.Instance) { InnerHandler = api });
    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone") { Content = new StringContent("{\"state\":\"data\"}") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-secret");
        return request;
    }
    private sealed class RecordingBudget : ITypeSafeRateBudget
    {
        public List<int> Costs { get; } = [];
        public List<TimeSpan> Delays { get; } = [];
        public bool Wait { get; init; }
        public async Task AcquireAsync(int tokens, CancellationToken ct)
        {
            Costs.Add(tokens);
            if (Wait) await Task.Delay(Timeout.Infinite, ct);
        }
        public Task CooldownAsync(TimeSpan delay, CancellationToken ct) { Delays.Add(delay); return Task.CompletedTask; }
    }
}
