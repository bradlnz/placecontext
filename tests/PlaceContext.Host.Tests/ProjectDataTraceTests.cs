using System.Text.Json;
using PlaceContext.Application;
using PlaceContext.Application.Activity;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Host.Tools;

namespace PlaceContext.Host.Tests;

public sealed class ProjectDataTraceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Data_tool_traces_omit_sql_names_results_and_errors_but_preserve_the_call_result(bool chart, bool fail)
    {
        var log = new TraceLog();
        var dispatcher = new Dispatcher(fail);
        var service = new PlaceContextService(dispatcher, null!, null!, null!);
        var project = Guid.NewGuid();
        var sql = "SELECT 'private-marker' AS customer";
        Task<string> Call() => chart
            ? PlaceContextTools.SaveSqlChart(service, log, project, "private-marker", sql)
            : PlaceContextTools.QueryProjectData(service, log, project, sql);

        if (fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Call);
        }
        if (!fail)
        {
            Assert.Contains("private-marker", await Call());
        }

        var entry = Assert.Single(log.Entries);
        Assert.Equal(project.ToString(), entry.Project);
        Assert.Equal(fail ? ToolCallStatus.Error : ToolCallStatus.Ok, entry.Status);
        Assert.DoesNotContain("private-marker", JsonSerializer.Serialize(entry));
        Assert.DoesNotContain("SELECT", JsonSerializer.Serialize(entry));
    }

    private sealed class TraceLog : IToolCallLog
    {
        public List<ToolCallEntry> Entries { get; } = [];
        public void Record(ToolCallEntry entry) => Entries.Add(entry);
        public IReadOnlyList<ToolCallEntry> Recent(int take = 100) => Entries.Take(take).ToArray();
    }

    private sealed class Dispatcher(bool fail) : IDispatcher
    {
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default)
        {
            if (fail)
            {
                throw new InvalidOperationException("Database error contains private-marker");
            }
            object result = command is ExecuteProjectDataCommand
                ? new ProjectQueryResult(["customer"], [["private-marker"]], 0, false)
                : new ProjectChartView("sql:private-marker", "private-marker", DateTimeOffset.UtcNow);
            return Task.FromResult((T)result);
        }
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
