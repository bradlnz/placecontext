using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>An authenticated portal user (a member of one organisation), with their role.</summary>
public sealed record AuthUser(Guid Id, Guid TenantId, string Email, string DisplayName, UserRole Role);
