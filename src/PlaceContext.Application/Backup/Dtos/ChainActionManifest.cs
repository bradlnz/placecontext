using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ChainActionManifest(
    string Type,
    string? Recipient = null,
    string? RecipientName = null,
    string? Subject = null,
    string? Body = null,
    string? AttachmentPath = null);
