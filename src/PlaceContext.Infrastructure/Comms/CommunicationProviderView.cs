using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Comms;

public sealed record CommunicationProviderView(
    Guid Id,
    string Channel,
    string Kind,
    string Name,
    bool Enabled,
    bool IsDefault,
    bool UseForTwoFactor,
    string AuthType,
    string? AuthHeaderName,
    Guid? VaultProjectId,
    string? ApiKeySecretName,
    string SettingsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
