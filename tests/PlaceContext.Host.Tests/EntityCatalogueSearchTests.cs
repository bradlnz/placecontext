using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class EntityCatalogueSearchTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("  CUSTOMERS  ", true)]
    [InlineData("accounts", true)]
    [InlineData("support", true)]
    [InlineData("orders", true)]
    [InlineData("missing", false)]
    public void Catalogue_search_matches_names_tables_tags_and_relationships(string search, bool expected)
    {
        var entity = new DataEntityView(Guid.NewGuid(), Guid.NewGuid(), "Customers", "customer_accounts", "name",
            [new EntityRelationDto("id", "Orders", "customer_id")], ["support"], DateTimeOffset.UtcNow);

        Assert.Equal(expected, DataEntitiesViewModel.MatchesSearch(entity, search));
    }
}
