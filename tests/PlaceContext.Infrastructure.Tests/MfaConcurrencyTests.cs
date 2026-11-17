using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using PlaceContext.Infrastructure.Auth;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class MfaConcurrencyTests
{
    [ChainInputResumeTests.PostgresFact]
    public async Task Postgres_enforces_the_attempt_budget_and_single_use_across_connections()
    {
        foreach (var code in new[] { "000000", "123456" })
        {
            await using var fixture = await Fixture.CreateAsync(postgres: true);
            var results = await fixture.VerifyTogetherAsync(code, "login");
            Assert.Equal(code == "123456" ? 1 : 0, results.Count(accepted => accepted));
            await using var check = fixture.Open();
            Assert.Null((await check.Users.SingleAsync()).TwoFactorCodeHash);
        }
    }

    [Theory]
    [InlineData("login")]
    [InlineData("setup")]
    [InlineData("disable")]
    public async Task Concurrent_verifiers_can_consume_a_code_only_once(string operation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await fixture.VerifyTogetherAsync("123456", operation);
        Assert.Single(results, accepted => accepted);
        await using var check = fixture.Open();
        Assert.Null((await check.Users.SingleAsync()).TwoFactorCodeHash);
        Assert.False(await new AuthService(check, null!, null!).VerifyTwoFactorCodeAsync(fixture.UserId, "123456"));
    }

    [Fact]
    public async Task Concurrent_wrong_codes_exhaust_the_challenge_instead_of_losing_failure_counts()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.All(await fixture.VerifyTogetherAsync("000000", "login"), accepted => Assert.False(accepted));
        await using var check = fixture.Open();
        Assert.Null((await check.Users.SingleAsync()).TwoFactorCodeHash);
        Assert.False(await new AuthService(check, null!, null!).VerifyTwoFactorCodeAsync(fixture.UserId, "123456"));
    }

    [Fact]
    public async Task The_fifth_attempt_can_succeed_but_a_replaced_challenge_cannot_be_consumed_by_a_stale_request()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var service = new AuthService(db, null!, null!);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.False(await service.VerifyTwoFactorCodeAsync(fixture.UserId, "000000"));
        }
        Assert.True(await service.VerifyTwoFactorCodeAsync(fixture.UserId, "123456"));

        var row = await db.Users.SingleAsync();
        row.TwoFactorCodeHash = PasswordHasher.Hash("123456");
        row.TwoFactorCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();
        await using var stale = fixture.Open();
        await stale.Users.SingleAsync();
        row.TwoFactorCodeHash = PasswordHasher.Hash("123456");
        await db.SaveChangesAsync();
        Assert.False(await new AuthService(stale, null!, null!).VerifyTwoFactorCodeAsync(fixture.UserId, "123456"));
        Assert.True(await service.VerifyTwoFactorCodeAsync(fixture.UserId, "123456"));
    }

    [Fact]
    public void Concurrency_metadata_matches_the_postgres_model_snapshot_without_schema_changes()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options, new FakeCurrentTenant(Guid.NewGuid()));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class Fixture(DbContextOptions<AppDbContext> options, string? schema) : IAsyncDisposable
    {
        private readonly FakeCurrentTenant _tenant = new(Guid.NewGuid());
        public Guid UserId { get; } = Guid.NewGuid();
        public AppDbContext Open() => new(options, _tenant);

        public static async Task<Fixture> CreateAsync(bool postgres = false)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>();
            string? schema = null;
            if (postgres)
            {
                schema = "mfa_test_" + Guid.NewGuid().ToString("N");
                builder.UseNpgsql(new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_POSTGRES"))
                {
                    SearchPath = schema,
                }.ConnectionString);
            }
            if (!postgres)
            {
                builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            }
            var fixture = new Fixture(builder.Options, schema);
            await using var seed = fixture.Open();
            if (schema is not null)
            {
                await seed.Database.OpenConnectionAsync();
                await using var setup = seed.Database.GetDbConnection().CreateCommand();
                setup.CommandText = $"CREATE SCHEMA \"{schema}\";\n" + seed.Database.GenerateCreateScript();
                await setup.ExecuteNonQueryAsync();
            }
            seed.Users.Add(new UserRow
            {
                Id = fixture.UserId, Email = "audit@example.test", DisplayName = "Audit", Role = "Owner",
                PasswordHash = "unused", TwoFactorEnabled = true,
                TwoFactorCodeHash = PasswordHasher.Hash("123456"),
                TwoFactorCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            });
            await seed.SaveChangesAsync();
            return fixture;
        }

        public async Task<bool[]> VerifyTogetherAsync(string code, string operation)
        {
            var concurrentOptions = new DbContextOptionsBuilder<AppDbContext>(options).AddInterceptors(new ConcurrentReads(8)).Options;
            var contexts = Enumerable.Range(0, 8).Select(_ => new AppDbContext(concurrentOptions, _tenant)).ToArray();
            try
            {
                return await Task.WhenAll(contexts.Select(db =>
                {
                    var service = new AuthService(db, null!, null!);
                    return operation switch
                    {
                        "setup" => service.ConfirmTwoFactorSetupAsync(UserId, code),
                        "disable" => service.DisableTwoFactorAsync(UserId, code),
                        _ => service.VerifyTwoFactorCodeAsync(UserId, code),
                    };
                }));
            }
            finally
            {
                foreach (var db in contexts)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = Open();
            if (schema is not null)
            {
                await db.Database.OpenConnectionAsync();
                await using var cleanup = db.Database.GetDbConnection().CreateCommand();
                cleanup.CommandText = $"DROP SCHEMA \"{schema}\" CASCADE";
                await cleanup.ExecuteNonQueryAsync();
                return;
            }
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ConcurrentReads(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _arrived) == participants)
            {
                _ready.SetResult();
            }
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return result;
        }
    }
}
