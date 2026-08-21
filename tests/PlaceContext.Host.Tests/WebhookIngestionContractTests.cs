using Microsoft.AspNetCore.Http;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class WebhookIngestionContractTests
{
    [Theory]
    [InlineData("X-Ingest-Key", "ingest-secret")]
    [InlineData("X-Api-Key", "api-style-secret")]
    [InlineData("Authorization", "Bearer bearer-secret")]
    public void Ingestion_accepts_each_documented_authentication_shape(string header, string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[header] = value;

        var expected = header == "Authorization" ? "bearer-secret" : value;
        Assert.Equal(expected, IngestController.PresentedKey(context.Request));
    }

    [Fact]
    public void Purpose_built_ingest_header_wins_when_multiple_headers_are_present()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Ingest-Key"] = "webhook";
        context.Request.Headers["X-Api-Key"] = "generic";
        context.Request.Headers.Authorization = "Bearer bearer";

        Assert.Equal("webhook", IngestController.PresentedKey(context.Request));
    }

    [Fact]
    public void Webhook_template_has_a_real_handler_for_every_builtin_job_language()
    {
        var template = JobTemplateCatalog.GetById("webhook-receiver");

        Assert.NotNull(template);
        Assert.Equal(
            new[] { "node", "python", "go", "ruby", "dotnet" },
            template!.SourcesByRuntime.Keys);
        Assert.All(template.SourcesByRuntime.Values, source => Assert.False(string.IsNullOrWhiteSpace(source)));
        Assert.DoesNotContain("WEBHOOK_SIGNATURE", template.MapSource);
        Assert.Empty(template.RequiredCredentials);
    }

    [Fact]
    public void Controller_exposes_both_the_legacy_and_api_ingestion_routes()
    {
        var source = ReadHostSource("Controllers/IngestController.cs");

        Assert.Contains("[HttpPost(\"/ingest/{eventName}\")]", source);
        Assert.Contains("[HttpPost(\"/api/ingest/{eventName}\")]", source);
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
