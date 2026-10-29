using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record ChainActionManifest(
    string Type,
    string? Recipient = null,
    string? RecipientName = null,
    string? Subject = null,
    string? Body = null,
    string? AttachmentPath = null,
    string? Instructions = null,
    string? ResponseField = null,
    string? ResponseType = null,
    string? ImageProfile = null,
    string? Command = null,
    string? OutputField = null,
    int MemoryMb = 1024,
    int Cpus = 1,
    int TimeoutSeconds = 300,
    bool AllowNetwork = false,
    string? AgentProvider = null);
