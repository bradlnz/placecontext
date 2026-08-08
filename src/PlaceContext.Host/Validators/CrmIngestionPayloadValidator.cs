using System.Text.Json;
using FluentValidation;

namespace PlaceContext.Host.Controllers;

/// <summary>
/// Accepts arbitrary JSON, applying contact-form constraints only when the object has the fields
/// needed to create or update a lead. Other shapes are opaque automation input.
/// </summary>
public sealed class CrmIngestionPayloadValidator : AbstractValidator<JsonElement>
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public CrmIngestionPayloadValidator(IValidator<LeadIngestionRequest> leadValidator)
    {
        RuleFor(payload => payload).CustomAsync(async (payload, context, ct) =>
        {
            if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                context.AddFailure("A JSON payload is required.");
                return;
            }
            if (payload.ValueKind != JsonValueKind.Object) return;

            var lead = payload.Deserialize<LeadIngestionRequest>(WebJson);
            if (lead is null
                || string.IsNullOrWhiteSpace(lead.Name)
                || string.IsNullOrWhiteSpace(lead.Email) && string.IsNullOrWhiteSpace(lead.Phone))
                return;

            var result = await leadValidator.ValidateAsync(lead, ct);
            foreach (var error in result.Errors)
                context.AddFailure(error.PropertyName, error.ErrorMessage);
        });
    }
}
