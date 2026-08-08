using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record SendEmailChainActionView(
    string Recipient,
    string RecipientName,
    string Subject,
    string Body,
    string AttachmentPath = "")
    : ChainActionView(SendEmailChainAction.ActionType, "Send email");
