using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

internal sealed record ProcessJobResultRequest(
    string SourceKind,
    Guid SourceId,
    Guid RunId,
    Guid ProjectId,
    string? PrimaryOutput,
    IReadOnlyList<JobResultDocument> Documents);
