using System.Text.Json;
using FluentValidation;

namespace PlaceContext.Host.Controllers;

public sealed class LeadIngestionRequestValidator : AbstractValidator<LeadIngestionRequest>
{
    public LeadIngestionRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name is too long.");
        RuleFor(request => request)
            .Must(request => !string.IsNullOrWhiteSpace(request.Email)
                || !string.IsNullOrWhiteSpace(request.Phone))
            .WithMessage("Email or phone is required.");
        When(request => !string.IsNullOrWhiteSpace(request.Email), () =>
            RuleFor(request => request.Email)
                .MaximumLength(320).WithMessage("Enter a valid email address.")
                .EmailAddress().WithMessage("Enter a valid email address."));
        RuleFor(request => request.Phone)
            .MaximumLength(80).WithMessage("Phone is too long.");
        RuleFor(request => request.Company)
            .MaximumLength(300).WithMessage("Company is too long.");
        RuleFor(request => request.Message)
            .MaximumLength(10_000).WithMessage("Message is too long.");
        RuleFor(request => request.Source)
            .MaximumLength(200).WithMessage("Source is too long.");
        RuleFor(request => request.Address)
            .MaximumLength(1_000).WithMessage("Address is too long.");
        RuleFor(request => request.Metadata)
            .Must(metadata => metadata is null || metadata.Count <= 30)
            .WithMessage("Metadata has too many fields.");
    }
}

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
