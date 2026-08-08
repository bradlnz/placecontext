using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>Describes one credential that must be stored in the project vault before the
/// template can run successfully.</summary>
public sealed record JobCredentialRequirement(
    string Name,
    string Description,
    string EnvVarName);
