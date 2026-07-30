using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Tests;

public sealed class ArtifactViewConfigTests
{
    [Fact]
    public void CategoryFor_MatchesPrefixesCaseInsensitively()
    {
        var config = new ArtifactViewConfig(
        [
            new ArtifactCategoryRule(
                "feasibility-reports",
                "Feasibility Reports",
                ["feasibility_v1_", "feasibility_v2_"]),
        ]);

        Assert.Equal("feasibility-reports", config.CategoryFor("FEASIBILITY_V1_Site-42.pdf"));
        Assert.Equal("feasibility-reports", config.CategoryFor("feasibility_v2_site-42.png"));
        Assert.Null(config.CategoryFor("site-plan.png"));
    }

    [Fact]
    public void CategoryFor_UsesFirstMatchingRule()
    {
        var config = new ArtifactViewConfig(
        [
            new ArtifactCategoryRule("specific", "Specific", ["report_final_"]),
            new ArtifactCategoryRule("general", "General", ["report_"]),
        ]);

        Assert.Equal("specific", config.CategoryFor("report_final_customer.pdf"));
    }
}
