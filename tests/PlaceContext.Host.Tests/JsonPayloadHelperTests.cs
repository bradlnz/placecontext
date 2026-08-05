using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Tests;

public sealed class JsonPayloadHelperTests
{
    [Fact]
    public void FlattenScalars_takes_first_occurrence_and_skips_nested()
    {
        var result = JsonPayloadHelper.FlattenScalars(
            new[]
            {
                """{"address":"A","nested":{"x":1},"tags":[1],"n":42,"ok":true}""",
                """{"address":"B","extra":"z"}""",
            }
        );

        Assert.Equal("A", result["address"]);
        Assert.Equal("42", result["n"]);
        Assert.Equal("True", result["ok"]);
        Assert.Equal("z", result["extra"]);
        Assert.False(result.ContainsKey("nested"));
        Assert.False(result.ContainsKey("tags"));
    }

    [Fact]
    public void FlattenScalars_ignores_invalid_json()
    {
        var result = JsonPayloadHelper.FlattenScalars(new[] { "not-json", """{"a":"1"}""" });
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1" }, result);
    }

    [Fact]
    public void FlattenScalars_retains_file_marker_as_json()
    {
        var result = JsonPayloadHelper.FlattenScalars(
            new[]
            {
                """{"source_file":{"$file":{"bucket":"reports","key":"job-inputs/p/plan.pdf"}}}""",
            }
        );

        Assert.Equal(
            """{"$file":{"bucket":"reports","key":"job-inputs/p/plan.pdf"}}""",
            result["source_file"]
        );
    }
}
