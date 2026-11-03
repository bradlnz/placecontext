using System.Text.Json;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Application.Tests;

public sealed class SourcePreparationTests
{
    private static DataSourceDefinition Source() => new(Guid.NewGuid(), Guid.NewGuid(), "Customers", "public", "customers", "id",
        ["id", "name", "email", "active"], ["customers"], false, Guid.NewGuid(),
        Queue: new(Guid.NewGuid(), ["name"], ["email"]));

    [Fact]
    public void Preparation_filters_trims_redacts_preserves_types_and_deduplicates()
    {
        var source = Source();
        var documents = SourcePreparation.Prepare(source,
            [new("1", "{\"id\":1,\"name\":\" Ada \",\"email\":\"private@example.com\",\"active\":true,\"secret\":\"never-store\"}"),
             new("1", "{\"active\":true,\"email\":\"private@example.com\",\"name\":\"Ada\",\"id\":1}")], DateTimeOffset.UtcNow);
        var document = Assert.Single(documents);
        Assert.Null(document.ValidationError);
        using var json = JsonDocument.Parse(document.Json);
        Assert.Equal("Ada", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("[REDACTED]", json.RootElement.GetProperty("email").GetString());
        Assert.True(json.RootElement.GetProperty("active").GetBoolean());
        Assert.DoesNotContain("private@example.com", document.Json);
        Assert.DoesNotContain("secret", document.Json);
        Assert.Empty(document.Chunks);
    }

    [Theory]
    [InlineData("{\"id\":1,\"name\":\" \"}")]
    [InlineData("{\"id\":1,\"name\":null}")]
    [InlineData("{\"id\":2,\"name\":\"Ada\"}")]
    [InlineData("{\"id\":1,\"name\":\"Ada\",\"name\":\"Bob\"}")]
    [InlineData("not JSON")]
    public void Invalid_records_are_held_without_becoming_retrievable(string json)
    {
        var source = Source();
        var document = Assert.Single(SourcePreparation.Prepare(source, [new("1", json)], DateTimeOffset.UtcNow));
        Assert.NotNull(document.ValidationError);
        Assert.False(DataSourcePipeline.IsRetrievable(source, document));
        Assert.Empty(document.Chunks);
    }

    [Fact]
    public void Conflicting_keys_are_held_instead_of_silently_overwriting_a_record()
    {
        var document = Assert.Single(SourcePreparation.Prepare(Source(),
            [new("1", "{\"id\":1,\"name\":\"Ada\"}"), new("1", "{\"id\":1,\"name\":\"Bob\"}")], DateTimeOffset.UtcNow));
        Assert.Contains("Conflicting", document.ValidationError);
        Assert.DoesNotContain("Ada", document.Json);
        Assert.DoesNotContain("Bob", document.Json);
    }

    [Fact]
    public void Rules_cannot_redact_keys_reference_unapproved_fields_or_run_models_before_queueing()
    {
        var source = Source();
        Assert.Throws<ArgumentException>(() => DataSourceRules.Validate(source with { Queue = source.Queue! with { RedactedFields = ["id"] } }));
        Assert.Throws<ArgumentException>(() => DataSourceRules.Validate(source with { Queue = source.Queue! with { RequiredFields = ["secret"] } }));
        Assert.Throws<ArgumentException>(() => DataSourceRules.Validate(source with { EnableEmbeddings = true }));
        Assert.Throws<ArgumentException>(() => DataSourceRules.Validate(source with { Processing = new("Run a model") }));
    }
}
