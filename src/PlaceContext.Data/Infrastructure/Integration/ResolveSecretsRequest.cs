using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Data.Integration;

namespace PlaceContext.Data.Infrastructure.Integration;

internal sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
