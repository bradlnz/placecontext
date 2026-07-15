using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Tests;

public class ProjectDataReservedNamesTests
{
    [Theory]
    [InlineData("projects", true)]
    [InlineData("Projects", true)]
    [InlineData("JOBS", true)]
    [InlineData("schedules", true)]
    [InlineData("entities", true)]
    [InlineData("my_projects", false)]
    [InlineData("project", false)]
    [InlineData("sites", false)]
    [InlineData("suburb_markets", false)]
    [InlineData("suburb-markets", false)]
    public void IsReserved_api_path_segments(string name, bool reserved)
        => Assert.Equal(reserved, ProjectDataReservedNames.IsReserved(name));

    [Fact]
    public void EnsureAllowed_throws_for_reserved()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ProjectDataReservedNames.EnsureAllowed("entities", "table name"));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureAllowed_passes_for_normal_names()
        => ProjectDataReservedNames.EnsureAllowed("suburb_markets", "table name");

    [Theory]
    [InlineData("Sites", "sites")]
    [InlineData("Suburb Markets", "suburb-markets")]
    [InlineData("foo_bar", "foo-bar")]
    public void Slug_matches_entity_api(string raw, string expected)
        => Assert.Equal(expected, ProjectDataReservedNames.Slug(raw));
}
