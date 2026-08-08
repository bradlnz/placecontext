using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Xunit;

namespace PlaceContext.Application.Tests;

public class ChartSpecTests
{
    // ── parsing model output ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Parses_bare_fenced_and_prose_wrapped_json()
    {
        const string bare = "{\"type\":\"bar\",\"title\":\"t\",\"labels\":[\"a\",\"b\"],\"series\":[{\"name\":\"n\",\"values\":[1,2]}]}";
        foreach (var raw in new[] { bare, "```json\n" + bare + "\n```", "Here is the chart:\n" + bare + "\nEnjoy!" })
        {
            var spec = ChartSpec.TryParse(raw);
            Assert.NotNull(spec);
            Assert.Equal("bar", spec!.Type);
            Assert.Equal(new[] { 1d, 2d }, spec.Series[0].Values);
        }
    }

    [Theory]
    [InlineData("{\"type\":\"scatter\",\"labels\":[\"a\"],\"series\":[{\"name\":\"n\",\"values\":[1]}]}")] // unknown type
    [InlineData("{\"type\":\"bar\",\"labels\":[],\"series\":[{\"name\":\"n\",\"values\":[1]}]}")]           // no labels
    [InlineData("{\"type\":\"bar\",\"labels\":[\"a\",\"b\"],\"series\":[{\"name\":\"n\",\"values\":[1]}]}")] // short series
    [InlineData("{\"type\":\"bar\",\"labels\":[\"a\"],\"series\":[]}")]                                      // no series
    [InlineData("not json at all")]
    public void Rejects_unusable_specs(string raw) => Assert.Null(ChartSpec.TryParse(raw));

    [Fact]
    public void Pie_keeps_one_series_and_extra_labels_are_capped()
    {
        var labels = string.Join(",", Enumerable.Range(0, 40).Select(i => $"\"l{i}\""));
        var values = string.Join(",", Enumerable.Range(0, 40));
        var spec = ChartSpec.TryParse(
            $"{{\"type\":\"pie\",\"labels\":[{labels}],\"series\":[{{\"name\":\"a\",\"values\":[{values}]}},{{\"name\":\"b\",\"values\":[{values}]}}]}}");
        Assert.NotNull(spec);
        Assert.Single(spec!.Series);
        Assert.Equal(ChartSpec.MaxLabels, spec.Labels.Count);
        Assert.Equal(ChartSpec.MaxLabels, spec.Series[0].Values.Count);
    }

    // ── deterministic builder ───────────────────────────────────────────────────────────────────────

    private static ProjectQueryResult Result(string[] cols, params string?[][] rows)
        => new(cols, rows.Select(r => (IReadOnlyList<string?>)r.ToList()).ToList(), rows.Length, false);

    [Fact]
    public void Numeric_columns_become_series_over_the_text_column()
    {
        var spec = ChartSpec.FromSample("sales", Result(
            new[] { "region", "units", "revenue" },
            new[] { "north", "3", "30.5" },
            new[] { "south", "5", "51.0" }));

        Assert.NotNull(spec);
        Assert.Equal("bar", spec!.Type);
        Assert.Equal(new[] { "north", "south" }, spec.Labels);
        Assert.Equal(2, spec.Series.Count);
        Assert.Equal("units", spec.Series[0].Name);
        Assert.Equal(new[] { 30.5, 51.0 }, spec.Series[1].Values);
    }

    [Fact]
    public void All_text_tables_chart_value_counts()
    {
        var spec = ChartSpec.FromSample("events", Result(
            new[] { "kind" },
            new[] { "deploy" }, new[] { "deploy" }, new[] { "alert" }));

        Assert.NotNull(spec);
        Assert.Equal(new[] { "deploy", "alert" }, spec!.Labels);
        Assert.Equal(new[] { 2d, 1d }, spec.Series[0].Values);
    }

    [Fact]
    public void Unchartable_data_returns_null()
    {
        Assert.Null(ChartSpec.FromSample("empty", Result(new[] { "a" })));
        // a single distinct text value has nothing to compare
        Assert.Null(ChartSpec.FromSample("flat", Result(new[] { "a" }, new[] { "x" })));
    }
}
