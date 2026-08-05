using System.Text.Json;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class CrmIngestionValidatorTests
{
    private readonly CrmIngestionPayloadValidator _validator = new(
        new LeadIngestionRequestValidator()
    );

    [Fact]
    public async Task Arbitrary_address_payload_is_valid_automation_input()
    {
        using var document = JsonDocument.Parse(
            """{"address":"123 Example Street, Brisbane QLD 4000","options":{"detail":"full"}}"""
        );

        var result = await _validator.ValidateAsync(document.RootElement);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Contact_form_payload_uses_lead_validation_rules()
    {
        using var document = JsonDocument.Parse(
            """{"name":"Ada Lovelace","email":"not-an-email"}"""
        );

        var result = await _validator.ValidateAsync(document.RootElement);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.ErrorMessage == "Enter a valid email address."
        );
    }

    [Fact]
    public async Task Null_json_is_rejected()
    {
        using var document = JsonDocument.Parse("null");

        var result = await _validator.ValidateAsync(document.RootElement);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.ErrorMessage == "A JSON payload is required."
        );
    }
}
