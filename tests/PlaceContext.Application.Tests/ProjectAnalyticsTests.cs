using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The Analytics tab: table rows go to the local LLM which draws a chart; without an LLM (or when
/// it misbehaves) the handler still returns a themed, deterministic rendering of the data.
/// </summary>
public class ProjectAnalyticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeStore : IProjectDataStore
    {
        public string? SawSql;

        public Task<ProjectQueryResult> ExecuteAsync(Guid projectId, string sql, CancellationToken ct = default)
        {
            SawSql = sql;
            return Task.FromResult(new ProjectQueryResult(
                new[] { "sensor", "value" },
                new[] { new string?[] { "door", "21.5" }, new string?[] { "window", "19.0" } },
                0, false));
        }

        public Task<IReadOnlyList<ProjectTableInfo>> ListTablesAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectTableInfo>>(Array.Empty<ProjectTableInfo>());
        public Task CreateTableAsync(Guid projectId, string tableName, IReadOnlyList<ProjectColumnSpec> columns, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task RenameTableAsync(Guid projectId, string from, string to, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectColumnInfo>> ListColumnsAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProjectColumnInfo>>(new[] { new ProjectColumnInfo("sensor", "text", false, false) });
        public Task AddColumnAsync(Guid projectId, string tableName, ProjectColumnSpec column, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropColumnAsync(Guid projectId, string tableName, string columnName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DropTableAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<string> ExportTableCsvAsync(Guid projectId, string tableName, CancellationToken ct = default)
            => Task.FromResult("");
    }

    private sealed class FakeLlm : ILlmGateway
    {
        public bool IsEnabled { get; set; } = true;
        public string Reply { get; set; } = "<html><head></head><body><svg><text>chart</text></svg></body></html>";
        public string? SawSystem, SawUser;
        public bool Throws;

        public Task<string> GenerateAsync(string system, string user, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("llm down");
            SawSystem = system;
            SawUser = user;
            return Task.FromResult(Reply);
        }
    }

    private static async Task<(InMemoryProjectRepository projects, Project project)> WorldAsync()
    {
        var projects = new InMemoryProjectRepository();
        var project = Project.Discover(ProjectPath.From("/repo/demo"), ProjectName.From("demo"), T0);
        await projects.AddAsync(project);
        return (projects, project);
    }

    [Fact]
    public async Task Llm_chart_is_returned_themed_and_fed_the_table_rows()
    {
        var (projects, project) = await WorldAsync();
        var store = new FakeStore();
        var llm = new FakeLlm();
        var handler = new GenerateProjectChartHandler(projects, store, llm);

        var html = await handler.HandleAsync(new GenerateProjectChartCommand(project.Id.Value, "readings", "average per sensor"));

        Assert.Contains("pc-chart-theme", html);       // themed for the portal
        Assert.Contains("<svg>", html);                // the LLM's chart survived
        Assert.Contains("readings", store.SawSql!);    // sampled the right table
        Assert.Contains("21.5", llm.SawUser!);         // the rows reached the model
        Assert.Contains("average per sensor", llm.SawUser!); // and so did the instruction
    }

    [Fact]
    public async Task Without_an_llm_or_when_it_fails_the_fallback_still_renders_the_data()
    {
        var (projects, project) = await WorldAsync();
        var store = new FakeStore();

        var disabled = new FakeLlm { IsEnabled = false };
        var html1 = await new GenerateProjectChartHandler(projects, store, disabled)
            .HandleAsync(new GenerateProjectChartCommand(project.Id.Value, "readings", null));

        var broken = new FakeLlm { Throws = true };
        var html2 = await new GenerateProjectChartHandler(projects, store, broken)
            .HandleAsync(new GenerateProjectChartCommand(project.Id.Value, "readings", null));

        foreach (var html in new[] { html1, html2 })
        {
            Assert.Contains("pc-chart-theme", html);
            Assert.Contains("21.5", html); // the data itself is visible
        }
    }

    [Fact]
    public async Task Non_html_llm_replies_fall_back_and_unknown_projects_are_rejected()
    {
        var (projects, project) = await WorldAsync();
        var store = new FakeStore();
        var chatty = new FakeLlm { Reply = "Sure! Here is a description of your data with no markup at all." };

        var html = await new GenerateProjectChartHandler(projects, store, chatty)
            .HandleAsync(new GenerateProjectChartCommand(project.Id.Value, "readings", null));
        Assert.Contains("21.5", html); // fallback table, not the chatty reply
        Assert.DoesNotContain("Sure!", html);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GenerateProjectChartHandler(projects, store, chatty)
                .HandleAsync(new GenerateProjectChartCommand(Guid.NewGuid(), "readings", null)));
    }
}
