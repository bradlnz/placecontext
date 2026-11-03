using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Shared;
using PlaceContext.Application.Tenancy;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>Atomic, unlimited per-tenant usage accounting and per-plan start pacing.
/// Row locks coordinate the next run start across replicas; no run volume is blocked.</summary>
public sealed class DbRunAllowance(
    AppDbContext db, ICurrentTenant tenant, IClock clock,
    IOptions<RunBillingDeploymentOptions>? deployment = null,
    IOptions<PaymentBillingOptions>? stripe = null) : IRunAllowance
{
    private static DateTimeOffset MonthStart(DateTimeOffset now)
        => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    private bool Hosted => deployment?.Value.ManagedHosting ?? false;

    private int RateFor(string plan) => plan == RunPlans.Enterprise
        ? stripe?.Value.ContractFor(tenant.TenantId)?.MaxStartsPerMinute
            ?? throw new InvalidOperationException(
                "Enterprise execution capacity must be configured for this tenant.")
        : RunPlans.StartsPerMinute(plan);

    public async Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default)
    {
        RequireTenant();
        var plan = await db.TenantRunPlans.AsNoTracking().SingleOrDefaultAsync(ct);
        var now = clock.UtcNow;
        if (plan is null)
        {
            return new(RunPlans.Developer, Hosted ? "self_hosted_only" : "active", 0,
                        MonthStart(now), MonthStart(now).AddMonths(1), null);
        }
        var free = plan.Plan == RunPlans.Developer;
        var expiredFree = free && plan.PeriodEnd <= now;
        var status = free && Hosted ? "self_hosted_only" : plan.Status;
        int? startsPerMinute;
        try
        {
            var rate = RateFor(plan.Plan);
            startsPerMinute = rate == 0 ? null : rate;
        }
        catch (ArgumentException)
        {
            status = "configuration_required";
            startsPerMinute = null;
        }
        catch (InvalidOperationException)
        {
            status = "configuration_required";
            startsPerMinute = null;
        }

        return new(plan.Plan, status, expiredFree ? 0 : plan.UsedRuns,
            expiredFree ? MonthStart(now) : plan.PeriodStart,
            expiredFree ? MonthStart(now).AddMonths(1) : plan.PeriodEnd,
            startsPerMinute, !string.IsNullOrWhiteSpace(plan.StripeCustomerId));
    }

    public async Task ReserveAsync(Guid projectId, Guid executionId, string kind,
        CancellationToken ct = default)
    {
        RequireTenant();
        if (projectId == Guid.Empty || executionId == Guid.Empty || kind is not ("workflow" or "job"))
        {
            throw new ArgumentException("A project, execution id and known billable run kind are required.");
        }
        var now = clock.UtcNow;
        var period = MonthStart(now);
        var startAt = now;
        var spaced = false;

        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            // Free self-hosted Developer is automatic; managed SaaS rejects free runs.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tenant_run_plans ("TenantId", "Plan", "Status", "IncludedRuns", "UsedRuns",
                    "PeriodStart", "PeriodEnd", "LastStripeEventCreated")
                VALUES ({tenant.TenantId}, {RunPlans.Developer}, {"active"}, {RunPlans.Unlimited}, {0L},
                    {period}, {period.AddMonths(1)}, {0L})
                ON CONFLICT ("TenantId") DO NOTHING
                """, ct);

            var plan = await db.TenantRunPlans.FromSqlInterpolated(
                $"""SELECT * FROM tenant_run_plans WHERE "TenantId" = {tenant.TenantId} FOR UPDATE""")
                .SingleAsync(ct);
            // A reused DbContext might already track an older plan snapshot. Reload
            // after acquiring the row lock so the pacing cursor and counter are current.
            await db.Entry(plan).ReloadAsync(ct);
            var key = $"{kind}:{executionId:N}";
            if (plan.Plan == RunPlans.Developer && Hosted)
            {
                throw new InvalidOperationException(
                                "Developer Edition is self-hosted only. Choose a managed subscription on hosted PlaceContext.");
            }
            if (plan.Plan == RunPlans.Developer && plan.PeriodEnd <= now)
            {
                plan.PeriodStart = period;
                plan.PeriodEnd = period.AddMonths(1);
                plan.UsedRuns = 0;
            }
            if (plan.Status is not ("active" or "trialing") || now < plan.PeriodStart
                || now >= plan.PeriodEnd)
            {
                throw new InvalidOperationException(
                                "This workspace does not have an active subscription period. Review its billing status.");
            }
            // A previously admitted request may have been cancelled while waiting,
            // before its chain/job row was created. Reuse the original durable
            // dispatch slot instead of letting a retry bypass the tenant's pace.
            var existing = await db.BillableRuns.AsNoTracking()
                .SingleOrDefaultAsync(r => r.RunKey == key, ct);
            async Task ReserveAllowanceAsync()
            {
                if (existing is not null)
                {
                    startAt = existing.DispatchAfter ?? now;
                    spaced = existing.DispatchAfter.HasValue;
                    await tx.CommitAsync(ct);
                    return;
                }
                {
                    var startsPerMinute = RateFor(plan.Plan);
                    if (startsPerMinute > 0)
                    {
                        // Admission schedule is shared by all replicas but not a lifetime quota.
                        if (plan.NextDispatchAt is { } upcoming && upcoming > startAt)
                        {
                            startAt = upcoming;
                        }
                        var spacing = TimeSpan.FromTicks(Math.Max(1,
                                                TimeSpan.TicksPerMinute / startsPerMinute));
                        plan.NextDispatchAt = startAt.Add(spacing);
                        spaced = true;
                    }

                    plan.IncludedRuns = RunPlans.Unlimited;
                    checked
                    {
                        plan.UsedRuns++;
                    }
                    db.BillableRuns.Add(new BillableRunRow
                    {
                        TenantId = tenant.TenantId,
                        RunKey = key,
                        ProjectId = projectId,
                        Kind = kind,
                        PeriodStart = plan.PeriodStart,
                        ReservedAt = now,
                        DispatchAfter = spaced ? startAt : null
                    });
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
            }
            await ReserveAllowanceAsync();
        }

        // The DB lock is released before waiting. The existing RPC/scheduled queues
        // are durable; direct portal requests may time out in a long burst.
        if (spaced)
        {
            while (true)
            {
                var remaining = startAt - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }
                await Task.Delay(remaining > TimeSpan.FromMinutes(1)
                                    ? TimeSpan.FromMinutes(1) : remaining, ct);
            }
            // A paid run that waited must not launch after its subscription lapses.
            if (!(await GetAsync(ct)).CanRun)
            {
                throw new InvalidOperationException(
                                "The subscription became inactive before this queued run could start.");
            }
        }
    }

    private void RequireTenant()
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A resolved workspace is required to start a run.");
        }
    }
}
