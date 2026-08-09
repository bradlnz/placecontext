using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Domain.Entities;
using PlaceContext.Artifacts.Integration;

namespace PlaceContext.Artifacts.Infrastructure.Integration;

internal sealed record StoreOcrResultRequest(
    Guid ProjectId,
    Guid ArtifactId,
    Guid RunId,
    Guid JobId,
    string? Title,
    string? ContentType,
    string Markdown,
    DateTimeOffset IngestedAt);
