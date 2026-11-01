using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobTemplateCatalogTests
{
    [Fact]
    public void Data_quality_job_is_executable_without_external_credentials_or_network_access()
    {
        var quality = Assert.IsType<JobTemplate>(JobTemplateCatalog.GetById("json-data-quality"));
        Assert.Equal("Data quality", quality.Category);
        Assert.Equal("python", quality.MapRuntimeId);
        Assert.Equal("main.py", quality.MapEntrypoint);
        Assert.Equal(PlaceContext.Domain.ValueObjects.JobReturnType.Json, quality.ReturnType);
        Assert.False(quality.AllowNetworkEgress);
        Assert.Empty(quality.RequiredCredentials);
        Assert.Contains("REQUIRED_FIELDS", quality.MapEnvRaw);
        Assert.Contains("UNIQUE_FIELD", quality.MapEnvRaw);
        Assert.Contains("MAX_NULL_RATE", quality.MapEnvRaw);
        Assert.Contains("FIELD_TYPES_JSON", quality.MapEnvRaw);
        Assert.Contains("sys.exit(0 if passed else 1)", quality.MapSource);
        Assert.Equal(quality.MapSource, quality.SourcesByRuntime["python"]);
        Assert.Single(quality.SourcesByRuntime);
    }

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
