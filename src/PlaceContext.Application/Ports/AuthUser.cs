using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>An authenticated portal user (scoped to a tenant).</summary>
public sealed record AuthUser(Guid Id, Guid TenantId, string Email, string DisplayName);
