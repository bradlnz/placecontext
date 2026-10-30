using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Dashboards;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tests.AgentWorkflows;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

public sealed class PersonalDashboardTests
{
    [Fact]
    public async Task Saves_encrypted_layout_and_prevents_stale_updates_or_deletes()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service();
        var first = await service.SaveAsync(f.Project, new(Guid.Empty, " My dashboard ", [chart.Id], "Is A larger?", Guid.Empty));
        Assert.Equal("My dashboard", first.Name);
        await using var db = f.Db();
        Assert.DoesNotContain("My dashboard", (await db.Set<PersonalDashboardRow>().SingleAsync()).Definition);
        var second = await service.SaveAsync(f.Project, first with { Name = "Updated" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(f.Project, first.Id, first.Version));
        Assert.Equal(second, Assert.Single(await service.ListAsync(f.Project)) with { ChartIds = second.ChartIds });
        await service.DeleteAsync(f.Project, second.Id, second.Version);
        Assert.Empty(await service.ListAsync(f.Project)); Assert.Single(await service.ChartsAsync(f.Project));
    }

    [Fact]
    public async Task Other_users_and_tenants_cannot_read_modify_or_delete_a_dashboard()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync();
        var saved = await f.Service().SaveAsync(f.Project, new(Guid.Empty, "Private", [chart.Id], "", Guid.Empty));
        var otherUser = f.Service(user: new AgentTestUser());
        Assert.Empty(await otherUser.ListAsync(f.Project));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherUser.SaveAsync(f.Project, saved));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherUser.DeleteAsync(f.Project, saved.Id, saved.Version));
        var tenant = Guid.NewGuid(); var otherProject = Guid.NewGuid(); await f.SeedAsync(tenant, otherProject);
        var otherTenant = f.Service(tenant);
        Assert.Empty(await otherTenant.ListAsync(otherProject));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherTenant.ListAsync(f.Project));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherTenant.SaveAsync(otherProject, saved));
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherTenant.DeleteAsync(otherProject, saved.Id, saved.Version));
        Assert.NotEqual(chart.Id, Assert.Single(await otherTenant.ChartsAsync(otherProject)).Id);
    }

    [Fact]
    public async Task Foreign_project_charts_and_missing_permission_are_rejected()
    {
        await using var f = new DashboardFixture(); await f.SeedAsync();
        var foreign = await f.SeedAsync(project: Guid.NewGuid()); var service = f.Service();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, new(Guid.Empty, "Invalid", [foreign.Id], "", Guid.Empty)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(f.Project, [foreign.Id], new DashboardSnapshot([foreign]).Fingerprint, "Is this valid?"));
        Assert.Equal(0, f.Reviewer.Calls);
        f.Permissions.Denied.Add(Permission.DataRead);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(f.Project));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChartsAsync(f.Project));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AnalyzeAsync(f.Project, [foreign.Id], "x", "Question?"));
    }

    [Fact]
    public async Task Jev_receives_exact_displayed_values_and_rejects_stale_or_oversized_evidence()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service();
        var snapshot = new DashboardSnapshot([chart]);
        var result = await service.AnalyzeAsync(f.Project, [chart.Id], snapshot.Fingerprint, "Is A larger?");
        Assert.Equal(f.Project, f.Reviewer.ProjectId); Assert.Equal(snapshot.Evidence, f.Reviewer.Evidence); Assert.Equal(snapshot.Fingerprint, result.Fingerprint);
        Assert.Equal("Supported by this snapshot", result.Verdict);
        await using var db = f.Db(); var row = await db.ProjectCharts.SingleAsync();
        row.GeneratedAt = row.GeneratedAt.AddSeconds(1); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(f.Project, [chart.Id], snapshot.Fingerprint, "Is A larger?"));
        var huge = chart.Spec with { Labels = [new string('x', 61_000), "B"] };
        row.Html = f.Encryption.Protect(huge.ToJson(), IDataEncryptor.Purpose.Chart); await db.SaveChangesAsync();
        var updated = new DashboardSnapshot(await service.ChartsAsync(f.Project));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(f.Project, [chart.Id], updated.Fingerprint, "Is A larger?"));
        Assert.Equal(1, f.Reviewer.Calls);
    }

    [Theory]
    [InlineData(-.1)] [InlineData(1.1)] [InlineData(double.NaN)]
    public async Task Invalid_model_scores_never_appear_as_analysis(double score)
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); f.Reviewer.Score = score;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service().AnalyzeAsync(f.Project, [chart.Id], new DashboardSnapshot([chart]).Fingerprint, "Is A larger?"));
    }

    [Fact]
    public async Task Upstream_errors_are_sanitized_and_duplicate_charts_are_rejected()
    {
        await using var f = new DashboardFixture(); var chart = await f.SeedAsync(); var service = f.Service(); f.Reviewer.Fail = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AnalyzeAsync(f.Project, [chart.Id], new DashboardSnapshot([chart]).Fingerprint, "Is A larger?"));
        Assert.DoesNotContain("private provider", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(f.Project, new(Guid.Empty, "Invalid", [chart.Id, chart.Id], "", Guid.Empty)));
    }
}
