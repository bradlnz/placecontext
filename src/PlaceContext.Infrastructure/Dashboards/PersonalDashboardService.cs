using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Dashboards;

public sealed class PersonalDashboardService(IServiceScopeFactory scopes, ICurrentTenant tenant, ICurrentUser user,
    IPermissionService permissions, IDataEncryptor encryption, IJevReviewer jev, IClock clock) : IPersonalDashboards
{
    private const string Purpose = "personal.dashboard.v1";
    private async Task CheckAsync(AppDbContext db, Guid projectId, CancellationToken ct)
    {
        if (!tenant.IsResolved || tenant.TenantId == Guid.Empty || !user.IsAuthenticated || user.UserId == Guid.Empty
            || !await permissions.HasAsync(Permission.ProjectsView, ct) || !await permissions.HasAsync(Permission.DataRead, ct))
            throw new UnauthorizedAccessException("You do not have access to dashboards.");
        if (!await db.Projects.AnyAsync(x => x.Id == projectId, ct)) throw new InvalidOperationException("Project not found.");
    }

    public async Task<IReadOnlyList<PersonalDashboard>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var rows = await db.Set<PersonalDashboardRow>().AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.UserId == user.UserId).ToListAsync(ct);
        return rows.Select(x => JsonSerializer.Deserialize<PersonalDashboard>(encryption.Unprotect(x.Definition, Purpose))!
            with { Id = x.Id, Version = x.Version }).OrderBy(x => x.Name).ToArray();
    }

    public async Task<IReadOnlyList<DashboardChart>> ChartsAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        return await ReadChartsAsync(db, projectId, ct);
    }

    private async Task<IReadOnlyList<DashboardChart>> ReadChartsAsync(AppDbContext db, Guid projectId, CancellationToken ct)
    {
        var rows = await db.ProjectCharts.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.TableName).ToListAsync(ct);
        var charts = new List<DashboardChart>();
        foreach (var row in rows)
        {
            var raw = encryption.Unprotect(row.Html, IDataEncryptor.Purpose.Chart);
            if (!raw.TrimStart().StartsWith('{') || ChartSpec.TryParse(raw) is not { } spec) continue;
            charts.Add(new(row.Id, spec.Title ?? row.TableName, spec, row.GeneratedAt, row.TableName));
        }
        return charts;
    }

    private static void ValidateSelection(IReadOnlyList<Guid> ids)
    {
        if (ids is null || ids.Count is < 1 or > 12 || ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("Choose between 1 and 12 different charts.");
    }

    private static DashboardSnapshot Select(IReadOnlyList<DashboardChart> charts, IReadOnlyList<Guid> ids)
    {
        ValidateSelection(ids);
        var lookup = charts.ToDictionary(x => x.Id);
        if (ids.Any(id => !lookup.ContainsKey(id))) throw new InvalidOperationException("A selected chart is unavailable. Refresh and choose your charts again.");
        return new(ids.Select(id => lookup[id]).ToArray());
    }

    public async Task<PersonalDashboard> SaveAsync(Guid projectId, PersonalDashboard dashboard, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        if (string.IsNullOrWhiteSpace(dashboard.Name) || dashboard.Name.Length > 100 || dashboard.Question is null || dashboard.Question.Length > 1000)
            throw new InvalidOperationException("Use a name up to 100 characters and a question up to 1,000 characters.");
        var snapshot = Select(await ReadChartsAsync(db, projectId, ct), dashboard.ChartIds);
        DashboardSignals.ValidateTargets(snapshot, dashboard.Targets);
        PersonalDashboardRow row;
        if (dashboard.Id == Guid.Empty)
        {
            row = new() { Id = Guid.NewGuid(), ProjectId = projectId, UserId = user.UserId };
            db.Set<PersonalDashboardRow>().Add(row);
        }
        else
        {
            row = await db.Set<PersonalDashboardRow>().SingleOrDefaultAsync(x => x.Id == dashboard.Id && x.ProjectId == projectId && x.UserId == user.UserId, ct)
                ?? throw new InvalidOperationException("Dashboard not found.");
            if (row.Version != dashboard.Version) throw new InvalidOperationException("This dashboard changed in another tab. Reopen it before saving.");
        }
        row.Version = Guid.NewGuid();
        var saved = dashboard with { Id = row.Id, Name = dashboard.Name.Trim(), Version = row.Version };
        row.Definition = encryption.Protect(JsonSerializer.Serialize(saved), Purpose);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("This dashboard changed in another tab. Reopen it before saving."); }
        return saved;
    }

    public async Task DeleteAsync(Guid projectId, Guid id, Guid version, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var row = await db.Set<PersonalDashboardRow>().SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId && x.UserId == user.UserId, ct)
            ?? throw new InvalidOperationException("Dashboard not found.");
        if (row.Version != version) throw new InvalidOperationException("This dashboard changed in another tab. Reopen it before deleting.");
        db.Remove(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("This dashboard changed in another tab. Reopen it before deleting."); }
    }

    public async Task<DashboardAnalysis> AnalyzeAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint, string question, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        if (string.IsNullOrWhiteSpace(question) || question.Length > 1000) throw new InvalidOperationException("Ask a yes/no question up to 1,000 characters.");
        var snapshot = Select(await ReadChartsAsync(db, projectId, ct), chartIds);
        if (snapshot.Fingerprint != fingerprint) throw new InvalidOperationException("Chart data has changed. Refresh the snapshots before asking Jev.");
        if (snapshot.Evidence.Length > 60_000) throw new InvalidOperationException("These charts contain too much data for one analysis. Select fewer charts.");
        JsonObject response;
        try { response = await jev.EvaluateAsync(question.Trim(), snapshot.Evidence, ct, projectId); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("Jev analysis is unavailable. Check the host's TypeSafe configuration and try again."); }
        if (response["noul"] is not JsonValue value || !value.TryGetValue<double>(out var score) || !double.IsFinite(score) || score is < 0 or > 1)
            throw new InvalidOperationException("Jev did not return a usable judgment. Try again.");
        return new(question.Trim(), score, clock.UtcNow, snapshot.Fingerprint);
    }

    public async Task<DashboardDecisionSet> DecideAsync(Guid projectId, IReadOnlyList<Guid> chartIds, string fingerprint,
        IReadOnlyList<string> signalIds, IReadOnlyList<DashboardWatchTarget>? targets, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CheckAsync(db, projectId, ct);
        var snapshot = Select(await ReadChartsAsync(db, projectId, ct), chartIds);
        DashboardSignals.ValidateTargetScope(snapshot, targets);
        var currentFingerprint = DashboardSignals.Fingerprint(snapshot, targets);
        if (fingerprint != currentFingerprint)
            throw new InvalidOperationException("Chart data or watch targets have changed. Refresh the decision evidence before evaluating.");
        if (signalIds is null || signalIds.Count is < 1 or > DashboardSignals.MaxDecisions
            || signalIds.Distinct(StringComparer.Ordinal).Count() != signalIds.Count)
            throw new InvalidOperationException("Choose between 1 and 12 different measured signals.");
        var candidates = DashboardSignals.Create(snapshot, clock.UtcNow, targets).ToDictionary(signal => signal.Id, StringComparer.Ordinal);
        if (signalIds.Any(id => id is null || !candidates.ContainsKey(id)))
            throw new InvalidOperationException("A selected signal is unavailable. Refresh the measured signals before evaluating.");
        var selected = signalIds.Select(id => candidates[id]).ToArray();
        var payloads = selected.Where(signal => !signal.NeedsData).ToDictionary(signal => signal.Id,
            signal => DashboardSignals.Evidence(snapshot.Charts.Single(chart => chart.Id == signal.ChartId), signal));
        if (payloads.Values.Any(payload => payload.Length > 60_000))
            throw new InvalidOperationException("A chart contains too much evidence for one decision. Reduce the source chart before evaluating.");

        var calls = 0;
        using var concurrency = new SemaphoreSlim(2);
        async Task<DashboardDecision> Evaluate(DashboardSignal signal)
        {
            ct.ThrowIfCancellationRequested();
            if (signal.NeedsData) return DashboardDecision.ForMissingData(signal, clock.UtcNow, currentFingerprint);
            await concurrency.WaitAsync(ct);
            try
            {
                Interlocked.Increment(ref calls);
                var response = await jev.EvaluateAsync(signal.Question, payloads[signal.Id], ct, projectId);
                if (response["noul"] is not JsonValue value || !value.TryGetValue<double>(out var score)
                    || !double.IsFinite(score) || score is < 0 or > 1)
                    return DashboardDecision.Failed(signal, clock.UtcNow, currentFingerprint);
                var model = response["model"] is JsonValue modelValue && modelValue.TryGetValue<string>(out var modelName)
                    && !string.IsNullOrWhiteSpace(modelName) && modelName.Length <= 160 ? modelName : null;
                return DashboardDecision.FromJudgment(signal, score, model, clock.UtcNow, currentFingerprint);
            }
            catch (OperationCanceledException) { throw; }
            catch { return DashboardDecision.Failed(signal, clock.UtcNow, currentFingerprint); }
            finally { concurrency.Release(); }
        }

        var decisions = await Task.WhenAll(selected.Select(Evaluate));
        ct.ThrowIfCancellationRequested();
        return new(currentFingerprint, clock.UtcNow, decisions, calls);
    }
}
