using System.Data;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Billing;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Billing;

/// <summary>
/// PostgreSQL row-locked run allowance. Reservations are atomic per tenant across replicas.
/// A top-level execution counts once, regardless of retry attempts or workflow child jobs.
/// </summary>
public sealed class DbRunAllowance(AppDbContext db, ICurrentTenant tenant, IClock clock) : IRunAllowance
{
    private static DateTimeOffset MonthStart(DateTimeOffset now)
        => new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<RunAllowanceSnapshot> GetAsync(CancellationToken ct = default)
    {
        RequireTenant();
        var plan = await db.TenantRunPlans.AsNoTracking().SingleOrDefaultAsync(ct);
        var now = clock.UtcNow;
        if (plan is null)
            return new("free", "active", RunPlans.FreeRuns, 0, MonthStart(now), MonthStart(now).AddMonths(1));
        var used = plan.Plan == "free" && plan.PeriodEnd <= now ? 0 : plan.UsedRuns;
        return new(plan.Plan, plan.Status, plan.IncludedRuns, used,
            plan.Plan == "free" && plan.PeriodEnd <= now ? MonthStart(now) : plan.PeriodStart,
            plan.Plan == "free" && plan.PeriodEnd <= now ? MonthStart(now).AddMonths(1) : plan.PeriodEnd);
    }

    public async Task ReserveAsync(Guid projectId, Guid executionId, string kind,
        CancellationToken ct = default)
    {
        RequireTenant();
        if (projectId == Guid.Empty || executionId == Guid.Empty || kind is not ("workflow" or "job"))
            throw new ArgumentException("A project, execution id and known billable run kind are required.");
        var now = clock.UtcNow;
        var period = MonthStart(now);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // Establish the free plan exactly once. Paid entitlements can only be changed by a
        // verified billing event. A user cannot bypass quotas by changing metadata in the request.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tenant_run_plans ("TenantId", "Plan", "Status", "IncludedRuns", "UsedRuns",
                "PeriodStart", "PeriodEnd", "LastStripeEventCreated")
            VALUES ({tenant.TenantId}, {"free"}, {"active"}, {RunPlans.FreeRuns}, {0},
                {period}, {period.AddMonths(1)}, {0L})
            ON CONFLICT ("TenantId") DO NOTHING
            """, ct);

        var plan = await db.TenantRunPlans.FromSqlInterpolated(
            $"""SELECT * FROM tenant_run_plans WHERE "TenantId" = {tenant.TenantId} FOR UPDATE""")
            .SingleAsync(ct);
        var key = $"{kind}:{executionId:N}";
        // The lock is acquired BEFORE inspecting the unique execution key or allowance.
        if (await db.BillableRuns.AnyAsync(r => r.RunKey == key, ct))
        {
            await tx.CommitAsync(ct);
            return;
        }
        if (plan.Plan == "free" && plan.PeriodEnd <= now)
        {
            plan.PeriodStart = period;
            plan.PeriodEnd = period.AddMonths(1);
            plan.UsedRuns = 0;
        }
        if (plan.Status is not ("active" or "trialing") || now < plan.PeriodStart
            || now >= plan.PeriodEnd || plan.IncludedRuns <= plan.UsedRuns)
            throw new InvalidOperationException(
                "This workspace has reached its run allowance or its billing period is not active. Review the plan before starting another run.");

        plan.UsedRuns++;
        db.BillableRuns.Add(new BillableRunRow
        {
            TenantId = tenant.TenantId, RunKey = key, ProjectId = projectId,
            Kind = kind, PeriodStart = plan.PeriodStart, ReservedAt = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void RequireTenant()
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("A resolved workspace is required to reserve a run.");
    }
}
