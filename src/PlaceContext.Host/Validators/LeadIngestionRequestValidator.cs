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
