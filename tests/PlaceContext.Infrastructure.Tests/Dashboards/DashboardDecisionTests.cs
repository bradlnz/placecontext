using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

public sealed class DashboardDecisionTests
{
    [Fact]
    public async Task Targets_round_trip_encrypted_and_legacy_layouts_still_open_without_targets()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service();
        var target = new DashboardWatchTarget(chart.Id, "Count", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 10);
        var saved = await service.SaveAsync(f.Project, new(Guid.Empty, "Watched dashboard", [chart.Id], "Question?", Guid.Empty, [target]));
        var loaded = Assert.Single(await service.ListAsync(f.Project));
        Assert.Equal(target, Assert.Single(loaded.Targets!));
        await using var db = f.Db(); var row = await db.Set<PersonalDashboardRow>().SingleAsync();
        Assert.DoesNotContain("Threshold", row.Definition);
        Assert.DoesNotContain("Watched dashboard", row.Definition);
        row.Definition = f.Encryption.Protect(JsonSerializer.Serialize(new
        {
            saved.Id, saved.Name, saved.ChartIds, saved.Question, saved.Version
        }), "personal.dashboard.v1");
        await db.SaveChangesAsync();
        var legacy = Assert.Single(await service.ListAsync(f.Project));
        Assert.Null(legacy.Targets);
        Assert.Equal(saved.Question, legacy.Question);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, saved with { Targets = [target, target] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, saved with { Targets = [target with { SeriesName = "missing" }] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, saved with { Targets = [target with { Threshold = double.NaN }] }));
    }

    [Fact]
    public async Task Explicit_evaluation_uses_exact_candidate_chart_and_measured_policy_with_project_scoping()
    {
        await using var f = new DashboardFixture(); var first = await f.SeedAsync();
        var second = await AddChart(f, "secondary_source", [9, 3]); var service = f.Service();
        var snapshot = new DashboardSnapshot(await service.ChartsAsync(f.Project));
        var target = new DashboardWatchTarget(second.Id, "Calls", DashboardMeasureStatistic.Mean, DashboardTargetDirection.AtMost, 5);
        var signal = DashboardSignals.Create(snapshot, f.Clock.UtcNow, [target]).Single(x => x.ChartId == second.Id);
        var fingerprint = DashboardSignals.Fingerprint(snapshot, [target]);
        var result = await service.DecideAsync(f.Project, snapshot.Charts.Select(x => x.Id).ToArray(), fingerprint, [signal.Id], [target]);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DashboardDecisionStatus.Review, decision.Status);
        Assert.Equal(.95, decision.Probability);
        Assert.Equal("jev-test", decision.Model);
        Assert.Equal(fingerprint, decision.Fingerprint);
        Assert.Equal(fingerprint, result.Fingerprint);
        Assert.Equal(1, result.ModelCalls);
        var request = Assert.Single(f.Reviewer.Requests);
        Assert.Equal(f.Project, request.ProjectId);
        Assert.Equal(signal.Question, request.Question);
        Assert.Contains(second.Id.ToString(), request.Evidence);
        Assert.DoesNotContain(first.Id.ToString(), request.Evidence);
        var evidence = JsonNode.Parse(request.Evidence!)!;
        Assert.Equal(6, evidence["signal"]!["Measurement"]!["Mean"]!.GetValue<double>());
        Assert.Equal("Mean", evidence["signal"]!["Target"]!["Statistic"]!.GetValue<string>());
        Assert.Equal("AtMost", evidence["signal"]!["Target"]!["Direction"]!.GetValue<string>());
        Assert.True(evidence["signal"]!["TargetBreached"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Stale_and_incompatible_target_evidence_skip_Jev_instead_of_reusing_a_recommendation()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service();
        await using (var db = f.Db())
        {
            (await db.ProjectCharts.SingleAsync()).GeneratedAt = f.Clock.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }
        var snapshot = new DashboardSnapshot(await service.ChartsAsync(f.Project));
        var signals = DashboardSignals.Create(snapshot, f.Clock.UtcNow);
        f.Reviewer.Fail = true;
        var result = await service.DecideAsync(f.Project, [chart.Id], DashboardSignals.Fingerprint(snapshot), [signals[0].Id], null);
        Assert.Equal(DashboardDecisionStatus.NeedsData, Assert.Single(result.Decisions).Status);
        Assert.Null(result.Decisions[0].Probability);
        Assert.Equal(0, result.ModelCalls);
        Assert.Equal(0, f.Reviewer.Calls);
        var target = new DashboardWatchTarget(chart.Id, "removed_measure", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 10);
        result = await service.DecideAsync(f.Project, [chart.Id], DashboardSignals.Fingerprint(snapshot, [target]), [signals[0].Id], [target]);
        Assert.Equal(DashboardDecisionStatus.NeedsData, Assert.Single(result.Decisions).Status);
        Assert.Contains("missing or ambiguous", result.Decisions[0].Rationale);
        Assert.Equal(0, f.Reviewer.Calls);
    }

    [Fact]
    public async Task Policy_or_snapshot_changes_and_forged_or_duplicate_signals_are_rejected_before_spending()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service();
        var snapshot = new DashboardSnapshot([chart]);
        var target = new DashboardWatchTarget(chart.Id, "Count", DashboardMeasureStatistic.Maximum, DashboardTargetDirection.AtMost, 10);
        var signal = Assert.Single(DashboardSignals.Create(snapshot, f.Clock.UtcNow, [target]));
        var fingerprint = DashboardSignals.Fingerprint(snapshot, [target]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, [signal.Id], [target with { Threshold = 20 }]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, ["forged"], [target]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, [signal.Id, signal.Id], [target]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, Enumerable.Repeat(signal.Id, 13).ToArray(), [target]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, [signal.Id], [target with { ChartId = Guid.NewGuid() }]));
        await using var db = f.Db(); (await db.ProjectCharts.SingleAsync()).GeneratedAt = f.Clock.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(f.Project, [chart.Id], fingerprint, [signal.Id], [target]));
        Assert.Equal(0, f.Reviewer.Calls);
    }

    [Fact]
    public async Task Decision_evaluation_keeps_project_and_permission_boundaries()
    {
        await using var f = new DashboardFixture(); await f.SeedAsync(); var foreign = await f.SeedAsync(project: Guid.NewGuid());
        var snapshot = new DashboardSnapshot([foreign]); var signal = Assert.Single(DashboardSignals.Create(snapshot, f.Clock.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().DecideAsync(f.Project, [foreign.Id], DashboardSignals.Fingerprint(snapshot), [signal.Id], null));
        f.Permissions.Denied.Add(Permission.DataRead);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service().DecideAsync(f.Project, [foreign.Id], "fingerprint", [signal.Id], null));
        Assert.Equal(0, f.Reviewer.Calls);
    }

    [Theory]
    [InlineData(-.1)] [InlineData(1.1)] [InlineData(double.NaN)]
    public async Task Invalid_scores_remain_unavailable_without_a_recommendation(double score)
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var snapshot = new DashboardSnapshot([chart]);
        f.Reviewer.Score = score;
        var signal = Assert.Single(DashboardSignals.Create(snapshot, f.Clock.UtcNow));
        var result = await f.Service().DecideAsync(f.Project, [chart.Id], DashboardSignals.Fingerprint(snapshot), [signal.Id], null);
        Assert.Equal(DashboardDecisionStatus.Unavailable, Assert.Single(result.Decisions).Status);
        Assert.Null(result.Decisions[0].Probability);
        Assert.Equal(1, result.ModelCalls);
    }

    [Fact]
    public async Task One_provider_failure_is_sanitized_without_losing_another_signal_result()
    {
        await using var f = new DashboardFixture(); var first = await f.SeedAsync(); var second = await AddChart(f, "other_calls", [7, 2]);
        var service = f.Service(); var snapshot = new DashboardSnapshot(await service.ChartsAsync(f.Project));
        f.Reviewer.Handler = (_, state, _, _) => state!.Contains(first.Id.ToString())
            ? throw new InvalidOperationException("private provider details")
            : Task.FromResult(new JsonObject { ["noul"] = .95, ["model"] = "jev-test" });
        var signals = DashboardSignals.Create(snapshot, f.Clock.UtcNow);
        var result = await service.DecideAsync(f.Project, [first.Id, second.Id], DashboardSignals.Fingerprint(new([first, second])),
            signals.Select(x => x.Id).ToArray(), null);
        Assert.Equal(2, result.ModelCalls);
        Assert.Contains(result.Decisions, x => x.Status == DashboardDecisionStatus.Unavailable);
        Assert.Contains(result.Decisions, x => x.Status == DashboardDecisionStatus.Review);
        Assert.DoesNotContain("private provider", string.Join(" ", result.Decisions.Select(x => x.Rationale)));
    }

    [Fact]
    public async Task Provider_calls_are_bounded_to_two_in_flight_and_results_preserve_requested_order()
    {
        await using var f = new DashboardFixture(); var first = await f.SeedAsync();
        var second = await AddChart(f, "calls_2", [2, 4]); var third = await AddChart(f, "calls_3", [3, 6]);
        var fourth = await AddChart(f, "calls_4", [4, 8]);
        var snapshot = new DashboardSnapshot([first, second, third, fourth]);
        var ids = DashboardSignals.Create(snapshot, f.Clock.UtcNow).Select(x => x.Id).Reverse().ToArray();
        using var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maximum = 0; var sync = new object();
        f.Reviewer.Handler = async (_, _, ct, _) =>
        {
            lock (sync) { active++; maximum = Math.Max(maximum, active); }
            entered.Release();
            try { await release.Task.WaitAsync(ct); return new JsonObject { ["noul"] = .95 }; }
            finally { lock (sync) active--; }
        };
        var pending = f.Service().DecideAsync(f.Project, snapshot.Charts.Select(x => x.Id).ToArray(), DashboardSignals.Fingerprint(snapshot), ids, null);
        try
        {
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(2, f.Reviewer.Calls);
        }
        finally { release.TrySetResult(); }
        var result = await pending;
        Assert.Equal(2, maximum);
        Assert.Equal(4, result.ModelCalls);
        Assert.Equal(ids, result.Decisions.Select(x => x.SignalId));
    }

    [Fact]
    public async Task Cancellation_discards_a_late_success_even_when_the_reviewer_ignores_the_token()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync();
        var snapshot = new DashboardSnapshot([chart]);
        var signal = Assert.Single(DashboardSignals.Create(snapshot, f.Clock.UtcNow));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        f.Reviewer.Handler = async (_, _, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new JsonObject { ["noul"] = .95, ["model"] = "late-success" };
        };
        var pending = f.Service().DecideAsync(f.Project, [chart.Id], DashboardSignals.Fingerprint(snapshot), [signal.Id], null, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, f.Reviewer.Calls);
    }

    private static async Task<DashboardChart> AddChart(DashboardFixture f, string source, double[] values)
    {
        var chart = new DashboardChart(Guid.NewGuid(), source,
            new("bar", source, values.Select((_, i) => $"Category {i}").ToArray(), [new("Calls", values)]), f.Clock.UtcNow, source);
        await using var db = f.Db();
        db.ProjectCharts.Add(new() { Id = chart.Id, ProjectId = f.Project, TableName = source,
            Html = f.Encryption.Protect(chart.Spec.ToJson(), IDataEncryptor.Purpose.Chart), GeneratedAt = chart.GeneratedAt });
        await db.SaveChangesAsync();
        return chart;
    }
}
