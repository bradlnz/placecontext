using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

internal sealed record StoreJobArtifactRequest(
    Guid ProjectId,
    Guid JobId,
    Guid RunId,
    string JobName,
    string Kind,
    string FileName,
    string Title,
    string ContentType,
    string ContentBase64);
