namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainActionResponse(
    string Type,
    string DisplayName,
    string? Recipient,
    string? RecipientName,
    string? Subject,
    string? Body,
    string? AttachmentPath);
