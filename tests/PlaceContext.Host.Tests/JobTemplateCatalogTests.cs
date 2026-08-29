using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobTemplateCatalogTests
{
    [Fact]
    public void New_job_catalogue_excludes_third_party_integrations()
    {
        Assert.DoesNotContain(JobTemplateCatalog.All, template => template.Category == "Integrations");
        Assert.Null(JobTemplateCatalog.GetById("hubspot-contacts"));
        Assert.Null(JobTemplateCatalog.GetById("xero-invoices"));
        Assert.Null(JobTemplateCatalog.GetById("shopify-orders"));
        Assert.Null(JobTemplateCatalog.GetById("postgres-query"));
    }
}
