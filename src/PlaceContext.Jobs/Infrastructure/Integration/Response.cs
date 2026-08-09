using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

internal sealed record Response(Guid Id);
