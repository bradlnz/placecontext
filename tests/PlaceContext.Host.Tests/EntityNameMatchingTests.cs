using PlaceContext.Application.Features;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Tests;

public class EntityNameMatchingTests
{
    private static DataEntityView Entity(string name, string table) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            table,
            "id",
            Array.Empty<EntityRelationDto>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow
        );

    [Theory]
    [InlineData("Sites", "sites_table", "Sites", true)]
    [InlineData("Sites", "sites_table", "sites", true)]
    [InlineData("Sites", "sites_table", "sites_table", true)]
    [InlineData("Suburb Markets", "suburb_markets", "suburb-markets", true)]
    [InlineData("Sites", "sites_table", "listings", false)]
    public void EntityNameMatches_name_table_and_slug(
        string name,
        string table,
        string request,
        bool expected
    ) => Assert.Equal(expected, EntitiesController.EntityNameMatches(Entity(name, table), request));

    [Theory]
    [InlineData("Sites", "sites")]
    [InlineData("Suburb Markets", "suburb-markets")]
    [InlineData("foo_bar", "foo-bar")]
    public void Slug_normalises_spaces_and_underscores(string raw, string expected) =>
        Assert.Equal(expected, EntitiesController.Slug(raw));
}
