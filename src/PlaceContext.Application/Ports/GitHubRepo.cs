using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>A GitHub repository visible to a connected account.</summary>
public sealed record GitHubRepo(
    string FullName, string Name, string CloneUrl, bool Private, string? Description, string? Language, DateTimeOffset? PushedAt);
