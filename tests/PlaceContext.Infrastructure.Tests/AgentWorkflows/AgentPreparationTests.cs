using System.Text.Json;
using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Tests.AgentWorkflows;

public sealed class AgentPreparationTests
{
    [Fact]
    public async Task Core_project_data_prepares_an_agent_without_optional_forms_or_model_calls()
    {
        await using var f = new AgentWorkflowFixture(); await f.SeedAsync();
        f.Data.Tables["queue_metrics"] = Page(["queue", "offered", "abandoned"], [["Support", "100", "12"]]);
        f.Data.Tables["payroll"] = Page(["private"], [["unrelated"]]);

        var result = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");

        Assert.Empty(result.MissingFields);
        Assert.Equal("", result.Inputs["serviceTargets"]);
        var source = Assert.Single(result.Sources);
        Assert.Equal("queue_metrics", source.Name); Assert.Null(source.UpdatedAt);
        var read = Assert.Single(f.Data.Reads);
        Assert.Equal(f.ProjectId, read.ProjectId); Assert.Equal("queue_metrics", read.Table);
        Assert.Equal(8, read.PageSize); Assert.False(read.IncludeCount);
        Assert.Equal(0, f.Model.Calls);
        using var data = JsonDocument.Parse(result.Inputs["queueMetrics"]);
        Assert.True(data.RootElement.GetProperty("sampled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("sourceUpdatedAt").ValueKind);
        Assert.Contains("not verified", result.Notice);

        var check = await f.Service().RunAsync(f.ProjectId, "call-abandonment-checklist", Guid.NewGuid(), JsonSerializer.Serialize(result.Inputs));
        Assert.Equal(AgentRunStatus.Completed, check.Status); Assert.Equal(0, f.Model.Calls);
        Assert.Contains("Contents have not been verified", check.Output);
    }

    [Fact]
    public async Task Data_permission_and_project_tenant_access_are_checked_before_discovery()
    {
        await using var f = new AgentWorkflowFixture(); await f.SeedAsync();
        f.Permissions.Denied.Add(Permission.DataRead);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service().PrepareAsync(f.ProjectId, "call-abandonment"));
        Assert.Equal(0, f.Data.ListCalls); Assert.Empty(f.Data.Reads);
        f.Permissions.Denied.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service(Guid.NewGuid()).PrepareAsync(f.ProjectId, "call-abandonment"));
        Assert.Equal(0, f.Data.ListCalls); Assert.Equal(0, f.Model.Calls);
    }

    [Fact]
    public async Task Ambiguous_empty_and_unrelated_tables_do_not_satisfy_required_inputs()
    {
        await using var f = new AgentWorkflowFixture(); await f.SeedAsync();
        f.Data.Tables["queue_metrics"] = Page(["queue"], [["Support"]]);
        f.Data.Tables["call_queue_metrics"] = Page(["queue"], [["Sales"]]);
        var ambiguous = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");
        Assert.Equal(["queueMetrics"], ambiguous.MissingFields); Assert.Empty(f.Data.Reads);
        Assert.Contains("multiple tables", ambiguous.Notice);

        f.Data.Tables.Remove("call_queue_metrics");
        f.Data.Tables["queue_metrics"] = Page(["queue"], [[" "], [null]]);
        var empty = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");
        Assert.Equal(["queueMetrics"], empty.MissingFields); Assert.Empty(empty.Sources);

        f.Data.Tables.Clear(); f.Data.Reads.Clear();
        f.Data.Tables["unrelated_queue_notes"] = Page(["queue"], [["Not a metrics table"]]);
        var unrelated = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");
        Assert.Equal(["queueMetrics"], unrelated.MissingFields); Assert.Empty(f.Data.Reads);
    }

    [Fact]
    public async Task Escaped_context_is_bounded_and_valid_before_the_outer_run_payload_is_sent()
    {
        await using var f = new AgentWorkflowFixture(); await f.SeedAsync();
        var value = string.Concat(Enumerable.Repeat("quoted \"value\" <tag>\n", 300));
        foreach (var table in new[] { "products", "sales", "stock" })
            f.Data.Tables[table] = Page(["id", "details"], Enumerable.Range(0, 8)
                .Select(i => (IReadOnlyList<string?>)new string?[] { i.ToString(), value }).ToArray());

        var result = await f.Service().PrepareAsync(f.ProjectId, "retail");
        var payload = JsonSerializer.Serialize(result.Inputs);
        Assert.InRange(payload.Length, 1, 16000);
        Assert.NotEmpty(result.Sources);
        foreach (var input in result.Inputs.Values.Where(x => x.Length > 0))
        {
            using var sample = JsonDocument.Parse(input);
            Assert.True(sample.RootElement.GetProperty("valuesOrColumnsClipped").GetBoolean());
            Assert.InRange(sample.RootElement.GetProperty("rows").GetArrayLength(), 1, 8);
        }
        Assert.All(result.Sources, source => Assert.Null(source.UpdatedAt));
        var check = await f.Service().RunAsync(f.ProjectId, "retail-checklist", Guid.NewGuid(), payload);
        Assert.NotEqual(AgentRunStatus.Failed, check.Status);
    }

    [Fact]
    public async Task Unreadable_sources_remain_missing_and_do_not_leak_database_errors()
    {
        await using var f = new AgentWorkflowFixture(); await f.SeedAsync();
        f.Data.Tables["queue_metrics"] = Page(["queue"], [["Support"]]);
        f.Data.FailReads.Add("queue_metrics");
        var failedRead = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");
        Assert.Equal(["queueMetrics"], failedRead.MissingFields); Assert.Empty(failedRead.Sources);
        Assert.Contains("could not be read", failedRead.Notice); Assert.DoesNotContain("Private", failedRead.Notice);
        f.Data.FailList = true;
        var failedList = await f.Service().PrepareAsync(f.ProjectId, "call-abandonment");
        Assert.Contains("unavailable", failedList.Notice); Assert.DoesNotContain("Private", failedList.Notice);
        Assert.Empty(failedList.Sources); Assert.Equal(0, f.Model.Calls);
    }

    private static ProjectTablePageResult Page(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows) =>
        new(columns, rows, rows.Count, 1, 8, false);
}
