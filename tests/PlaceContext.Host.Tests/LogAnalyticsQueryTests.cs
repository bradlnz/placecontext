using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class LogAnalyticsQueryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T03:00:00Z");
    private static QueryLogEvent Entry(string job, string level, string message, string? time = "2026-10-25T02:31:00Z") => new(
        new RunReportView(Guid.NewGuid(), job, "Demo", new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Succeeded", Now, Now, [], null, new("code", "python", null, null, null, 1, 1, false))),
        new JobLogEvent(1, "stderr", time, level, message));
    private static QueryLogEvent[] Events => [Entry("A", "ERROR", "{\"durationMs\":10,\"user\":\"x\"}"),
        Entry("A", "ERROR", "{\"durationMs\":30,\"user\":\"y\"}"), Entry("B", "INFO", "plain | text", "2026-10-25T02:36:00Z")];

    [Fact]
    public void Azure_where_contains_works_on_json_fields_and_after_aggregation()
    {
        var result = new LogAnalyticsQuery("""logs | where message contains "PLAIN | TEXT" and message !contains "missing" | project job, message""", Now).Execute(Events);
        Assert.Equal("B", Assert.Single(result.Rows).Cell("job"));
        Assert.Single(new LogAnalyticsQuery("""where json.user contains "X" """, Now).Execute(Events).Rows);
        Assert.Empty(new LogAnalyticsQuery("""where json.user contains_cs "X" """, Now).Execute(Events).Rows);
        var aggregate = new LogAnalyticsQuery("""summarize count() as events by job | where job contains "a" """, Now).Execute(Events);
        Assert.Equal("2", Assert.Single(aggregate.Rows).Cell("events"));
        var mapped = new LogAnalyticsQuery("""where message contains "Y" """, Now)
            .Execute(Events, new Dictionary<string, string> { ["message"] = "json.user" });
        Assert.Equal("y", Assert.Single(mapped.Rows).Cell("message"));
    }

    [Fact]
    public void Pipeline_filters_aggregates_orders_and_limits_rows()
    {
        var result = new LogAnalyticsQuery("logs | filter level = ERROR | stats count() as errors by job | sort errors desc | limit 1", Now).Execute(Events);
        Assert.Equal(["job", "errors"], result.Columns);
        Assert.Equal("A", Assert.Single(result.Rows).Cell("job"));
        Assert.Equal("2", result.Rows[0].Cell("errors"));
        Assert.Equal(2, result.MatchedEvents);
        Assert.True(result.Aggregated);
        Assert.Null(result.Rows[0].Source);
    }

    [Fact]
    public void Json_numeric_metrics_support_aliases_and_post_aggregation_filters()
    {
        var result = new LogAnalyticsQuery("stats avg(json.durationMs) as average, sum(json.durationMs) as total, min(json.durationMs) as low, max(json.durationMs) as high, count_distinct(json.user) as users by job | where average >= 20 | project job, average, total, low, high, users", Now).Execute(Events);
        var row = Assert.Single(result.Rows);
        Assert.Equal(["A", "20", "40", "10", "30", "2"], result.Columns.Select(row.Cell));
    }

    [Fact]
    public void Time_buckets_are_utc_aligned_and_missing_times_are_a_separate_group()
    {
        var result = new LogAnalyticsQuery("stats count() as events by bin(timestamp, 5m), level | sort timestamp asc", Now)
            .Execute(Events.Append(Entry("C", "WARN", "no time", null)));
        Assert.Equal(3, result.Rows.Count);
        Assert.StartsWith("2026-10-25T02:30:00", result.Rows[0].Cell("timestamp"));
        Assert.Equal("2", result.Rows[0].Cell("events"));
        Assert.Equal("", result.Rows[^1].Cell("timestamp"));
    }

    [Fact]
    public void Projection_preserves_run_drilldown_and_quoted_pipes_are_not_commands()
    {
        var result = new LogAnalyticsQuery("filter message ~ 'plain | text' | fields @timestamp, job as worker, message | limit 1", Now).Execute(Events);
        Assert.Equal(["timestamp", "worker", "message"], result.Columns);
        Assert.Equal("B", Assert.Single(result.Rows).Cell("worker"));
        Assert.NotNull(result.Rows[0].Source);
    }

    [Fact]
    public void Numeric_sort_and_pipeline_order_are_respected()
    {
        var data = Enumerable.Range(0, 12).Select(_ => Entry("A", "INFO", "x")).Concat(Events);
        var result = new LogAnalyticsQuery("stats count() as events by job | sort events desc | take 1", Now).Execute(data);
        Assert.Equal("14", Assert.Single(result.Rows).Cell("events"));
        var limitedFirst = new LogAnalyticsQuery("limit 1 | summarize count() as events", Now).Execute(data);
        Assert.Equal("1", Assert.Single(limitedFirst.Rows).Cell("events"));
    }

    [Fact]
    public void Empty_aggregate_and_missing_numeric_values_have_defined_results()
    {
        var result = new LogAnalyticsQuery("filter level = FATAL | stats count() as events, avg(json.durationMs) as duration", Now).Execute(Events);
        Assert.Equal("0", Assert.Single(result.Rows).Cell("events"));
        Assert.Equal("", result.Rows[0].Cell("duration"));
        Assert.Empty(new LogAnalyticsQuery("filter level = FATAL | stats count() by job", Now).Execute(Events).Rows);
    }

    [Fact]
    public void Field_mappings_apply_before_filtering_and_preserve_source_fields_and_run_links()
    {
        var entry = Entry("A", "", """{"event":{"at":"2026-10-25T02:32:00Z","severity":"warning","text":"retry","source":"worker"}}""");
        var mapping = new Dictionary<string, string>
        {
            ["timestamp"] = "json.event.at", ["level"] = "json.event.severity",
            ["message"] = "json.event.text", ["stream"] = "json.event.source",
        };
        var result = new LogAnalyticsQuery("filter level = WARN AND timestamp >= -1h | fields timestamp, message, stream, json.event.severity", Now)
            .Execute([entry], mapping);
        var row = Assert.Single(result.Rows);
        Assert.StartsWith("2026-10-25T02:32:00", row.Cell("timestamp"));
        Assert.Equal("retry", row.Cell("message"));
        Assert.Equal("worker", row.Cell("stream"));
        Assert.Equal("warning", row.Cell("json.event.severity"));
        Assert.Same(entry, row.Source);
        Assert.Contains("json.event.text", result.SourceFields);
        Assert.Single(new LogAnalyticsQuery("level = WARN", Now).Execute([entry], mapping).Rows);
    }

    [Fact]
    public void Missing_or_invalid_mapped_values_keep_detected_fields()
    {
        var entry = Entry("A", "INFO", """{"at":"invalid","text":null}""");
        var mapping = new Dictionary<string, string> { ["timestamp"] = "json.at", ["level"] = "json.absent", ["message"] = "json.text" };
        var row = Assert.Single(new LogAnalyticsQuery("logs", Now).Execute([entry], mapping).Rows);
        Assert.StartsWith("2026-10-25T02:31:00", row.Cell("timestamp"));
        Assert.Equal("INFO", row.Cell("level"));
        Assert.Equal(entry.Event.Message, row.Cell("message"));
        Assert.Throws<ArgumentException>(() => new LogAnalyticsQuery("logs", Now).Execute([entry], new Dictionary<string, string> { ["run"] = "json.id" }));
    }

    [Theory]
    [InlineData("logs | unknown job")]
    [InlineData("logs || limit 2")]
    [InlineData("fields secret")]
    [InlineData("fields job | filter level = ERROR")]
    [InlineData("stats count() as job by job")]
    [InlineData("stats avg(secret)")]
    [InlineData("stats count() by bin(0m)")]
    [InlineData("sort job sideways")]
    [InlineData("limit 501")]
    [InlineData("filter")]
    [InlineData("filter message = 'unclosed")]
    public void Invalid_pipeline_is_rejected(string query) => Assert.Throws<ArgumentException>(() => new LogAnalyticsQuery(query, Now).Execute(Events));

    [Fact]
    public void Aggregates_scan_all_loaded_events_before_the_display_cap()
    {
        var data = Enumerable.Range(0, 1500).Select(_ => Events[0]);
        Assert.Equal("1500", Assert.Single(new LogAnalyticsQuery("stats count()", Now).Execute(data).Rows).Cell("count"));
        var result = new LogAnalyticsQuery("fields message", Now).Execute(data);
        Assert.Equal(1500, result.TotalRows);
        Assert.Equal(500, result.Rows.Count);
    }
}
