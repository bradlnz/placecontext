using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ArtifactViewConfigServiceTests
{
    [Fact]
    public void Default_config_has_no_artifact_category_filters()
    {
        var service = new ArtifactViewConfigService(null!, null!);

        Assert.Empty(service.DefaultConfig().Categories);
    }
}
