using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Search.Integration;

namespace PlaceContext.Search.Infrastructure.Integration;

internal sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
