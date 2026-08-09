namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainActionResponse(
    string Type,
    string DisplayName,
    string? Recipient,
    string? RecipientName,
    string? Subject,
    string? Body,
    string? AttachmentPath);
