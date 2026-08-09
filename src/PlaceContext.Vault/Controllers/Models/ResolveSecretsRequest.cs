using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Vault.Controllers;

public sealed record ResolveSecretsRequest(IReadOnlyList<string> Names);
