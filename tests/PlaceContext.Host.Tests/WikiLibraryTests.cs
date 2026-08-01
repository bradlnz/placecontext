using PlaceContext.Host.Wiki;

namespace PlaceContext.Host.Tests;

public sealed class WikiLibraryTests
{
    [Fact]
    public void Current_user_guides_are_embedded_and_ordered_for_everyday_readers()
    {
        var slugs = WikiLibrary.Articles.Select(article => article.Slug).ToList();

        Assert.Equal("getting-started", slugs[0]);
        Assert.Contains("crm-and-communications", slugs);
        Assert.Contains("events-and-schedules", slugs);
        Assert.Contains("security-and-sharing", slugs);
        Assert.True(slugs.IndexOf("crm-and-communications") < slugs.IndexOf("jobs-and-artifacts"));
        Assert.True(slugs.IndexOf("jobs-and-artifacts") < slugs.IndexOf("events-and-schedules"));

        Assert.Contains("Switch between light and dark mode",
            WikiLibrary.Find("getting-started")!.Html);
        Assert.Contains("Share an artifact publicly",
            WikiLibrary.Find("jobs-and-artifacts")!.Html);
        Assert.Contains("How customer information is protected",
            WikiLibrary.Find("crm-and-communications")!.Html);
        Assert.Contains("Test an event manually",
            WikiLibrary.Find("events-and-schedules")!.Html);
        Assert.Contains("View tests",
            WikiLibrary.Find("jobs-and-artifacts")!.Html);
        Assert.Contains("api/v1/search",
            WikiLibrary.Find("project-data")!.Html);
    }
}
