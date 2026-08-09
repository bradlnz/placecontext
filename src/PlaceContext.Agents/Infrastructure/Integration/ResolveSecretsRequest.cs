using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Agents.Cluster;

namespace PlaceContext.Agents.Infrastructure.Integration;

internal sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
