using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Workload;

public interface ITypeSafeRateBudget
{
    Task AcquireAsync(int tokens, CancellationToken ct);
    Task CooldownAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>One organization allowance shared by all callers and replicas using the application database.</summary>
public sealed class TypeSafeRateBudget(IServiceScopeFactory scopes, IOptions<ModelReviewOptions> options) : ITypeSafeRateBudget
{
    public async Task AcquireAsync(int tokens, CancellationToken ct)
    {
        var limits = options.Value;
        if (limits.RequestsPerSecond <= 0 || limits.TokensPerSecond <= 0)
            throw new InvalidOperationException("Configure PlaceContext:TypeSafe:RequestsPerSecond and TokensPerSecond from the organization's developer console before AI processing.");
        if (tokens <= 0 || tokens > limits.TokensPerSecond)
            throw new InvalidOperationException("AI request exceeds the configured per-second token budget. Reduce the batch size.");
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.OpenConnectionAsync(ct);
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("scope", limits.RateLimitScope);
            command.CommandText = """
                INSERT INTO typesafe_rate_budgets (scope) VALUES (@scope) ON CONFLICT DO NOTHING;
                SELECT scope FROM typesafe_rate_budgets WHERE scope = @scope FOR UPDATE;
                """;
            await command.ExecuteNonQueryAsync(ct);
            command.CommandText = """
                DELETE FROM typesafe_rate_admissions WHERE scope = @scope AND admitted_at <= clock_timestamp() - interval '1 second';
                SELECT clock_timestamp(), cooldown_until,
                    (SELECT count(*) FROM typesafe_rate_admissions WHERE scope = @scope),
                    (SELECT coalesce(sum(tokens), 0) FROM typesafe_rate_admissions WHERE scope = @scope),
                    (SELECT min(admitted_at) FROM typesafe_rate_admissions WHERE scope = @scope)
                FROM typesafe_rate_budgets WHERE scope = @scope;
                """;
            DateTime now, cooldown;
            DateTime? oldest;
            long requests, used;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                now = reader.GetDateTime(0);
                cooldown = reader.GetDateTime(1);
                requests = reader.GetInt64(2);
                used = reader.GetInt64(3);
                oldest = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
            }
            var delay = cooldown > now ? cooldown - now : TimeSpan.Zero;
            if (requests >= limits.RequestsPerSecond || used + tokens > limits.TokensPerSecond)
            {
                var window = oldest!.Value.AddSeconds(1) - now + TimeSpan.FromMilliseconds(10);
                if (window > delay) delay = window;
            }
            if (delay <= TimeSpan.Zero)
            {
                command.CommandText = "INSERT INTO typesafe_rate_admissions (scope, admitted_at, tokens) VALUES (@scope, clock_timestamp(), @tokens)";
                command.Parameters.AddWithValue("tokens", tokens);
                await command.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            await db.Database.CloseConnectionAsync();
            if (delay <= TimeSpan.Zero) return;
            await Task.Delay(delay, ct);
        }
    }

    public async Task CooldownAsync(TimeSpan delay, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO typesafe_rate_budgets (scope, cooldown_until)
            VALUES ({options.Value.RateLimitScope}, clock_timestamp() + {delay})
            ON CONFLICT (scope) DO UPDATE SET cooldown_until = greatest(typesafe_rate_budgets.cooldown_until, EXCLUDED.cooldown_until)
            """, ct);
    }
}
